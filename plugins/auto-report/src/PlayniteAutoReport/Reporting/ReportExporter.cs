using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAutoReport.Infrastructure;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace PlayniteAutoReport.Reporting
{
    internal sealed class ReportExporter
    {
        private const string libraryCsvName = "playnite-library-latest.csv";
        private const string libraryJsonName = "playnite-library-latest.json";
        private const string summaryJsonName = "playnite-summary-latest.json";
        private const string lastHistoryName = ".last-history-utc";

        private readonly string pluginDataPath;
        private readonly ILogger logger;

        public ReportExporter(string pluginDataPath, ILogger logger)
        {
            this.pluginDataPath = pluginDataPath;
            this.logger = logger;
        }

        public ExportResult Export(LibrarySnapshot snapshot, ReportSettings settings)
        {
            var outputDirectory = settings.ResolveOutputDirectory(pluginDataPath);
            Directory.CreateDirectory(outputDirectory);

            var result = new ExportResult
            {
                OutputDirectory = outputDirectory,
                GameCount = snapshot.Games.Count
            };

            if (settings.ExportCsv)
            {
                result.LatestCsvPath = Path.Combine(outputDirectory, libraryCsvName);
                CsvWriter.Write(result.LatestCsvPath, snapshot.Games);
            }

            if (settings.ExportJson)
            {
                result.LatestJsonPath = Path.Combine(outputDirectory, libraryJsonName);
                WriteJson(result.LatestJsonPath, snapshot);
            }

            if (settings.WriteSummary)
            {
                result.LatestSummaryPath = Path.Combine(outputDirectory, summaryJsonName);
                WriteJson(result.LatestSummaryPath, snapshot.Summary);
            }

            if (settings.KeepHistory && ShouldWriteHistory(outputDirectory, settings))
            {
                WriteHistory(outputDirectory, snapshot, settings);
                RecordHistoryTime(outputDirectory, snapshot.GeneratedAtUtc);
            }

            PruneHistory(outputDirectory, settings);
            return result;
        }

        private static void WriteHistory(
            string outputDirectory,
            LibrarySnapshot snapshot,
            ReportSettings settings)
        {
            var historyDirectory = Path.Combine(
                outputDirectory,
                "history",
                snapshot.GeneratedAtUtc.ToString("yyyy", CultureInfo.InvariantCulture),
                snapshot.GeneratedAtUtc.ToString("MM", CultureInfo.InvariantCulture));

            Directory.CreateDirectory(historyDirectory);

            var stamp = snapshot.GeneratedAtUtc.ToString(
                "yyyyMMdd-HHmmssfff",
                CultureInfo.InvariantCulture);
            var trigger = SanitizeFileName(snapshot.Trigger);
            var baseName = "playnite-library-" + stamp + "-" + trigger;

            if (settings.ExportCsv)
            {
                CsvWriter.Write(
                    Path.Combine(historyDirectory, baseName + ".csv"),
                    snapshot.Games);
            }

            if (settings.ExportJson)
            {
                WriteJson(
                    Path.Combine(historyDirectory, baseName + ".json"),
                    snapshot);
            }

            if (settings.WriteSummary)
            {
                WriteJson(
                    Path.Combine(
                        historyDirectory,
                        "playnite-summary-" + stamp + "-" + trigger + ".json"),
                    snapshot.Summary);
            }
        }

        private static bool ShouldWriteHistory(
            string outputDirectory,
            ReportSettings settings)
        {
            if (settings.MinimumHistoryIntervalMinutes == 0)
            {
                return true;
            }

            var markerPath = Path.Combine(outputDirectory, lastHistoryName);
            if (!File.Exists(markerPath))
            {
                return true;
            }

            DateTime lastHistoryUtc;
            if (!DateTime.TryParse(
                File.ReadAllText(markerPath),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out lastHistoryUtc))
            {
                return true;
            }

            return DateTime.UtcNow - lastHistoryUtc.ToUniversalTime() >=
                TimeSpan.FromMinutes(settings.MinimumHistoryIntervalMinutes);
        }

        private static void RecordHistoryTime(
            string outputDirectory,
            DateTime generatedAtUtc)
        {
            AtomicFileWriter.WriteText(
                Path.Combine(outputDirectory, lastHistoryName),
                generatedAtUtc.ToString("o", CultureInfo.InvariantCulture),
                new UTF8Encoding(false));
        }

        private void PruneHistory(
            string outputDirectory,
            ReportSettings settings)
        {
            var historyDirectory = Path.Combine(outputDirectory, "history");
            var maximumBytes =
                (long)settings.MaximumHistorySizeMegabytes * 1024L * 1024L;

            try
            {
                var result = HistoryPruner.Prune(
                    historyDirectory,
                    DateTime.UtcNow,
                    settings.HistoryRetentionDays,
                    settings.MaximumHistorySnapshots,
                    maximumBytes);

                if (result.DeletedFiles > 0)
                {
                    logger.Debug(
                        "Pruned {0} history snapshots, {1} files, and {2} bytes.",
                        result.DeletedSnapshots,
                        result.DeletedFiles,
                        result.DeletedBytes);
                }
            }
            catch (Exception error)
            {
                logger.Warn(
                    error,
                    "Unable to prune Playnite Auto Report history files.");
            }
        }

        private static void WriteJson(string path, object value)
        {
            AtomicFileWriter.WriteText(
                path,
                Serialization.ToJson(value, true),
                new UTF8Encoding(false));
        }

        private static string SanitizeFileName(string value)
        {
            var result = string.IsNullOrWhiteSpace(value) ? "unknown" : value;
            foreach (var character in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(character, '-');
            }

            return result.Replace(' ', '-').ToLowerInvariant();
        }
    }

    internal sealed class ExportResult
    {
        public string OutputDirectory { get; set; }
        public int GameCount { get; set; }
        public string LatestCsvPath { get; set; }
        public string LatestJsonPath { get; set; }
        public string LatestSummaryPath { get; set; }
    }
}
