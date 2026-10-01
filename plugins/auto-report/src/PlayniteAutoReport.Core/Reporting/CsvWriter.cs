using PlayniteAutoReport.Infrastructure;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PlayniteAutoReport.Reporting
{
    internal static class CsvWriter
    {
        private static readonly string[] headers =
        {
            "Name",
            "SortingName",
            "Source",
            "Platforms",
            "Genres",
            "Developers",
            "Publishers",
            "Tags",
            "CompletionStatus",
            "PlayniteId",
            "ProviderGameId",
            "ProviderPluginId",
            "Installed",
            "Hidden",
            "Favorite",
            "Played",
            "ActivityClass",
            "PlaytimeSeconds",
            "PlaytimeHours",
            "PlayCount",
            "LastPlayed",
            "Added",
            "Modified",
            "ReleaseYear",
            "Version",
            "InstallDirectory",
            "InstallSizeBytes"
        };

        public static void Write(string path, IEnumerable<GameReportRecord> records)
        {
            AtomicFileWriter.Write(path, stream =>
            {
                using (var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(true),
                    4096,
                    true))
                {
                    WriteRow(writer, headers);

                    foreach (var record in records)
                    {
                        WriteRow(writer, new[]
                        {
                            record.Name,
                            record.SortingName,
                            record.Source,
                            record.Platforms,
                            record.Genres,
                            record.Developers,
                            record.Publishers,
                            record.Tags,
                            record.CompletionStatus,
                            record.PlayniteId,
                            record.ProviderGameId,
                            record.ProviderPluginId,
                            FormatBoolean(record.Installed),
                            FormatBoolean(record.Hidden),
                            FormatBoolean(record.Favorite),
                            FormatBoolean(record.Played),
                            record.ActivityClass,
                            record.PlaytimeSeconds.ToString(CultureInfo.InvariantCulture),
                            record.PlaytimeHours.ToString("0.00", CultureInfo.InvariantCulture),
                            record.PlayCount.ToString(CultureInfo.InvariantCulture),
                            FormatDate(record.LastPlayed),
                            FormatDate(record.Added),
                            FormatDate(record.Modified),
                            record.ReleaseYear.HasValue
                                ? record.ReleaseYear.Value.ToString(CultureInfo.InvariantCulture)
                                : string.Empty,
                            record.Version,
                            record.InstallDirectory,
                            record.InstallSizeBytes.HasValue
                                ? record.InstallSizeBytes.Value.ToString(CultureInfo.InvariantCulture)
                                : string.Empty
                        });
                    }
                }
            });
        }

        private static void WriteRow(TextWriter writer, IEnumerable<string> fields)
        {
            var first = true;
            foreach (var field in fields)
            {
                if (!first)
                {
                    writer.Write(',');
                }

                writer.Write(Escape(field));
                first = false;
            }

            writer.WriteLine();
        }

        private static string Escape(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string FormatBoolean(bool value)
        {
            return value ? "true" : "false";
        }

        private static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("o", CultureInfo.InvariantCulture)
                : string.Empty;
        }
    }
}
