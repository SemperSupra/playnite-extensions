using Xunit;

namespace MediaLibraryEnrichment.Core.Tests
{
    public sealed class CoverEnrichmentPolicyTests
    {
        [Fact]
        public void AddsOnlyWhenEvidenceIsValidAndCoverWasNotPreviouslyCommitted()
        {
            Assert.Equal(
                CoverPlanDecision.Add,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: null));

            Assert.Equal(
                CoverPlanDecision.Add,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "ROLLED_BACK"));
        }

        [Fact]
        public void MissingPreviouslyCommittedCoverIsUserOverride()
        {
            Assert.Equal(
                CoverPlanDecision.UserOverride,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "COMMITTED"));

            Assert.Equal(
                CoverPlanDecision.UserOverride,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "USER_OVERRIDDEN"));
        }

        [Fact]
        public void ExistingOwnedCoverIsNoop()
        {
            Assert.Equal(
                CoverPlanDecision.Noop,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: true,
                    currentMatchesOwnedCover: true,
                    ledgerStatus: "COMMITTED"));
        }

        [Fact]
        public void ExistingUnownedCoverIsPreservedAsConflict()
        {
            Assert.Equal(
                CoverPlanDecision.ConflictCoverPresent,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: true,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "COMMITTED"));
        }

        [Fact]
        public void InvalidEvidenceNeverPlansMutation()
        {
            Assert.Equal(
                CoverPlanDecision.ConflictEvidence,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: false,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "COMMITTED"));
        }

        [Fact]
        public void PlannedAndApplyingStateRemainRetryable()
        {
            Assert.Equal(
                CoverPlanDecision.Add,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "PLANNED"));

            Assert.Equal(
                CoverPlanDecision.Add,
                CoverEnrichmentPolicy.Decide(
                    evidenceValid: true,
                    hasCurrentCover: false,
                    currentMatchesOwnedCover: false,
                    ledgerStatus: "APPLYING"));
        }
    }
}
