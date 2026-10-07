using Playnite.SDK.Data;
using System;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class MediaLibraryEnrichmentSettings
    {
        public string Mode { get; set; } = "observe";

        public static MediaLibraryEnrichmentSettings LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                var defaults = new MediaLibraryEnrichmentSettings();
                defaults.Save(path);
                return defaults;
            }

            MediaLibraryEnrichmentSettings loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                loaded.Normalize();
                return loaded;
            }

            throw new InvalidDataException(
                "Media Library Enrichment settings are not valid JSON.",
                error);
        }

        public void Save(string path)
        {
            Normalize();
            File.WriteAllText(path, Serialization.ToJson(this, true));
        }

        private void Normalize()
        {
            var normalized = (Mode ?? string.Empty).Trim().ToLowerInvariant();
            if (normalized != "observe" &&
                normalized != "apply" &&
                normalized != "rollback")
            {
                normalized = "observe";
            }

            Mode = normalized;
        }
    }
}
