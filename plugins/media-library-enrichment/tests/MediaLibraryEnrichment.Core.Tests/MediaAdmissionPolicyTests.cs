using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class MediaAdmissionPolicyTests
    {
        [Theory]
        [InlineData("Humble Bundle RDTE", true)]
        [InlineData("Humble Extras", true)]
        [InlineData("Manual RDTE", false)]
        [InlineData(null, false)]
        public void HumbleAdapterOwnsProviderSpecificRecognition(
            string source,
            bool expected)
        {
            var evidence = HumbleMediaAdmissionAdapter.TryCreate(
                "73000000-0000-4000-8000-000000000001",
                "provider-item-1",
                source);

            Assert.Equal(expected, evidence != null);
            if (evidence != null)
            {
                Assert.Equal("humble-source-v1", evidence.ProducerKind);
                Assert.Equal(
                    "humble-source:provider-item-1",
                    evidence.EvidenceKey);
            }
        }

        [Fact]
        public void HumbleAdapterOverloadAcceptsOptionalFileEvidence()
        {
            var evidence = HumbleMediaAdmissionAdapter.TryCreate(
                "73000000-0000-4000-8000-000000000001",
                "provider-item-1",
                "Humble Bundle RDTE",
                @"C:\library\book.pdf",
                "Notes with C:\\library\\book.pdf");

            Assert.NotNull(evidence);
            Assert.Equal("humble-source-v1", evidence.ProducerKind);
            Assert.Equal("humble-source:provider-item-1", evidence.EvidenceKey);
        }

        [Fact]
        public void ExplicitEvidenceCanAdmitNonHumbleIdentity()
        {
            var evidence = new MediaAdmissionEvidence
            {
                PlayniteId = "73000000-0000-4000-8000-000000000005",
                ProviderGameId = "rdte-manual-media-book",
                EvidenceKey = "rdte-manual-media-book-v1",
                ProducerKind = "rdte-manual-evidence-v1"
            };

            Assert.True(MediaAdmissionPolicy.IsStructurallyValid(evidence));
            Assert.True(
                MediaAdmissionPolicy.MatchesCurrentIdentity(
                    evidence,
                    "73000000-0000-4000-8000-000000000005",
                    "rdte-manual-media-book"));
        }

        [Fact]
        public void ProviderIdentityMismatchFailsClosed()
        {
            var evidence = new MediaAdmissionEvidence
            {
                PlayniteId = "73000000-0000-4000-8000-000000000005",
                ProviderGameId = "rdte-manual-media-book",
                EvidenceKey = "rdte-manual-media-book-v1",
                ProducerKind = "rdte-manual-evidence-v1"
            };

            Assert.False(
                MediaAdmissionPolicy.MatchesCurrentIdentity(
                    evidence,
                    evidence.PlayniteId,
                    "different-provider-item"));
        }

        [Theory]
        [InlineData(null, "evidence", "producer")]
        [InlineData("not-a-guid", "evidence", "producer")]
        [InlineData("73000000-0000-4000-8000-000000000005", null, "producer")]
        [InlineData("73000000-0000-4000-8000-000000000005", "evidence", null)]
        public void MalformedEvidenceFailsClosed(
            string playniteId,
            string evidenceKey,
            string producerKind)
        {
            Assert.False(
                MediaAdmissionPolicy.IsStructurallyValid(
                    new MediaAdmissionEvidence
                    {
                        PlayniteId = playniteId,
                        EvidenceKey = evidenceKey,
                        ProducerKind = producerKind
                    }));
        }
    }
}
