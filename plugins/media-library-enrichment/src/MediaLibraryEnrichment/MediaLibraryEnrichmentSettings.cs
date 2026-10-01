using Playnite.SDK.Data;
using System;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class MediaLibraryEnrichmentSettings
    {
        public string Mode { get; set; } = "observe";

        public static MediaLibraryEnrichmentSettings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new MediaLibraryEnrichmentSettings();
            }

            var settings = Serialization.FromJson<MediaLibraryEnrichmentSettings>(
                File.ReadAllText(path));

            return settings ?? new MediaLibraryEnrichmentSettings();
        }

        public ProjectionMode ResolveMode()
        {
            if (string.Equals(Mode, "apply", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectionMode.Apply;
            }

            if (string.Equals(Mode, "rollback", StringComparison.OrdinalIgnoreCase))
            {
                return ProjectionMode.Rollback;
            }

            return ProjectionMode.Observe;
        }
    }
}
