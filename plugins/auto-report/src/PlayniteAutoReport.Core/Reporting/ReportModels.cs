using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAutoReport.Reporting
{
    public sealed class LibrarySnapshot
    {
        public string SchemaVersion { get; set; } = "1.0";
        public DateTime GeneratedAtUtc { get; set; }
        public string Trigger { get; set; }
        public List<GameReportRecord> Games { get; set; }
        public ReportSummary Summary { get; set; }
    }

    public sealed class GameReportRecord
    {
        public string Name { get; set; }
        public string SortingName { get; set; }
        public string Source { get; set; }
        public string Platforms { get; set; }
        public string Genres { get; set; }
        public string Developers { get; set; }
        public string Publishers { get; set; }
        public string Tags { get; set; }
        public string CompletionStatus { get; set; }

        public string PlayniteId { get; set; }
        public string ProviderGameId { get; set; }
        public string ProviderPluginId { get; set; }

        public bool Installed { get; set; }
        public bool Hidden { get; set; }
        public bool Favorite { get; set; }
        public bool Played { get; set; }
        public string ActivityClass { get; set; }

        public ulong PlaytimeSeconds { get; set; }
        public double PlaytimeHours { get; set; }
        public ulong PlayCount { get; set; }

        public DateTime? LastPlayed { get; set; }
        public DateTime? Added { get; set; }
        public DateTime? Modified { get; set; }

        public int? ReleaseYear { get; set; }
        public string Version { get; set; }
        public string InstallDirectory { get; set; }
        public ulong? InstallSizeBytes { get; set; }
    }

    public sealed class ReportSummary
    {
        public DateTime GeneratedAtUtc { get; set; }
        public string Trigger { get; set; }

        public int TotalGames { get; set; }
        public int InstalledGames { get; set; }
        public int HiddenGames { get; set; }
        public int PlayedGames { get; set; }
        public int NeverPlayedGames { get; set; }
        public double TotalPlaytimeHours { get; set; }

        public List<NamedCount> BySource { get; set; }
        public List<NamedCount> ByActivityClass { get; set; }

        public static ReportSummary FromGames(
            IEnumerable<GameReportRecord> records,
            DateTime generatedAtUtc,
            string trigger)
        {
            var games = records.ToList();

            return new ReportSummary
            {
                GeneratedAtUtc = generatedAtUtc,
                Trigger = trigger,
                TotalGames = games.Count,
                InstalledGames = games.Count(game => game.Installed),
                HiddenGames = games.Count(game => game.Hidden),
                PlayedGames = games.Count(game => game.Played),
                NeverPlayedGames = games.Count(game => !game.Played),
                TotalPlaytimeHours = Math.Round(
                    games.Sum(game => (double)game.PlaytimeSeconds) / 3600d,
                    2),
                BySource = Group(
                    games,
                    game => string.IsNullOrWhiteSpace(game.Source)
                        ? "(no source)"
                        : game.Source),
                ByActivityClass = Group(games, game => game.ActivityClass)
            };
        }

        private static List<NamedCount> Group(
            IEnumerable<GameReportRecord> games,
            Func<GameReportRecord, string> selector)
        {
            return games
                .GroupBy(selector, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new NamedCount
                {
                    Name = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public sealed class NamedCount
    {
        public string Name { get; set; }
        public int Count { get; set; }
    }

    internal static class ActivityClassifier
    {
        public static string Classify(ulong playtimeSeconds)
        {
            if (playtimeSeconds == 0)
            {
                return "Never played";
            }

            var hours = playtimeSeconds / 3600d;
            if (hours < 2)
            {
                return "Briefly tried";
            }

            if (hours < 10)
            {
                return "Sampled";
            }

            if (hours < 40)
            {
                return "Played";
            }

            return "Heavily played";
        }
    }
}
