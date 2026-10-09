using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaLibraryEnrichment
{
    // Read-only, explicitly rooted first step toward media discovery. It does
    // not associate files with games or authorize enrichment mutations.
    public static class MediaFileInventory
    {
        public static MediaFileInventoryResult InspectImmediateFiles(
            string explicitRoot,
            int maxEntries = 4096)
        {
            if (string.IsNullOrWhiteSpace(explicitRoot))
            {
                throw new ArgumentException("An explicit directory root is required.", nameof(explicitRoot));
            }
            if (maxEntries < 1 || maxEntries > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEntries));
            }
            if (!Path.IsPathRooted(explicitRoot))
            {
                throw new ArgumentException("Relative scan roots are prohibited.", nameof(explicitRoot));
            }

            var root = Path.GetFullPath(explicitRoot);
            if (root.StartsWith(@"\\", StringComparison.Ordinal) ||
                root.StartsWith("//", StringComparison.Ordinal))
            {
                throw new NotSupportedException("Network scan roots are not supported.");
            }

            // Never scan an entire drive/volume by accepting its root.
            var volumeRoot = Path.GetPathRoot(root);
            if (string.Equals(
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Volume roots are prohibited.", nameof(explicitRoot));
            }

            var directory = new DirectoryInfo(root);
            if (!directory.Exists)
            {
                throw new DirectoryNotFoundException("The selected directory does not exist.");
            }
            // A lexical child path can still escape through a parent junction.
            // Reject any reparse point in the complete selected-directory ancestry.
            for (var ancestor = directory; ancestor != null; ancestor = ancestor.Parent)
            {
                if ((ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new NotSupportedException("Reparse-point directory roots or ancestors are prohibited.");
                }
            }

            var results = new List<MediaFileInventoryItem>();
            var examined = 0;
            var skippedLinks = 0;

            // Top-level only. No subtree traversal and no files are opened.
            foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                examined++;
                if (examined > maxEntries)
                {
                    // Return no partial inventory when the declared cap is exceeded.
                    throw new InvalidOperationException("Directory exceeds the bounded inventory limit.");
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    skippedLinks++;
                    continue;
                }

                if (!(entry is FileInfo))
                {
                    continue;
                }
                var kind = MediaCandidateClassifier.ClassifyKind(entry.Name, null);
                if (!string.Equals(kind, "unresolved", StringComparison.Ordinal))
                {
                    results.Add(new MediaFileInventoryItem
                    {
                        FileName = entry.Name,
                        Kind = kind
                    });
                }
            }

            return new MediaFileInventoryResult
            {
                EntriesExamined = examined,
                ReparsePointsSkipped = skippedLinks,
                SupportedFiles = results
                    .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.FileName, StringComparer.Ordinal)
                    .ToArray()
            };
        }
    }

    public sealed class MediaFileInventoryItem
    {
        public string FileName { get; set; }
        public string Kind { get; set; }
    }

    public sealed class MediaFileInventoryResult
    {
        public int EntriesExamined { get; set; }
        public int ReparsePointsSkipped { get; set; }
        public MediaFileInventoryItem[] SupportedFiles { get; set; }
    }
}
