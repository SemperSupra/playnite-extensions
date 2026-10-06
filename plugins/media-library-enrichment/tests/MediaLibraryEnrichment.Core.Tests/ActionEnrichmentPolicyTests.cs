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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("PLANNED")]
        [InlineData("ROLLED_BACK")]
        public void MissingActionWithoutCommittedOwnershipIsAddable(string status)
        {
            Assert.Equal(
                ActionPresenceDecision.AddAction,
                ActionEnrichmentPolicy.DecidePresence(false, false, status));
        }

        [Theory]
        [InlineData("COMMITTED")]
        [InlineData("USER_OVERRIDDEN")]
        public void MissingPreviouslyCommittedActionIsAUserOverride(string status)
        {
            Assert.Equal(
                ActionPresenceDecision.UserOverride,
                ActionEnrichmentPolicy.DecidePresence(false, false, status));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("COMMITTED")]
        [InlineData("USER_OVERRIDDEN")]
        public void ExistingDesiredActionIsAlwaysANoop(string status)
        {
            Assert.Equal(
                ActionPresenceDecision.Noop,
                ActionEnrichmentPolicy.DecidePresence(true, false, status));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("COMMITTED")]
        [InlineData("USER_OVERRIDDEN")]
        public void SameNameChangedActionRemainsAConflict(string status)
        {
            Assert.Equal(
                ActionPresenceDecision.Conflict,
                ActionEnrichmentPolicy.DecidePresence(false, true, status));
        }
    }
}
