using System;

namespace MediaLibraryEnrichment
{
    public sealed class MediaAdmissionEvidence
    {
        public string PlayniteId { get; set; }
        public string ProviderGameId { get; set; }
        public string EvidenceKey { get; set; }
        public string ProducerKind { get; set; }
    }

    public static class MediaAdmissionPolicy
    {
        public static bool IsStructurallyValid(MediaAdmissionEvidence evidence)
        {
            Guid playniteId;
            return evidence != null &&
                Guid.TryParse(evidence.PlayniteId, out playniteId) &&
                !string.IsNullOrWhiteSpace(evidence.EvidenceKey) &&
                !string.IsNullOrWhiteSpace(evidence.ProducerKind);
        }

        public static bool MatchesCurrentIdentity(
            MediaAdmissionEvidence evidence,
            string currentPlayniteId,
            string currentProviderGameId)
        {
            if (!IsStructurallyValid(evidence))
            {
                return false;
            }

            Guid expectedId;
            Guid observedId;
            if (!Guid.TryParse(evidence.PlayniteId, out expectedId) ||
                !Guid.TryParse(currentPlayniteId, out observedId) ||
                expectedId != observedId)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(evidence.ProviderGameId) ||
                string.Equals(
                    evidence.ProviderGameId,
                    currentProviderGameId ?? string.Empty,
                    StringComparison.Ordinal);
        }
    }

    public static class HumbleMediaAdmissionAdapter
    {
        public static MediaAdmissionEvidence TryCreate(
            string playniteId,
            string providerGameId,
            string sourceName)
        {
            return TryCreate(playniteId, providerGameId, sourceName, null, null);
        }

        public static MediaAdmissionEvidence TryCreate(
            string playniteId,
            string providerGameId,
            string sourceName,
            string manualPath,
            string notes)
        {
            Guid parsed;
            if (!Guid.TryParse(playniteId, out parsed) ||
                string.IsNullOrWhiteSpace(sourceName) ||
                sourceName.IndexOf("Humble", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var stableIdentity = string.IsNullOrWhiteSpace(providerGameId)
                ? parsed.ToString()
                : providerGameId;

            return new MediaAdmissionEvidence
            {
                PlayniteId = parsed.ToString(),
                ProviderGameId = providerGameId ?? string.Empty,
                EvidenceKey = "humble-source:" + stableIdentity,
                ProducerKind = "humble-source-v1"
            };
        }
    }
}
