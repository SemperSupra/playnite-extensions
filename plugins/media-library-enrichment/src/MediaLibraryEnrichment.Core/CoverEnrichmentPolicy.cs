namespace MediaLibraryEnrichment
{
    public enum CoverPlanDecision
    {
        Add,
        Noop,
        ConflictEvidence,
        ConflictCoverPresent
    }

    public static class CoverEnrichmentPolicy
    {
        public static CoverPlanDecision Decide(
            bool evidenceValid,
            bool hasCurrentCover,
            bool currentMatchesOwnedCover)
        {
            if (!evidenceValid)
            {
                return CoverPlanDecision.ConflictEvidence;
            }

            if (!hasCurrentCover)
            {
                return CoverPlanDecision.Add;
            }

            return currentMatchesOwnedCover
                ? CoverPlanDecision.Noop
                : CoverPlanDecision.ConflictCoverPresent;
        }
    }
}
