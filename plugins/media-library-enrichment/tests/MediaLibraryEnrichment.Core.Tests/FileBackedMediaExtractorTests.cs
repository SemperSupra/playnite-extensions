using System;
using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class FileBackedMediaExtractorTests
    {
        [Fact]
        public void TitleAndSourceNameAloneYieldSkipNoEvidence()
        {
            var input = new FileBackedMediaExtractorInput
            {
                Name = "Humble eBook Bundle Item",
                Source = "Humble Bundle RDTE"
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.False(result.HasPositiveEvidence);
            Assert.Equal("unresolved", result.Kind);
            Assert.Equal(string.Empty, result.PrimaryLocalEvidencePath);
            Assert.Empty(result.LocalEvidenceNames);
            Assert.Equal("SKIP_NO_EVIDENCE", result.EvidenceOutcome);
        }

        [Fact]
        public void NullInputYieldsSkipNoEvidence()
        {
            var result = FileBackedMediaExtractor.Extract(null);

            Assert.False(result.HasPositiveEvidence);
            Assert.Equal("unresolved", result.Kind);
            Assert.Equal("SKIP_NO_EVIDENCE", result.EvidenceOutcome);
        }

        [Theory]
        [InlineData("book.pdf", "book")]
        [InlineData("book.epub", "book")]
        [InlineData("book.mobi", "book")]
        [InlineData("book.azw3", "book")]
        [InlineData("comic.cbz", "comic")]
        [InlineData("comic.cbr", "comic")]
        [InlineData("soundtrack.flac", "audio")]
        [InlineData("soundtrack.mp3", "audio")]
        [InlineData("audiobook.m4b", "audio")]
        public void PositiveExtractionFromManualPath(string file, string expectedKind)
        {
            var path = @"C:\library\media\" + file;
            var input = new FileBackedMediaExtractorInput
            {
                Name = "Media Product",
                Source = "Humble",
                ManualPath = path
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.True(result.HasPositiveEvidence);
            Assert.Equal(expectedKind, result.Kind);
            Assert.Equal(path, result.PrimaryLocalEvidencePath);
            Assert.Single(result.LocalEvidenceNames);
            Assert.Equal(file, result.LocalEvidenceNames[0]);
            Assert.Equal("POSITIVE_FILE_EVIDENCE", result.EvidenceOutcome);
        }

        [Fact]
        public void PositiveExtractionFromRoms()
        {
            var input = new FileBackedMediaExtractorInput
            {
                Name = "Comic Collection",
                Roms = new[] { @"D:\comics\issue1.cbz" }
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.True(result.HasPositiveEvidence);
            Assert.Equal("comic", result.Kind);
            Assert.Equal(@"D:\comics\issue1.cbz", result.PrimaryLocalEvidencePath);
            Assert.Contains("issue1.cbz", result.LocalEvidenceNames);
            Assert.Equal("POSITIVE_FILE_EVIDENCE", result.EvidenceOutcome);
        }

        [Fact]
        public void PositiveExtractionFromGameActions()
        {
            var input = new FileBackedMediaExtractorInput
            {
                Name = "Audiobook Product",
                GameActions = new[] { @"E:\audio\book.m4b" }
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.True(result.HasPositiveEvidence);
            Assert.Equal("audio", result.Kind);
            Assert.Equal(@"E:\audio\book.m4b", result.PrimaryLocalEvidencePath);
            Assert.Contains("book.m4b", result.LocalEvidenceNames);
            Assert.Equal("POSITIVE_FILE_EVIDENCE", result.EvidenceOutcome);
        }

        [Fact]
        public void PositiveExtractionFromNotes()
        {
            var input = new FileBackedMediaExtractorInput
            {
                Name = "EBook Product",
                Notes = @"Downloaded ebook: C:\books\guide.pdf"
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.True(result.HasPositiveEvidence);
            Assert.Equal("book", result.Kind);
            Assert.Equal(@"C:\books\guide.pdf", result.PrimaryLocalEvidencePath);
            Assert.Contains("guide.pdf", result.LocalEvidenceNames);
            Assert.Equal("POSITIVE_FILE_EVIDENCE", result.EvidenceOutcome);
        }

        [Fact]
        public void NonMediaFileExtensionsYieldUnresolvedFormat()
        {
            var input = new FileBackedMediaExtractorInput
            {
                Name = "Game Executable",
                Source = "Humble",
                ManualPath = @"C:\games\game.exe",
                Roms = new[] { @"C:\games\game.iso" }
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.False(result.HasPositiveEvidence);
            Assert.Equal("unresolved", result.Kind);
            Assert.Equal(string.Empty, result.PrimaryLocalEvidencePath);
            Assert.Empty(result.LocalEvidenceNames);
            Assert.Equal("UNRESOLVED_FORMAT", result.EvidenceOutcome);
        }

        [Fact]
        public void MultipleEvidenceSourcesDeduplicateEvidenceNames()
        {
            var input = new FileBackedMediaExtractorInput
            {
                Name = "Multi Format Item",
                ManualPath = @"C:\library\item.pdf",
                Notes = @"Reference: C:\library\item.pdf; Audio: D:\library\track.flac"
            };

            var result = FileBackedMediaExtractor.Extract(input);

            Assert.True(result.HasPositiveEvidence);
            Assert.Equal(new[] { "item.pdf", "track.flac" }, result.LocalEvidenceNames);
            Assert.Equal("POSITIVE_FILE_EVIDENCE", result.EvidenceOutcome);
        }
    }
}
