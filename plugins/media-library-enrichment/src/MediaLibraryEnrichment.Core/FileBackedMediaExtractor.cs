using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public sealed class FileBackedMediaExtractorInput
    {
        public string Name { get; set; }
        public string Source { get; set; }
        public string ManualPath { get; set; }
        public string Notes { get; set; }
        public IEnumerable<string> Roms { get; set; }
        public IEnumerable<string> GameActions { get; set; }
        public string InstallDir { get; set; }
    }

    public sealed class FileBackedMediaResult
    {
        public bool HasPositiveEvidence { get; set; }
        public string Kind { get; set; } = "unresolved";
        public string PrimaryLocalEvidencePath { get; set; } = string.Empty;
        public string[] LocalEvidenceNames { get; set; } = Array.Empty<string>();
        public string EvidenceOutcome { get; set; } = "SKIP_NO_EVIDENCE";
    }

    public static class FileBackedMediaExtractor
    {
        private static readonly string[] BookExtensions = new[]
        {
            ".pdf", ".epub", ".mobi", ".azw", ".azw3", ".prc"
        };

        private static readonly string[] ComicExtensions = new[]
        {
            ".cbz", ".cbr", ".cb7"
        };

        private static readonly string[] AudioExtensions = new[]
        {
            ".flac", ".mp3", ".m4a", ".m4b", ".ogg", ".wav"
        };

        public static FileBackedMediaResult Extract(FileBackedMediaExtractorInput input)
        {
            if (input == null)
            {
                return new FileBackedMediaResult
                {
                    HasPositiveEvidence = false,
                    Kind = "unresolved",
                    EvidenceOutcome = "SKIP_NO_EVIDENCE"
                };
            }

            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(input.ManualPath))
            {
                candidates.Add(input.ManualPath.Trim());
            }

            if (input.Roms != null)
            {
                foreach (var rom in input.Roms)
                {
                    if (!string.IsNullOrWhiteSpace(rom))
                    {
                        candidates.Add(rom.Trim());
                    }
                }
            }

            if (input.GameActions != null)
            {
                foreach (var action in input.GameActions)
                {
                    if (!string.IsNullOrWhiteSpace(action))
                    {
                        candidates.Add(action.Trim());
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(input.Notes))
            {
                var resolvedNotePath = MediaCandidateClassifier.ResolveLocalEvidencePath(null, input.Notes);
                if (!string.IsNullOrWhiteSpace(resolvedNotePath))
                {
                    candidates.Add(resolvedNotePath);
                }

                var extractedFromNotes = MediaCandidateClassifier.CollectLocalEvidenceNames(null, input.Notes);
                if (extractedFromNotes != null)
                {
                    foreach (var name in extractedFromNotes)
                    {
                        candidates.Add(name);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(input.InstallDir))
            {
                var installDir = input.InstallDir.Trim();
                var kindFromDir = ClassifyExtension(GetExtension(installDir));
                if (!string.Equals(kindFromDir, "unresolved", StringComparison.Ordinal))
                {
                    candidates.Add(installDir);
                }
            }

            var positivePaths = new List<string>();
            var positiveNames = new List<string>();
            string primaryKind = "unresolved";
            string primaryPath = string.Empty;

            bool hadNonMediaPaths = false;

            foreach (var candidate in candidates)
            {
                var ext = GetExtension(candidate);
                var kind = ClassifyExtension(ext);

                if (!string.Equals(kind, "unresolved", StringComparison.Ordinal))
                {
                    var normalizedName = MediaCandidateClassifier.NormalizeLocalEvidenceName(candidate);
                    if (!string.IsNullOrWhiteSpace(normalizedName) && !positiveNames.Contains(normalizedName, StringComparer.OrdinalIgnoreCase))
                    {
                        positiveNames.Add(normalizedName);
                    }

                    if (string.Equals(primaryKind, "unresolved", StringComparison.Ordinal))
                    {
                        primaryKind = kind;
                        primaryPath = candidate;
                    }
                    positivePaths.Add(candidate);
                }
                else if (!string.IsNullOrWhiteSpace(candidate))
                {
                    hadNonMediaPaths = true;
                }
            }

            if (positivePaths.Count > 0)
            {
                return new FileBackedMediaResult
                {
                    HasPositiveEvidence = true,
                    Kind = primaryKind,
                    PrimaryLocalEvidencePath = primaryPath,
                    LocalEvidenceNames = positiveNames
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    EvidenceOutcome = "POSITIVE_FILE_EVIDENCE"
                };
            }

            return new FileBackedMediaResult
            {
                HasPositiveEvidence = false,
                Kind = "unresolved",
                PrimaryLocalEvidencePath = string.Empty,
                LocalEvidenceNames = Array.Empty<string>(),
                EvidenceOutcome = hadNonMediaPaths ? "UNRESOLVED_FORMAT" : "SKIP_NO_EVIDENCE"
            };
        }

        public static string ClassifyExtension(string extension)
        {
            var ext = (extension ?? string.Empty).Trim().ToLowerInvariant();
            if (BookExtensions.Contains(ext))
            {
                return "book";
            }
            if (ComicExtensions.Contains(ext))
            {
                return "comic";
            }
            if (AudioExtensions.Contains(ext))
            {
                return "audio";
            }
            return "unresolved";
        }

        private static string GetExtension(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var name = MediaCandidateClassifier.NormalizeLocalEvidenceName(path);
            var dotIndex = name.LastIndexOf('.');
            return dotIndex >= 0 ? name.Substring(dotIndex) : string.Empty;
        }
    }
}
