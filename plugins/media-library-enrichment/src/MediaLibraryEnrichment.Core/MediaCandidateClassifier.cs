using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public static class MediaCandidateClassifier
    {
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

        public static string[] CollectLocalEvidenceNames(
            string manualPath,
            string notes)
        {
            var names = new List<string>();

            if (!string.IsNullOrWhiteSpace(manualPath) &&
                !string.Equals(
                    ClassifyPath(manualPath),
                    "unresolved",
                    StringComparison.Ordinal))
            {
                names.Add(NormalizeLocalEvidenceName(manualPath));
            }

            if (!string.IsNullOrWhiteSpace(notes))
            {
                var extensions = new[]
                {
                    ".pdf", ".epub", ".mobi", ".azw", ".azw3", ".prc",
                    ".cbz", ".cbr", ".cb7",
                    ".flac", ".mp3", ".m4a", ".m4b", ".ogg", ".wav"
                };

                foreach (var extension in extensions)
                {
                    var searchStart = 0;
                    while (searchStart < notes.Length)
                    {
                        var index = FindTerminalExtension(
                            notes,
                            extension,
                            searchStart);
                        if (index < 0)
                        {
                            break;
                        }

                        var end = index + extension.Length;

                        var prefix = notes.Substring(0, end);
                        var semicolon = prefix.LastIndexOf(';');
                        var newline = Math.Max(
                            prefix.LastIndexOf('\n'),
                            prefix.LastIndexOf('\r'));
                        var label = prefix.LastIndexOf(
                            ": ",
                            StringComparison.Ordinal);
                        var delimiter = Math.Max(
                            Math.Max(semicolon, newline),
                            label);
                        var start = delimiter < 0
                            ? 0
                            : delimiter + (delimiter == label ? 2 : 1);
                        var candidate = prefix.Substring(
                            start,
                            end - start).Trim();
                        var name = NormalizeLocalEvidenceName(candidate);

                        if (!string.IsNullOrWhiteSpace(name) &&
                            !string.Equals(
                                ClassifyPath(name),
                                "unresolved",
                                StringComparison.Ordinal))
                        {
                            names.Add(name);
                        }

                        searchStart = end;
                    }
                }
            }

            return names
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ThenBy(value => value, StringComparer.Ordinal)
                .ToArray();
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
                var index = FindTerminalExtension(notes, extension, 0);
                if (index < 0)
                {
                    continue;
                }

                var end = index + extension.Length;
                var prefix = notes.Substring(0, end);
                var semicolon = prefix.LastIndexOf("; ", StringComparison.Ordinal);
                var label = prefix.LastIndexOf(": ", StringComparison.Ordinal);
                var start = Math.Max(semicolon, label);
                start = start < 0 ? 0 : start + 2;
                return prefix.Substring(start).Trim();
            }

            return string.Empty;
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
                if (FindTerminalExtension(text, extension, 0) >= 0)
                {
                    return ClassifyExtension(extension);
                }
            }

            return "unresolved";
        }

        private static int FindTerminalExtension(
            string text,
            string extension,
            int searchStart)
        {
            while (!string.IsNullOrEmpty(text) && searchStart < text.Length)
            {
                var index = text.IndexOf(
                    extension,
                    searchStart,
                    StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return -1;
                }

                var end = index + extension.Length;
                if (end >= text.Length ||
                    (!char.IsLetterOrDigit(text[end]) &&
                     text[end] != '.' &&
                     text[end] != '_' &&
                     text[end] != '-'))
                {
                    return index;
                }

                searchStart = end;
            }

            return -1;
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
        public string[] LocalEvidenceNames { get; set; }
        public string AdmissionEvidenceKey { get; set; }
        public string AdmissionProducerKind { get; set; }
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
