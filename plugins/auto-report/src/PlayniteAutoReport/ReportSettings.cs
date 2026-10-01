using Playnite.SDK;
using Playnite.SDK.Data;
using PlayniteAutoReport.Infrastructure;
using System;
using System.IO;
using System.Text;

namespace PlayniteAutoReport
{
    public sealed class ReportSettings
    {
        public string OutputDirectory { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlayniteReports");

        public bool ExportCsv { get; set; } = true;
        public bool ExportJson { get; set; } = true;
        public bool WriteSummary { get; set; } = true;

        public bool ExportOnApplicationStart { get; set; } = true;
        public bool ExportOnLibraryUpdate { get; set; } = true;
        public bool ExportOnGameStopped { get; set; } = true;

        public bool IncludeHidden { get; set; } = true;
        public bool IncludeUninstalled { get; set; } = true;

        public bool KeepHistory { get; set; } = true;
        public int HistoryRetentionDays { get; set; } = 90;
        public int MaximumHistorySnapshots { get; set; } = 500;
        public int MaximumHistorySizeMegabytes { get; set; } = 512;
        public int MinimumHistoryIntervalMinutes { get; set; } = 60;

        public string ListSeparator { get; set; } = " | ";

        public static ReportSettings LoadOrCreate(string path, ILogger logger)
        {
            if (!File.Exists(path))
            {
                var defaults = new ReportSettings();
                defaults.Normalize();
                defaults.Save(path);
                return defaults;
            }

            try
            {
                return Load(path);
            }
            catch (Exception error)
            {
                logger.Warn(
                    error,
                    "Unable to read Playnite Auto Report settings; defaults will be used without overwriting the invalid file.");

                var defaults = new ReportSettings();
                defaults.Normalize();
                return defaults;
            }
        }

        public static ReportSettings Load(string path)
        {
            ReportSettings loaded;
            Exception error;

            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                loaded.Normalize();
                return loaded;
            }

            throw new InvalidDataException(
                "The Playnite Auto Report configuration is not valid JSON.",
                error);
        }

        public void Save(string path)
        {
            Normalize();
            AtomicFileWriter.WriteText(
                path,
                Serialization.ToJson(this, true),
                new UTF8Encoding(false));
        }

        public string ResolveOutputDirectory(string pluginDataPath)
        {
            Normalize();

            var expanded = Environment.ExpandEnvironmentVariables(OutputDirectory);
            if (Path.IsPathRooted(expanded))
            {
                return Path.GetFullPath(expanded);
            }

            return Path.GetFullPath(Path.Combine(pluginDataPath, expanded));
        }

        public ReportSettings Clone()
        {
            return new ReportSettings
            {
                OutputDirectory = OutputDirectory,
                ExportCsv = ExportCsv,
                ExportJson = ExportJson,
                WriteSummary = WriteSummary,
                ExportOnApplicationStart = ExportOnApplicationStart,
                ExportOnLibraryUpdate = ExportOnLibraryUpdate,
                ExportOnGameStopped = ExportOnGameStopped,
                IncludeHidden = IncludeHidden,
                IncludeUninstalled = IncludeUninstalled,
                KeepHistory = KeepHistory,
                HistoryRetentionDays = HistoryRetentionDays,
                MaximumHistorySnapshots = MaximumHistorySnapshots,
                MaximumHistorySizeMegabytes = MaximumHistorySizeMegabytes,
                MinimumHistoryIntervalMinutes = MinimumHistoryIntervalMinutes,
                ListSeparator = ListSeparator
            };
        }

        private void Normalize()
        {
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                OutputDirectory =
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PlayniteReports");
            }

            if (!ExportCsv && !ExportJson)
            {
                ExportCsv = true;
            }

            if (HistoryRetentionDays < 1)
            {
                HistoryRetentionDays = 1;
            }

            if (MaximumHistorySnapshots < 1)
            {
                MaximumHistorySnapshots = 1;
            }

            if (MaximumHistorySizeMegabytes < 1)
            {
                MaximumHistorySizeMegabytes = 1;
            }

            if (MinimumHistoryIntervalMinutes < 0)
            {
                MinimumHistoryIntervalMinutes = 0;
            }

            if (string.IsNullOrEmpty(ListSeparator))
            {
                ListSeparator = " | ";
            }
        }
    }
}
