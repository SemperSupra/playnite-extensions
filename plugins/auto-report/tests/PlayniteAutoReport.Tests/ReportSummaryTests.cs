using PlayniteAutoReport.Reporting;
using System;
using System.Collections.Generic;
using Xunit;

namespace PlayniteAutoReport.Tests
{
    public sealed class ReportSummaryTests
    {
        [Fact]
        public void FromGamesAggregatesCountsAndPlaytime()
        {
            var generatedAt = new DateTime(
                2026,
                8,
                9,
                12,
                0,
                0,
                DateTimeKind.Utc);

            var games = new List<GameReportRecord>
            {
                new GameReportRecord
                {
                    Name = "A",
                    Source = "Steam",
                    Installed = true,
                    Played = true,
                    ActivityClass = "Played",
                    PlaytimeSeconds = 3600
                },
                new GameReportRecord
                {
                    Name = "B",
                    Source = "steam",
                    Hidden = true,
                    Played = false,
                    ActivityClass = "Never played",
                    PlaytimeSeconds = 0
                },
                new GameReportRecord
                {
                    Name = "C",
                    Source = string.Empty,
                    Installed = true,
                    Played = true,
                    ActivityClass = "Briefly tried",
                    PlaytimeSeconds = 1800
                }
            };

            var result = ReportSummary.FromGames(
                games,
                generatedAt,
                "test");

            Assert.Equal(3, result.TotalGames);
            Assert.Equal(2, result.InstalledGames);
            Assert.Equal(1, result.HiddenGames);
            Assert.Equal(2, result.PlayedGames);
            Assert.Equal(1, result.NeverPlayedGames);
            Assert.Equal(1.5d, result.TotalPlaytimeHours);
            Assert.Equal(2, result.BySource[0].Count);
            Assert.Equal("Steam", result.BySource[0].Name);
            Assert.Equal("(no source)", result.BySource[1].Name);
        }
    }
}
