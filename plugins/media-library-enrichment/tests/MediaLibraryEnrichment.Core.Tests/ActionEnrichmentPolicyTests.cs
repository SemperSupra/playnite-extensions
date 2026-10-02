using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class ActionEnrichmentPolicyTests
    {
        [Theory]
        [InlineData("book", "media-open-book", "Read")]
        [InlineData("comic", "media-open-comic", "Read")]
        [InlineData("audio", "media-open-audio", "Listen")]
        public void MapsKnownKindsToStableCustomActions(
            string kind,
            string semanticKey,
            string actionName)
        {
            var spec = ActionEnrichmentPolicy.ForKind(kind);

            Assert.NotNull(spec);
            Assert.Equal(semanticKey, spec.SemanticKey);
            Assert.Equal(actionName, spec.ActionName);
        }

        [Fact]
        public void LeavesUnknownKindsUnmanaged()
        {
            Assert.Null(ActionEnrichmentPolicy.ForKind("video"));
        }
    }
}
