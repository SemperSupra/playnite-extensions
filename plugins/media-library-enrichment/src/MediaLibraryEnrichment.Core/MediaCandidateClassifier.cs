using System;
using System.IO;

namespace MediaLibraryEnrichment
{
    public static class MediaCandidateClassifier
    {
        public static bool ShouldObserve(string sourceName, bool hasCoverImage)
        {
            return IsHumbleSource(sourceName) && !hasCoverImage;
        }

        public static string ClassifyKind(string manualPath, string notes)
        {
            var fromPath = ClassifyPath(manualPath);
            if (!string.Equals(fromPath, "unresolved", StringComparison.Ordinal))
            {
                return fromPath;
            }

            return ClassifyText(notes);
        }

        private static bool IsHumbleSource(string sourceName)
        {
            return !string.IsNullOrWhiteSpace(sourceName) &&
                sourceName.IndexOf("Humble", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ClassifyPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "unresolved";
            }

            return ClassifyExtension(Path.GetExtension(path));
        }

        private static string ClassifyText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "unresolved";
            }

            var extensions = new[]
            {
                ".pdf", ".epub", ".mobi", ".azw", ".azw3", ".prc",
                ".cbz", ".cbr", ".cb7",
                ".flac", ".mp3", ".m4a", ".m4b", ".ogg", ".wav"
            };

            foreach (var extension in extensions)
            {
                if (text.IndexOf(extension, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ClassifyExtension(extension);
                }
            }

            return "unresolved";
        }

        private static string ClassifyExtension(string extension)
        {
            switch ((extension ?? string.Empty).ToLowerInvariant())
            {
                case ".pdf":
                case ".epub":
                case ".mobi":
                case ".azw":
                case ".azw3":
                case ".prc":
                    return "book";
                case ".cbz":
                case ".cbr":
                case ".cb7":
                    return "comic";
                case ".flac":
                case ".mp3":
                case ".m4a":
                case ".m4b":
                case ".ogg":
                case ".wav":
                    return "audio";
                default:
                    return "unresolved";
            }
        }
    }

    public sealed class MediaObservation
    {
        public string PlayniteId { get; set; }
        public string ProviderGameId { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string Kind { get; set; }
        public string ManualPath { get; set; }
        public bool CoverMissing { get; set; }
    }

    public sealed class MediaObservationReceipt
    {
        public string Schema { get; set; }
        public string FixtureContract { get; set; }
        public int CandidateCount { get; set; }
        public MediaObservation[] Candidates { get; set; }
    }
}
