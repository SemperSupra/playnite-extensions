using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlayniteAutoReport.Reporting
{
    public static class HistoryPruner
    {
        private const string libraryPrefix = "playnite-library-";
        private const string summaryPrefix = "playnite-summary-";

        public static HistoryPruneResult Prune(
            string historyDirectory,
            DateTime utcNow,
            int retentionDays,
            int maximumSnapshots,
            long maximumBytes)
        {
            if (string.IsNullOrWhiteSpace(historyDirectory))
            {
                throw new ArgumentException(
                    "A history directory is required.",
                    nameof(historyDirectory));
            }

            if (retentionDays < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(retentionDays));
            }

            if (maximumSnapshots < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSnapshots));
            }

            if (maximumBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }

            var result = new HistoryPruneResult();
            if (!Directory.Exists(historyDirectory))
            {
                return result;
            }

            var snapshots = Directory
                .EnumerateFiles(historyDirectory, "*.*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .GroupBy(
                    file => GetSnapshotKey(file.FullName),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new HistorySnapshot(
                    group.Key,
                    group.ToList()))
                .OrderByDescending(snapshot => snapshot.LastWriteTimeUtc)
                .ToList();

            var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cutoffUtc = utcNow.ToUniversalTime().AddDays(-retentionDays);

            foreach (var snapshot in snapshots.Where(
                snapshot => snapshot.LastWriteTimeUtc < cutoffUtc))
            {
                DeleteSnapshot(snapshot, result);
                removedKeys.Add(snapshot.Key);
            }

            var remaining = snapshots
                .Where(snapshot => !removedKeys.Contains(snapshot.Key))
                .OrderByDescending(snapshot => snapshot.LastWriteTimeUtc)
                .ToList();

            foreach (var snapshot in remaining.Skip(maximumSnapshots).ToList())
            {
                DeleteSnapshot(snapshot, result);
                removedKeys.Add(snapshot.Key);
            }

            remaining = remaining
                .Where(snapshot => !removedKeys.Contains(snapshot.Key))
                .OrderByDescending(snapshot => snapshot.LastWriteTimeUtc)
                .ToList();

            var totalBytes = remaining.Sum(snapshot => snapshot.TotalBytes);
            foreach (var snapshot in remaining
                .OrderBy(snapshot => snapshot.LastWriteTimeUtc)
                .ToList())
            {
                if (totalBytes <= maximumBytes)
                {
                    break;
                }

                DeleteSnapshot(snapshot, result);
                totalBytes -= snapshot.TotalBytes;
            }

            DeleteEmptyDirectories(historyDirectory);
            return result;
        }

        private static string GetSnapshotKey(string path)
        {
            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);

            if (name.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(libraryPrefix.Length);
            }
            else if (name.StartsWith(summaryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(summaryPrefix.Length);
            }

            return Path.Combine(directory, name);
        }

        private static void DeleteSnapshot(
            HistorySnapshot snapshot,
            HistoryPruneResult result)
        {
            foreach (var file in snapshot.Files)
            {
                if (!file.Exists)
                {
                    continue;
                }

                var length = file.Length;
                file.Delete();
                result.DeletedFiles++;
                result.DeletedBytes += length;
            }

            result.DeletedSnapshots++;
        }

        private static void DeleteEmptyDirectories(string historyDirectory)
        {
            foreach (var directory in Directory
                .EnumerateDirectories(historyDirectory, "*", SearchOption.AllDirectories)
                .OrderByDescending(path => path.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            if (Directory.Exists(historyDirectory) &&
                !Directory.EnumerateFileSystemEntries(historyDirectory).Any())
            {
                Directory.Delete(historyDirectory);
            }
        }

        private sealed class HistorySnapshot
        {
            public HistorySnapshot(string key, List<FileInfo> files)
            {
                Key = key;
                Files = files;
                LastWriteTimeUtc = files.Max(file => file.LastWriteTimeUtc);
                TotalBytes = files.Sum(file => file.Length);
            }

            public string Key { get; }
            public List<FileInfo> Files { get; }
            public DateTime LastWriteTimeUtc { get; }
            public long TotalBytes { get; }
        }
    }

    public sealed class HistoryPruneResult
    {
        public int DeletedSnapshots { get; set; }
        public int DeletedFiles { get; set; }
        public long DeletedBytes { get; set; }
    }
}
