using System;

namespace MediaLibraryEnrichment
{
    public static class MediaCandidateClassifier
    {
        public static bool IsMediaCandidate(string sourceName)
        {
            return IsHumbleSource(sourceName);
        }

        public static bool ShouldObserve(string sourceName, bool hasCoverImage)
        {
            return IsMediaCandidate(sourceName) && !hasCoverImage;
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

        public static string NormalizeLocalEvidenceName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Replace('\\', '/');
            var separator = normalized.LastIndexOf('/');
            return separator >= 0
                ? normalized.Substring(separator + 1)
                : normalized;
        }

        public static string ResolveLocalEvidencePath(string manualPath, string notes)
        {
            if (!string.IsNullOrWhiteSpace(manualPath) &&
                !string.Equals(ClassifyPath(manualPath), "unresolved", StringComparison.Ordinal))
            {
                return manualPath;
            }

            if (string.IsNullOrWhiteSpace(notes))
            {
                return string.Empty;
            }

            var extensions = new[]
            {
                ".pdf", ".epub", ".mobi", ".azw", ".azw3", ".prc",
                ".cbz", ".cbr", ".cb7",
                ".flac", ".mp3", ".m4a", ".m4b", ".ogg", ".wav"
            };

            foreach (var extension in extensions)
            {
                var end = notes.IndexOf(extension, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    continue;
                }

                end += extension.Length;
                var prefix = notes.Substring(0, end);
                var semicolon = prefix.LastIndexOf("; ", StringComparison.Ordinal);
                var label = prefix.LastIndexOf(": ", StringComparison.Ordinal);
                var start = Math.Max(semicolon, label);
                start = start < 0 ? 0 : start + 2;
                return prefix.Substring(start).Trim();
            }

            return string.Empty;
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

            var name = NormalizeLocalEvidenceName(path);
            var extensionIndex = name.LastIndexOf('.');
            var extension = extensionIndex >= 0 ? name.Substring(extensionIndex) : string.Empty;
            return ClassifyExtension(extension);
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
        public string LocalEvidenceName { get; set; }
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
