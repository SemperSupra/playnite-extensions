using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class CoverEnrichmentPolicyTests
    {
        [Fact]
        public void AddsOnlyWhenEvidenceIsValidAndCoverIsMissing()
        {
            Assert.Equal(
                CoverPlanDecision.Add,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false));
        }

        [Fact]
        public void ExistingOwnedCoverIsNoop()
        {
            Assert.Equal(
                CoverPlanDecision.Noop,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: true,
                    currentMatchesOwnedCover: true));
        }

        [Fact]
        public void ExistingUnownedCoverIsPreservedAsConflict()
        {
            Assert.Equal(
                CoverPlanDecision.ConflictCoverPresent,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: true,
                    currentMatchesOwnedCover: false));
        }

        [Fact]
        public void InvalidEvidenceNeverPlansMutation()
        {
            Assert.Equal(
                CoverPlanDecision.ConflictEvidence,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: false,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false));
        }
    }
}
