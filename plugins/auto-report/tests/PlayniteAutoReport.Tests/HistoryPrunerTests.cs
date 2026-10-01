using PlayniteAutoReport.Reporting;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PlayniteAutoReport.Tests
{
    public sealed class HistoryPrunerTests
    {
        [Fact]
        public void PruneRemovesSnapshotsOlderThanRetentionPeriod()
        {
            var directory = CreateTemporaryDirectory();

            try
            {
                var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
                CreateSnapshot(directory, "old", now.AddDays(-31), 20);
                CreateSnapshot(directory, "current", now.AddDays(-2), 20);

                var result = HistoryPruner.Prune(directory, now, 30, 100, 1024 * 1024);

                Assert.Equal(1, result.DeletedSnapshots);
                Assert.Equal(3, result.DeletedFiles);
                Assert.Equal(3, Directory.EnumerateFiles(
                    directory,
                    "*.*",
                    SearchOption.AllDirectories).Count());
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void PruneKeepsOnlyNewestConfiguredSnapshotCount()
        {
            var directory = CreateTemporaryDirectory();

            try
            {
                var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
                for (var index = 0; index < 5; index++)
                {
                    CreateSnapshot(
                        directory,
                        "snapshot-" + index,
                        now.AddMinutes(index),
                        20);
                }

                var result = HistoryPruner.Prune(directory, now, 365, 2, 1024 * 1024);

                Assert.Equal(3, result.DeletedSnapshots);
                Assert.Equal(9, result.DeletedFiles);
                Assert.Equal(6, Directory.EnumerateFiles(
                    directory,
                    "*.*",
                    SearchOption.AllDirectories).Count());
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        [Fact]
        public void PruneRemovesOldestSnapshotsUntilSizeLimitIsMet()
        {
            var directory = CreateTemporaryDirectory();

            try
            {
                var now = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
                CreateSnapshot(directory, "oldest", now.AddMinutes(-2), 100);
                CreateSnapshot(directory, "middle", now.AddMinutes(-1), 100);
                CreateSnapshot(directory, "newest", now, 100);

                var result = HistoryPruner.Prune(directory, now, 365, 100, 350);

                Assert.Equal(2, result.DeletedSnapshots);
                Assert.Equal(6, result.DeletedFiles);
                Assert.Equal(3, Directory.EnumerateFiles(
                    directory,
                    "*.*",
                    SearchOption.AllDirectories).Count());
            }
            finally
            {
                DeleteTemporaryDirectory(directory);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "PlayniteAutoReportTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void CreateSnapshot(
            string root,
            string key,
            DateTime lastWriteTimeUtc,
            int bytesPerFile)
        {
            var directory = Path.Combine(root, "2026", "08");
            Directory.CreateDirectory(directory);

            var paths = new[]
            {
                Path.Combine(directory, "playnite-library-" + key + ".csv"),
                Path.Combine(directory, "playnite-library-" + key + ".json"),
                Path.Combine(directory, "playnite-summary-" + key + ".json")
            };

            foreach (var path in paths)
            {
                File.WriteAllBytes(path, new byte[bytesPerFile]);
                File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
            }
        }

        private static void DeleteTemporaryDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
