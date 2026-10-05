using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaCandidateClassifierTests
    {
        [Theory]
        [InlineData("Humble Bundle RDTE", true)]
        [InlineData("Humble Extras", true)]
        [InlineData("Manual RDTE", false)]
        [InlineData(null, false)]
        public void StableMediaAdmissionDependsOnSourceNotCoverState(
            string source,
            bool expected)
        {
            Assert.Equal(expected, MediaCandidateClassifier.IsMediaCandidate(source));
        }

        [Theory]
        [InlineData("Humble Bundle RDTE", false, true)]
        [InlineData("Humble Extras", false, true)]
        [InlineData("Humble Bundle RDTE", true, false)]
        [InlineData("Manual RDTE", false, false)]
        [InlineData(null, false, false)]
        public void ObservesOnlyHumbleItemsWithMissingCover(
            string source,
            bool hasCover,
            bool expected)
        {
            Assert.Equal(expected, MediaCandidateClassifier.ShouldObserve(source, hasCover));
        }

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
