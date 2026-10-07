using System;

namespace MediaLibraryEnrichment
{
    public sealed class FilterPresetEnrichmentSpec
    {
        public string Kind { get; set; }
        public Guid PresetId { get; set; }
        public string PresetName { get; set; }
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; }
    }

    public enum FilterPresetPresenceDecision
    {
        Noop,
        Conflict,
        AddPreset,
        UserOverride
    }

    public static class FilterPresetEnrichmentPolicy
    {
        private static readonly FilterPresetEnrichmentSpec Books =
            new FilterPresetEnrichmentSpec
            {
                Kind = "book",
                PresetId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1501"),
                PresetName = "SemperSupra Media: Books",
                CategoryId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401"),
                CategoryName = "SemperSupra.Media:Book"
            };

        private static readonly FilterPresetEnrichmentSpec Comics =
            new FilterPresetEnrichmentSpec
            {
                Kind = "comic",
                PresetId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1502"),
                PresetName = "SemperSupra Media: Comics",
                CategoryId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1402"),
                CategoryName = "SemperSupra.Media:Comic"
            };

        private static readonly FilterPresetEnrichmentSpec Audio =
            new FilterPresetEnrichmentSpec
            {
                Kind = "audio",
                PresetId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1503"),
                PresetName = "SemperSupra Media: Audio",
                CategoryId = Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1403"),
                CategoryName = "SemperSupra.Media:Audio"
            };

        public static FilterPresetPresenceDecision DecidePresence(
            bool desiredPresent,
            bool identityConflict,
            string ledgerStatus)
        {
            if (desiredPresent)
            {
                return FilterPresetPresenceDecision.Noop;
            }

            if (identityConflict)
            {
                return FilterPresetPresenceDecision.Conflict;
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
                return FilterPresetPresenceDecision.UserOverride;
            }

            return FilterPresetPresenceDecision.AddPreset;
        }

        public static FilterPresetEnrichmentSpec ForKind(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "book":
                    return Books;
                case "comic":
                    return Comics;
                case "audio":
                    return Audio;
                default:
                    return null;
            }
        }

        public static FilterPresetEnrichmentSpec[] All()
        {
            return new[] { Books, Comics, Audio };
        }
    }
}
