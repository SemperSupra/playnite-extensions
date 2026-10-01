using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaCandidateClassifierTests
    {
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
    }
}
