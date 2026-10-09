using System;
using System.IO;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaLocalFileEvidencePreflightTests : IDisposable
    {
        private const string BindingA =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        private const string BindingB =
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        private readonly string root;

        public MediaLocalFileEvidencePreflightTests()
        {
            root = Path.Combine(Path.GetTempPath(),
                "mle-p4b-synthetic-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        private static MediaAssociationReviewSuggestion Proposal(
            params string[] names)
        {
            return new MediaAssociationReviewSuggestion
            {
                PlayniteId = "025b2cee-c509-4df6-a460-e5c72caf1e70",
                MediaKindHint = "book",
                FileNames = names,
                Rule = "EXACT_TITLE_STEM_REVIEW_ONLY",
                Disposition = "REVIEW_REQUIRED"
            };
        }

        [Fact]
        public void ReadOnlySnapshotKeepsSizeAndTimestampWithoutContentRead()
        {
            var filename = "SyntheticBook.epub";
            var path = Path.Combine(root, filename);
            File.WriteAllText(path, "purely synthetic book bytes");
            var beforeBytes = File.ReadAllBytes(path);
            var snapshot = MediaLocalFileEvidencePreflight.Inspect(
                root, Proposal(filename), BindingA);
            Assert.Equal("mle-local-evidence-preflight/v1", snapshot.Schema);
            Assert.Equal(1, snapshot.Entries.Length);
            Assert.Equal(filename, snapshot.Entries[0].FileName);
            Assert.Equal(beforeBytes.LongLength, snapshot.Entries[0].Length);
            Assert.True(snapshot.Entries[0].LastWriteUtcTicks > 0);
            Assert.Equal(beforeBytes, File.ReadAllBytes(path));
            Assert.True(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                snapshot, root, Proposal(filename), BindingA));
        }

        [Fact]
        public void FileLengthChangeInvalidatesOldEvidence()
        {
            var file = Path.Combine(root, "Changed.pdf");
            File.WriteAllText(file, "first");
            var initial = MediaLocalFileEvidencePreflight.Inspect(
                root, Proposal("Changed.pdf"), BindingA);
            File.WriteAllText(file, "longer replaced synthetic content");
            Assert.False(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, Proposal("Changed.pdf"), BindingA));
        }

        [Fact]
        public void SameSizeWriteTimeChangeInvalidatesOldEvidence()
        {
            var file = Path.Combine(root, "Changed.epub");
            File.WriteAllText(file, "same-size");
            var initial = MediaLocalFileEvidencePreflight.Inspect(
                root, Proposal("Changed.epub"), BindingA);
            File.SetLastWriteTimeUtc(file,
                new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
            Assert.False(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, Proposal("Changed.epub"), BindingA));
        }

        [Fact]
        public void RemovedFileOrNewRootBindingInvalidatesEvidence()
        {
            var file = Path.Combine(root, "Removed.epub");
            File.WriteAllText(file, "synthetic");
            var initial = MediaLocalFileEvidencePreflight.Inspect(
                root, Proposal("Removed.epub"), BindingA);
            Assert.False(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, Proposal("Removed.epub"), BindingB));
            File.Delete(file);
            Assert.False(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, Proposal("Removed.epub"), BindingA));
        }

        [Fact]
        public void RejectsUnlistedAndNestedFilesAndZeroByteEvidence()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MediaLocalFileEvidencePreflight.Inspect(
                    root, Proposal("Missing.pdf"), BindingA));
            File.WriteAllText(Path.Combine(root, "Empty.pdf"), "");
            Assert.Throws<InvalidOperationException>(() =>
                MediaLocalFileEvidencePreflight.Inspect(
                    root, Proposal("Empty.pdf"), BindingA));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested", "Nested.pdf"), "data");
            Assert.Throws<InvalidOperationException>(() =>
                MediaLocalFileEvidencePreflight.Inspect(
                    root, Proposal("Nested.pdf"), BindingA));
        }

        [Fact]
        public void RejectsUntrustedProposalWithoutReadingFiles()
        {
            File.WriteAllText(Path.Combine(root, "Book.pdf"), "synthetic");
            var candidate = Proposal("Book.pdf");
            candidate.Disposition = "APPROVED_FOR_APPLY";
            Assert.Throws<InvalidOperationException>(() =>
                MediaLocalFileEvidencePreflight.Inspect(root, candidate, BindingA));
        }

        [Fact]
        public void MultipleVariantsStableButIdentityChangeInvalidates()
        {
            File.WriteAllText(Path.Combine(root, "Book.pdf"), "pdf synthetic");
            File.WriteAllText(Path.Combine(root, "Book.epub"), "epub synthetic");
            var initial = MediaLocalFileEvidencePreflight.Inspect(root,
                Proposal("Book.epub", "Book.pdf"), BindingA);
            Assert.Equal(2, initial.Entries.Length);
            Assert.True(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, Proposal("Book.pdf", "Book.epub"), BindingA));
            var renamedGame = Proposal("Book.pdf", "Book.epub");
            renamedGame.PlayniteId = "079f1cec-9147-4bf8-b456-d07d6dd8bcb7";
            Assert.False(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, renamedGame, BindingA));
        }

        [Fact]
        public void RejectsWholeVolumeAndRelativeRoots()
        {
            var proposal = Proposal("Book.pdf");
            Assert.Throws<ArgumentException>(() =>
                MediaLocalFileEvidencePreflight.Inspect("relative-folder",
                    proposal, BindingA));
            Assert.Throws<ArgumentException>(() =>
                MediaLocalFileEvidencePreflight.Inspect(
                    Path.GetPathRoot(root), proposal, BindingA));
        }

        [Fact]
        public void MalformedPriorSnapshotCannotCountAsFresh()
        {
            File.WriteAllText(Path.Combine(root, "Book.pdf"), "synthetic");
            var initial = MediaLocalFileEvidencePreflight.Inspect(root,
                Proposal("Book.pdf"), BindingA);
            initial.Entries = new MediaLocalEvidenceEntry[0];
            Assert.False(MediaLocalFileEvidencePreflight.MatchesFreshEvidence(
                initial, root, Proposal("Book.pdf"), BindingA));
        }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
