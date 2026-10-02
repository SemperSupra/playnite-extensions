using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SemperSupraRdteSeeder
{
    public sealed class SemperSupraRdteSeeder : GenericPlugin
    {
        public static readonly Guid PluginGuid = Guid.Parse("6d06cf1b-d1e4-4caa-b6c3-cc6026953135");
        public override Guid Id { get; } = PluginGuid;

        private static readonly Guid SourceHumble = Guid.Parse("70000000-0000-4000-8000-000000000001");
        private static readonly Guid SourceManual = Guid.Parse("70000000-0000-4000-8000-000000000002");
        private static readonly Guid CategoryBook = Guid.Parse("71000000-0000-4000-8000-000000000001");
        private static readonly Guid CategoryComic = Guid.Parse("71000000-0000-4000-8000-000000000002");
        private static readonly Guid CategoryAudio = Guid.Parse("71000000-0000-4000-8000-000000000003");
        private static readonly Guid SeriesMedia = Guid.Parse("72000000-0000-4000-8000-000000000001");
        private static readonly Guid EnrichmentCategoryBook =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401");
        private const string EnrichmentCategoryBookName = "SemperSupra.Media:Book";
        private static readonly Guid EnrichmentFilterPresetBooks =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1501");
        private const string EnrichmentFilterPresetBooksName = "SemperSupra Media: Books";
        private const string ExternalFilterPresetBooksName = "External Books Shelf";

        private static readonly Guid GameBook = Guid.Parse("73000000-0000-4000-8000-000000000001");
        private static readonly Guid GameComic = Guid.Parse("73000000-0000-4000-8000-000000000002");
        private static readonly Guid GameAudio = Guid.Parse("73000000-0000-4000-8000-000000000003");
        private static readonly Guid GameManual = Guid.Parse("73000000-0000-4000-8000-000000000004");

        private static readonly Guid[] FixtureGameIds = { GameBook, GameComic, GameAudio, GameManual };

        public SemperSupraRdteSeeder(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var dataPath = GetPluginUserDataPath();
            var fixturePath = Path.Combine(dataPath, "media");
            Directory.CreateDirectory(fixturePath);

            var profilePath = Path.Combine(dataPath, "fixture-profile.txt");
            var fixtureProfile = File.Exists(profilePath)
                ? File.ReadAllText(profilePath).Trim()
                : "media-baseline-v1";
            var rawMediaProfile = string.Equals(
                fixtureProfile,
                "media-raw-v1",
                StringComparison.OrdinalIgnoreCase);

            if (string.Equals(
                    fixtureProfile,
                    "r4i-conflict-apply-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-conflict-verify-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunR4IConflictFixture(dataPath, fixtureProfile);
                return;
            }

            if (string.Equals(
                    fixtureProfile,
                    "r4i-action-conflict-apply-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-action-conflict-verify-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunActionConflictFixture(dataPath, fixturePath, fixtureProfile);
                return;
            }

            if (string.Equals(
                    fixtureProfile,
                    "r4i-filter-preset-conflict-apply-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-filter-preset-conflict-verify-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunFilterPresetConflictFixture(dataPath, fixtureProfile);
                return;
            }

            var pdfPath = Path.Combine(fixturePath, "rdte-book.pdf");
            var epubPath = Path.Combine(fixturePath, "rdte-book.epub");
            var cbzPath = Path.Combine(fixturePath, "rdte-comic.cbz");
            var flacPath = Path.Combine(fixturePath, "rdte-soundtrack.flac");

            EnsureFile(pdfPath, "%PDF-1.4\n% SemperSupra deterministic RDTE fixture\n");
            EnsureFile(epubPath, "SemperSupra RDTE EPUB placeholder\n");
            EnsureFile(cbzPath, "SemperSupra RDTE CBZ placeholder\n");
            EnsureFile(flacPath, "fLaC\nSemperSupra RDTE audio placeholder\n");

            using (PlayniteApi.Database.BufferedUpdate())
            {
                EnsureMetadata(PlayniteApi.Database.Sources, new GameSource { Id = SourceHumble, Name = "Humble Bundle RDTE" });
                EnsureMetadata(PlayniteApi.Database.Sources, new GameSource { Id = SourceManual, Name = "Manual RDTE" });
                if (!rawMediaProfile)
                {
                    EnsureMetadata(PlayniteApi.Database.Categories, new Category { Id = CategoryBook, Name = "Media: Book" });
                    EnsureMetadata(PlayniteApi.Database.Categories, new Category { Id = CategoryComic, Name = "Media: Comic" });
                    EnsureMetadata(PlayniteApi.Database.Categories, new Category { Id = CategoryAudio, Name = "Media: Audio" });
                    EnsureMetadata(PlayniteApi.Database.Series, new Series { Id = SeriesMedia, Name = "RDTE Media Series" });
                }

                UpsertGame(new Game("RDTE Humble Ebook")
                {
                    Id = GameBook,
                    GameId = "rdte-humble-ebook",
                    SourceId = SourceHumble,
                    CategoryIds = rawMediaProfile ? null : new List<Guid> { CategoryBook },
                    SeriesIds = rawMediaProfile ? null : new List<Guid> { SeriesMedia },
                    IsInstalled = true,
                    OverrideInstallState = true,
                    InstallDirectory = fixturePath,
                    Manual = pdfPath,
                    CoverImage = null,
                    Notes = "RDTE media fixture; variants: " + pdfPath + "; " + epubPath
                });

                UpsertGame(new Game("RDTE Humble Comic")
                {
                    Id = GameComic,
                    GameId = "rdte-humble-comic",
                    SourceId = SourceHumble,
                    CategoryIds = rawMediaProfile ? null : new List<Guid> { CategoryComic },
                    SeriesIds = rawMediaProfile ? null : new List<Guid> { SeriesMedia },
                    IsInstalled = true,
                    OverrideInstallState = true,
                    InstallDirectory = fixturePath,
                    Manual = cbzPath,
                    CoverImage = null,
                    Notes = "RDTE comic fixture: " + cbzPath
                });

                UpsertGame(new Game("RDTE Humble Soundtrack")
                {
                    Id = GameAudio,
                    GameId = "rdte-humble-soundtrack",
                    SourceId = SourceHumble,
                    CategoryIds = rawMediaProfile ? null : new List<Guid> { CategoryAudio },
                    IsInstalled = true,
                    OverrideInstallState = true,
                    InstallDirectory = fixturePath,
                    CoverImage = null,
                    Notes = "RDTE audio fixture: " + flacPath
                });

                UpsertGame(new Game("RDTE Manual Game")
                {
                    Id = GameManual,
                    GameId = "rdte-manual-game",
                    SourceId = SourceManual,
                    IsInstalled = false,
                    OverrideInstallState = true,
                    Manual = pdfPath,
                    Notes = "RDTE ordinary game/manual control fixture"
                });
            }

            var present = FixtureGameIds
                .Select(id => PlayniteApi.Database.Games.Get(id))
                .Where(game => game != null)
                .ToList();

            var receipt = new
            {
                schema = "sempersupra-playnite-fixture-seed/v1",
                fixture_set = fixtureProfile,
                expected_fixture_games = FixtureGameIds.Length,
                observed_fixture_games = present.Count,
                fixture_game_ids = FixtureGameIds.Select(id => id.ToString()).ToArray(),
                total_library_games = PlayniteApi.Database.Games.Count,
                media_path = fixturePath
            };

            File.WriteAllText(
                Path.Combine(dataPath, "seed-receipt.json"),
                Serialization.ToJson(receipt, true));
        }

        private void RunR4IConflictFixture(string dataPath, string fixtureProfile)
        {
            var category = PlayniteApi.Database.Categories.Get(EnrichmentCategoryBook);
            var game = PlayniteApi.Database.Games.Get(GameManual);
            var apply = string.Equals(
                fixtureProfile,
                "r4i-conflict-apply-v1",
                StringComparison.OrdinalIgnoreCase);

            string result = "PASS";
            string detail = string.Empty;

            try
            {
                if (category == null ||
                    !string.Equals(
                        category.Name,
                        EnrichmentCategoryBookName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Expected plugin-owned Book category is unavailable or renamed.");
                }

                if (game == null)
                {
                    throw new InvalidOperationException(
                        "Manual control game is unavailable.");
                }

                if (apply)
                {
                    var categories = game.CategoryIds == null
                        ? new List<Guid>()
                        : new List<Guid>(game.CategoryIds);

                    if (!categories.Contains(EnrichmentCategoryBook))
                    {
                        categories.Add(EnrichmentCategoryBook);
                        game.CategoryIds = categories;
                        PlayniteApi.Database.Games.Update(game);
                    }
                }

                var verified = PlayniteApi.Database.Games.Get(GameManual);
                var membershipPresent =
                    verified != null &&
                    verified.CategoryIds != null &&
                    verified.CategoryIds.Contains(EnrichmentCategoryBook);

                if (!membershipPresent)
                {
                    throw new InvalidOperationException(
                        "External manual-game category membership is not present.");
                }

                detail = apply
                    ? "External membership applied and verified."
                    : "External membership remains present.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            var currentCategory =
                PlayniteApi.Database.Categories.Get(EnrichmentCategoryBook);
            var currentGame = PlayniteApi.Database.Games.Get(GameManual);
            var currentMembership =
                currentGame != null &&
                currentGame.CategoryIds != null &&
                currentGame.CategoryIds.Contains(EnrichmentCategoryBook);

            File.WriteAllText(
                Path.Combine(dataPath, "conflict-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-r4i-conflict-fixture/v1",
                        mode = apply ? "apply" : "verify",
                        result = result,
                        manual_game_id = GameManual.ToString(),
                        category_id = EnrichmentCategoryBook.ToString(),
                        category_name = currentCategory == null
                            ? string.Empty
                            : currentCategory.Name,
                        membership_present = currentMembership,
                        detail = detail
                    },
                    true));
        }

        private void RunActionConflictFixture(
            string dataPath,
            string fixturePath,
            string fixtureProfile)
        {
            var apply = string.Equals(
                fixtureProfile,
                "r4i-action-conflict-apply-v1",
                StringComparison.OrdinalIgnoreCase);
            var externalPath = Path.Combine(fixturePath, "external-book.pdf");
            EnsureFile(
                externalPath,
                "%PDF-1.4\n% SemperSupra external action conflict fixture\n");

            string result = "PASS";
            string detail = string.Empty;

            try
            {
                var game = PlayniteApi.Database.Games.Get(GameBook);
                if (game == null)
                {
                    throw new InvalidOperationException(
                        "Humble Ebook fixture is unavailable.");
                }

                var readActions = game.GameActions == null
                    ? new List<GameAction>()
                    : game.GameActions
                        .Where(action =>
                            !action.IsPlayAction &&
                            string.Equals(
                                action.Name,
                                "Read",
                                StringComparison.Ordinal))
                        .ToList();

                if (apply)
                {
                    if (readActions.Count != 1)
                    {
                        throw new InvalidOperationException(
                            "Expected exactly one plugin-projected custom Read action.");
                    }

                    readActions[0].Path = externalPath;
                    readActions[0].WorkingDir = fixturePath;
                    PlayniteApi.Database.Games.Update(game);
                }

                var verified = PlayniteApi.Database.Games.Get(GameBook);
                var preserved = verified != null &&
                    verified.GameActions != null &&
                    verified.GameActions.Any(action =>
                        !action.IsPlayAction &&
                        string.Equals(action.Name, "Read", StringComparison.Ordinal) &&
                        string.Equals(action.Path, externalPath, StringComparison.Ordinal));

                if (!preserved)
                {
                    throw new InvalidOperationException(
                        "Externally changed custom Read action is not present.");
                }

                if (verified.Playtime != 0 ||
                    verified.PlayCount != 0 ||
                    verified.LastActivity.HasValue)
                {
                    throw new InvalidOperationException(
                        "Custom action projection changed gameplay activity semantics.");
                }

                detail = apply
                    ? "External Read action mutation applied and verified."
                    : "External Read action mutation remains present.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            var current = PlayniteApi.Database.Games.Get(GameBook);
            var currentAction = current == null || current.GameActions == null
                ? null
                : current.GameActions.FirstOrDefault(action =>
                    !action.IsPlayAction &&
                    string.Equals(action.Name, "Read", StringComparison.Ordinal) &&
                    string.Equals(action.Path, externalPath, StringComparison.Ordinal));

            File.WriteAllText(
                Path.Combine(dataPath, "action-conflict-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-r4i-action-conflict-fixture/v1",
                        mode = apply ? "apply" : "verify",
                        result = result,
                        book_game_id = GameBook.ToString(),
                        action_name = currentAction == null
                            ? string.Empty
                            : currentAction.Name,
                        action_path_name = currentAction == null
                            ? string.Empty
                            : Path.GetFileName(currentAction.Path),
                        is_play_action = currentAction != null && currentAction.IsPlayAction,
                        playtime = current == null ? 0UL : current.Playtime,
                        play_count = current == null ? 0UL : current.PlayCount,
                        last_activity_present =
                            current != null && current.LastActivity.HasValue,
                        last_activity =
                            current != null && current.LastActivity.HasValue
                                ? current.LastActivity.Value.ToString("o")
                                : string.Empty,
                        detail = detail
                    },
                    true));
        }


        private void RunFilterPresetConflictFixture(
            string dataPath,
            string fixtureProfile)
        {
            var apply = string.Equals(
                fixtureProfile,
                "r4i-filter-preset-conflict-apply-v1",
                StringComparison.OrdinalIgnoreCase);

            string result = "PASS";
            string detail = string.Empty;

            try
            {
                var preset =
                    PlayniteApi.Database.FilterPresets.Get(EnrichmentFilterPresetBooks);
                if (preset == null)
                {
                    throw new InvalidOperationException(
                        "Expected plugin-owned Books filter preset is unavailable.");
                }

                if (apply)
                {
                    if (!string.Equals(
                            preset.Name,
                            EnrichmentFilterPresetBooksName,
                            StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Books filter preset is not in plugin-owned state before external mutation.");
                    }

                    preset.Name = ExternalFilterPresetBooksName;
                    PlayniteApi.Database.FilterPresets.Update(preset);
                }

                var verified =
                    PlayniteApi.Database.FilterPresets.Get(EnrichmentFilterPresetBooks);
                var categoryIds =
                    verified == null ||
                    verified.Settings == null ||
                    verified.Settings.Category == null
                        ? null
                        : verified.Settings.Category.Ids;

                var preserved =
                    verified != null &&
                    string.Equals(
                        verified.Name,
                        ExternalFilterPresetBooksName,
                        StringComparison.Ordinal) &&
                    categoryIds != null &&
                    categoryIds.Count == 1 &&
                    categoryIds[0] == EnrichmentCategoryBook;

                if (!preserved)
                {
                    throw new InvalidOperationException(
                        "Externally changed Books filter preset is not present.");
                }

                var category =
                    PlayniteApi.Database.Categories.Get(EnrichmentCategoryBook);
                if (category == null ||
                    !string.Equals(
                        category.Name,
                        EnrichmentCategoryBookName,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Books category referenced by external filter preset is unavailable.");
                }

                detail = apply
                    ? "External Books filter-preset mutation applied and verified."
                    : "External Books filter-preset mutation and referenced category remain present.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            var current =
                PlayniteApi.Database.FilterPresets.Get(EnrichmentFilterPresetBooks);
            var currentCategory =
                PlayniteApi.Database.Categories.Get(EnrichmentCategoryBook);
            var currentCategoryIds =
                current == null ||
                current.Settings == null ||
                current.Settings.Category == null
                    ? null
                    : current.Settings.Category.Ids;

            File.WriteAllText(
                Path.Combine(dataPath, "filter-preset-conflict-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-r4i-filter-preset-conflict-fixture/v1",
                        mode = apply ? "apply" : "verify",
                        result = result,
                        preset_id = EnrichmentFilterPresetBooks.ToString(),
                        preset_name = current == null
                            ? string.Empty
                            : current.Name,
                        category_id = EnrichmentCategoryBook.ToString(),
                        category_reference_present =
                            currentCategoryIds != null &&
                            currentCategoryIds.Count == 1 &&
                            currentCategoryIds[0] == EnrichmentCategoryBook,
                        category_present = currentCategory != null,
                        detail = detail
                    },
                    true));
        }

        private static void EnsureFile(string path, string content)
        {
            if (!File.Exists(path) || File.ReadAllText(path) != content)
            {
                File.WriteAllText(path, content);
            }
        }

        private static void EnsureMetadata<T>(IItemCollection<T> collection, T desired) where T : DatabaseObject
        {
            var existing = collection.Get(desired.Id);
            if (existing == null)
            {
                collection.Add(desired);
                return;
            }

            if (!string.Equals(existing.Name, desired.Name, StringComparison.Ordinal))
            {
                existing.Name = desired.Name;
                collection.Update(existing);
            }
        }

        private void UpsertGame(Game desired)
        {
            var existing = PlayniteApi.Database.Games.Get(desired.Id);
            if (existing == null)
            {
                PlayniteApi.Database.Games.Add(desired);
                return;
            }

            existing.Name = desired.Name;
            existing.GameId = desired.GameId;
            existing.SourceId = desired.SourceId;
            existing.CategoryIds = desired.CategoryIds == null ? null : new List<Guid>(desired.CategoryIds);
            existing.SeriesIds = desired.SeriesIds == null ? null : new List<Guid>(desired.SeriesIds);
            existing.IsInstalled = desired.IsInstalled;
            existing.OverrideInstallState = desired.OverrideInstallState;
            existing.InstallDirectory = desired.InstallDirectory;
            existing.Manual = desired.Manual;
            existing.CoverImage = desired.CoverImage;
            existing.Notes = desired.Notes;
            PlayniteApi.Database.Games.Update(existing);
        }
    }
}
