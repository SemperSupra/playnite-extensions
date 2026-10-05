using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaCandidateClassifierTests
    {
        [Theory]
        [InlineData("book.pdf", null, "book")]
        [InlineData("book.epub", null, "book")]
        [InlineData("comic.cbz", null, "comic")]
        [InlineData(null, "fixture: soundtrack.flac", "audio")]
        [InlineData(null, "nothing useful", "unresolved")]
        public void ClassifiesConservativelyFromLocalEvidence(
            string manualPath,
            string notes,
            string expected)
        {
            Assert.Equal(expected, MediaCandidateClassifier.ClassifyKind(manualPath, notes));
        }

        [Theory]
        [InlineData(@"C:\runner-a\fixture\rdte-book.pdf", "rdte-book.pdf")]
        [InlineData(@"D:\runner-b\other\rdte-book.pdf", "rdte-book.pdf")]
        [InlineData("/tmp/runner-c/fixture/rdte-comic.cbz", "rdte-comic.cbz")]
        [InlineData(null, "")]
        public void NormalizesEphemeralRootsToStableEvidenceName(
            string path,
            string expected)
        {
            Assert.Equal(expected, MediaCandidateClassifier.NormalizeLocalEvidenceName(path));
        }
        [Fact]
        public void CollectsAllSupportedLocalVariantsWithoutRunnerRoots()
        {
            var variants = MediaCandidateClassifier.CollectLocalEvidenceNames(
                @"C:\\runner-a\\media\\rdte-book.pdf",
                @"RDTE media fixture; variants: C:\\runner-a\\media\\rdte-book.pdf; D:\\runner-b\\media\\rdte-book.epub");

            Assert.Equal(
                new[] { "rdte-book.epub", "rdte-book.pdf" },
                variants);
        }

        [Fact]
        public void LocalVariantInventoryDeduplicatesRepeatedEvidence()
        {
            var variants = MediaCandidateClassifier.CollectLocalEvidenceNames(
                @"C:\\media\\rdte-book.pdf",
                @"primary: D:\\other\\rdte-book.pdf; audio: D:\\other\\rdte-soundtrack.flac");

            Assert.Equal(
                new[] { "rdte-book.pdf", "rdte-soundtrack.flac" },
                variants);
        }

        [Fact]
        public void LocalVariantInventoryIgnoresUnsupportedEvidence()
        {
            Assert.Empty(
                MediaCandidateClassifier.CollectLocalEvidenceNames(
                    @"C:\\media\\readme.txt",
                    "fixture: C:\\media\\cover.png"));
        }

        [Fact]
        public void LocalVariantInventoryDoesNotSplitLongerExtensions()
        {
            Assert.Equal(
                new[] { "book.azw3" },
                MediaCandidateClassifier.CollectLocalEvidenceNames(
                    null,
                    @"variant: C:\\media\\book.azw3"));
        }

        [Theory]
        [InlineData(@"C:\\media\\book.pdf", null, @"C:\\media\\book.pdf")]
        [InlineData(null, @"RDTE audio fixture: C:\\media\\track.flac", @"C:\\media\\track.flac")]
        [InlineData(null, "nothing useful", "")]
        public void ResolvesStableLocalEvidencePath(
            string manualPath,
            string notes,
            string expected)
        {
            Assert.Equal(
                expected,
                MediaCandidateClassifier.ResolveLocalEvidencePath(manualPath, notes));
        }

    }
}
