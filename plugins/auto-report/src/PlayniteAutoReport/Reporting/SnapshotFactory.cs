using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PlayniteAutoReport.Reporting
{
    internal static class SnapshotFactory
    {
        public static LibrarySnapshot Create(
            IEnumerable<Game> sourceGames,
            ReportSettings settings,
            string trigger)
        {
            var records = sourceGames
                .Where(game => settings.IncludeHidden || !game.Hidden)
                .Where(game => settings.IncludeUninstalled || game.IsInstalled)
                .Select(game => FromGame(game, settings.ListSeparator))
                .OrderBy(game => game.Source, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var generatedAtUtc = DateTime.UtcNow;
            return new LibrarySnapshot
            {
                GeneratedAtUtc = generatedAtUtc,
                Trigger = trigger,
                Games = records,
                Summary = ReportSummary.FromGames(records, generatedAtUtc, trigger)
            };
        }

        private static GameReportRecord FromGame(Game game, string separator)
        {
            var played = game.Playtime > 0 || game.PlayCount > 0;

            return new GameReportRecord
            {
                Name = game.Name ?? string.Empty,
                SortingName = game.SortingName ?? string.Empty,
                Source = game.Source == null
                    ? string.Empty
                    : game.Source.Name ?? string.Empty,
                Platforms = JoinNames(game.Platforms, separator),
                Genres = JoinNames(game.Genres, separator),
                Developers = JoinNames(game.Developers, separator),
                Publishers = JoinNames(game.Publishers, separator),
                Tags = JoinNames(game.Tags, separator),
                CompletionStatus = game.CompletionStatus == null
                    ? string.Empty
                    : game.CompletionStatus.Name ?? string.Empty,

                PlayniteId = game.Id.ToString(),
                ProviderGameId = game.GameId ?? string.Empty,
                ProviderPluginId = game.PluginId.ToString(),

                Installed = game.IsInstalled,
                Hidden = game.Hidden,
                Favorite = game.Favorite,
                Played = played,
                ActivityClass = ActivityClassifier.Classify(game.Playtime),

                PlaytimeSeconds = game.Playtime,
                PlaytimeHours = Math.Round(game.Playtime / 3600d, 2),
                PlayCount = game.PlayCount,

                LastPlayed = game.LastActivity,
                Added = game.Added,
                Modified = game.Modified,

                ReleaseYear = game.ReleaseYear,
                Version = game.Version ?? string.Empty,
                InstallDirectory = game.InstallDirectory ?? string.Empty,
                InstallSizeBytes = game.InstallSize
            };
        }

        private static string JoinNames<T>(IEnumerable<T> items, string separator)
            where T : DatabaseObject
        {
            if (items == null)
            {
                return string.Empty;
            }

            return string.Join(
                separator,
                items
                    .Where(item =>
                        item != null &&
                        !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => item.Name)
                    .OrderBy(
                        name => name,
                        StringComparer.CurrentCultureIgnoreCase));
        }
    }
}
