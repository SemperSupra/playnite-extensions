using System;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaAssociationReviewPlannerTests
    {
        private static MediaObservation Game(string name, string kind = "unresolved",
            string id = "91428cca-3b52-4de4-8f81-3585eb572a46")
        {
            return new MediaObservation
            {
                Name = name,
                Kind = kind,
                PlayniteId = id,
                AdmissionProducerKind = "synthetic-admission-v1",
                AdmissionEvidenceKey = "synthetic-reviewed-admission"
            };
        }

        private static MediaFileInventoryResult Files(params MediaFileInventoryItem[] items)
        {
            return new MediaFileInventoryResult { SupportedFiles = items };
        }

        private static MediaFileInventoryItem File(string name, string kind)
        {
            return new MediaFileInventoryItem { FileName = name, Kind = kind };
        }

        [Fact]
        public void ExactStemCanOnlyProduceReviewRequiredProposal()
        {
            var game = Game(" Example Media ");
            var output = MediaAssociationReviewPlanner.Build(
                new[] { game }, Files(File("example media.epub", "book")));
            var item = Assert.Single(output.Suggestions);
            Assert.Equal(game.PlayniteId, item.PlayniteId);
            Assert.Equal("REVIEW_REQUIRED", item.Disposition);
            Assert.Equal("EXACT_TITLE_STEM_REVIEW_ONLY", item.Rule);
            Assert.Equal("book", item.MediaKindHint);
            Assert.Equal(new[] { "example media.epub" }, item.FileNames);
            Assert.Equal("unresolved", game.Kind);
            Assert.Equal(1, output.EligibleObservationCount);
        }

        [Fact]
        public void MultipleFormatsOfSameKindAreOneReviewBundle()
        {
            var output = MediaAssociationReviewPlanner.Build(
                new[] { Game("Manual Collection") },
                Files(File("Manual Collection.pdf", "book"),
                      File("Manual Collection.epub", "book")));
            var item = Assert.Single(output.Suggestions);
            Assert.Equal(2, item.FileNames.Length);
            Assert.Equal("Manual Collection.epub", item.FileNames[0]);
            Assert.Equal("Manual Collection.pdf", item.FileNames[1]);
        }

        [Fact]
        public void DuplicatedTitlesDoNotProduceProposals()
        {
            var output = MediaAssociationReviewPlanner.Build(
                new[] {
                    Game("Same Title"),
                    Game("same title", id: "5d2537d7-29e8-444c-a615-cd7fd57c1a81")
                }, Files(File("same title.pdf", "book")));
            Assert.Empty(output.Suggestions);
            Assert.Equal(1, output.AmbiguousMatchGroupCount);
        }

        [Fact]
        public void IneligibleDuplicateTitleStillBlocksReviewSuggestion()
        {
            var ineligible = Game("Collision",
                id: "43909f08-c0d6-426e-bd09-3db554b98f90");
            ineligible.AdmissionEvidenceKey = null;
            var output = MediaAssociationReviewPlanner.Build(
                new[] { Game("Collision"), ineligible },
                Files(File("Collision.pdf", "book")));
            Assert.Empty(output.Suggestions);
            Assert.Equal(1, output.AmbiguousMatchGroupCount);
        }

        [Fact]
        public void ColonOrControlCharacterInInventoryFileFailsClosed()
        {
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationReviewPlanner.Build(
                    new[] { Game("Book") }, Files(File("Book:copy.epub", "book"))));
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationReviewPlanner.Build(
                    new[] { Game("Book") }, Files(File("Book\u0001.pdf", "book"))));
        }

        [Fact]
        public void DuplicatedPlayniteIdentityIsNotEligible()
        {
            var output = MediaAssociationReviewPlanner.Build(
                new[] { Game("One"), Game("Two") },
                Files(File("One.pdf", "book"), File("Two.pdf", "book")));
            Assert.Empty(output.Suggestions);
            Assert.Equal(0, output.EligibleObservationCount);
        }

        [Fact]
        public void IneligibleDuplicatePlayniteIdentityStillBlocksReview()
        {
            var duplicate = Game("Another Title");
            duplicate.AdmissionEvidenceKey = null;
            var output = MediaAssociationReviewPlanner.Build(
                new[] { Game("Original Title"), duplicate },
                Files(File("Original Title.epub", "book")));
            Assert.Empty(output.Suggestions);
            Assert.Equal(0, output.EligibleObservationCount);
        }

        [Fact]
        public void MixedKindsAndExistingContradictionFailClosed()
        {
            var mixed = MediaAssociationReviewPlanner.Build(
                new[] { Game("Mixed") },
                Files(File("Mixed.pdf", "book"), File("Mixed.flac", "audio")));
            Assert.Empty(mixed.Suggestions);
            Assert.Equal(1, mixed.IncompatibleKindGroupCount);

            var contradictory = MediaAssociationReviewPlanner.Build(
                new[] { Game("Only Audio", kind: "audio") },
                Files(File("Only Audio.epub", "book")));
            Assert.Empty(contradictory.Suggestions);
            Assert.Equal(1, contradictory.IncompatibleKindGroupCount);
        }

        [Fact]
        public void DuplicateFilenamesAreAmbiguous()
        {
            var output = MediaAssociationReviewPlanner.Build(
                new[] { Game("Dup") },
                Files(File("Dup.pdf", "book"), File("dup.PDF", "book")));
            Assert.Empty(output.Suggestions);
            Assert.Equal(1, output.AmbiguousMatchGroupCount);
        }

        [Fact]
        public void NoTitleMatchAndNoAdmissionDoNotAuthorizeAssociation()
        {
            var unadmitted = Game("Match");
            unadmitted.AdmissionEvidenceKey = null;
            var output = MediaAssociationReviewPlanner.Build(
                new[] { unadmitted,
                    Game("Other", id: "e1d23d4d-354f-47a5-a64a-bc2385042001") },
                Files(File("Match.pdf", "book"), File("No Link.pdf", "book")));
            Assert.Empty(output.Suggestions);
            Assert.Equal(1, output.EligibleObservationCount);
        }

        [Theory]
        [InlineData("../escape.epub", "book")]
        [InlineData(@"C:\outside\escape.pdf", "book")]
        [InlineData("album.flac", "book")]
        [InlineData("album.pdf", "unsupported")]
        public void HostileOrMisclassifiedInventoryIsRejected(string fileName, string kind)
        {
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationReviewPlanner.Build(
                    new[] { Game("album") }, Files(File(fileName, kind))));
        }

        [Fact]
        public void CandidateCountLimitFailsClosed()
        {
            var rows = new MediaObservation[10001];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = Game("Synthetic " + i, id: Guid.NewGuid().ToString());
            }
            Assert.Throws<InvalidOperationException>(() =>
                MediaAssociationReviewPlanner.Build(rows, Files()));
        }

        [Fact]
        public void EmptyInputsProduceNoSuggestion()
        {
            var p = MediaAssociationReviewPlanner.Build(
                new MediaObservation[0], Files());
            Assert.Empty(p.Suggestions);
            Assert.Equal(0, p.InventoriedSupportedFileCount);
        }
    }
}
