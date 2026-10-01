using PlayniteAutoReport.Reporting;
using Xunit;

namespace PlayniteAutoReport.Tests
{
    public sealed class ActivityClassifierTests
    {
        [Theory]
        [InlineData(0ul, "Never played")]
        [InlineData(1ul, "Briefly tried")]
        [InlineData(7199ul, "Briefly tried")]
        [InlineData(7200ul, "Sampled")]
        [InlineData(35999ul, "Sampled")]
        [InlineData(36000ul, "Played")]
        [InlineData(143999ul, "Played")]
        [InlineData(144000ul, "Heavily played")]
        public void ClassifyUsesDocumentedBoundaries(
            ulong playtimeSeconds,
            string expected)
        {
            Assert.Equal(expected, ActivityClassifier.Classify(playtimeSeconds));
        }
    }
}
