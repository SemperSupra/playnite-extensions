using System;

namespace MediaLibraryEnrichment
{
    public enum CoverPlanDecision
    {
        Add,
        Noop,
        UserOverride,
        ConflictEvidence,
        ConflictCoverPresent
    }

    public static class CoverEnrichmentPolicy
    {
        public static CoverPlanDecision Decide(
            bool evidenceValid,
            bool hasCurrentCover,
            bool currentMatchesOwnedCover,
            string ledgerStatus)
        {
            if (!evidenceValid)
            {
                return CoverPlanDecision.ConflictEvidence;
            }

            if (hasCurrentCover)
            {
                return currentMatchesOwnedCover
                    ? CoverPlanDecision.Noop
                    : CoverPlanDecision.ConflictCoverPresent;
            }

            if (string.Equals(
                    ledgerStatus,
                    "COMMITTED",
                    StringComparison.Ordinal) ||
                string.Equals(
                    ledgerStatus,
                    "USER_OVERRIDDEN",
                    StringComparison.Ordinal))
            {
                return CoverPlanDecision.UserOverride;
            }

            return CoverPlanDecision.Add;
        }
    }
}
