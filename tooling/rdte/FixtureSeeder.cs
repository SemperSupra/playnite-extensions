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
        private static readonly Guid EnrichmentCategoryComic =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1402");
        private static readonly Guid EnrichmentCategoryAudio =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1403");
        private const string EnrichmentCategoryBookName = "SemperSupra.Media:Book";
        private const string EnrichmentCategoryComicName = "SemperSupra.Media:Comic";
        private const string EnrichmentCategoryAudioName = "SemperSupra.Media:Audio";
        private static readonly Guid EnrichmentFilterPresetBooks =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1501");
        private static readonly Guid EnrichmentFilterPresetComics =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1502");
        private static readonly Guid EnrichmentFilterPresetAudio =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1503");
        private const string EnrichmentFilterPresetBooksName = "SemperSupra Media: Books";
        private const string EnrichmentFilterPresetComicsName = "SemperSupra Media: Comics";
        private const string EnrichmentFilterPresetAudioName = "SemperSupra Media: Audio";
        private const string ExternalFilterPresetBooksName = "External Books Shelf";

        private static readonly Guid GameBook = Guid.Parse("73000000-0000-4000-8000-000000000001");
        private static readonly Guid GameComic = Guid.Parse("73000000-0000-4000-8000-000000000002");
        private static readonly Guid GameAudio = Guid.Parse("73000000-0000-4000-8000-000000000003");
        private static readonly Guid GameManual = Guid.Parse("73000000-0000-4000-8000-000000000004");
        private static readonly Guid GameManualMedia = Guid.Parse("73000000-0000-4000-8000-000000000005");

        private static readonly Guid[] FixtureGameIds = { GameBook, GameComic, GameAudio, GameManual, GameManualMedia };

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
                    "r4i-native-persistence-verify-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunNativePersistenceFixture(dataPath, fixturePath);
                return;
            }

            if (string.Equals(
                    fixtureProfile,
                    "r4i-user-category-override-remove-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-user-category-override-verify-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-user-category-override-restore-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunUserCategoryOverrideFixture(dataPath, fixtureProfile);
                return;
            }

            if (string.Equals(
                    fixtureProfile,
                    "r4i-user-action-override-remove-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-user-action-override-verify-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-user-action-override-restore-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunUserActionOverrideFixture(dataPath, fixtureProfile);
                return;
            }

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

            if (string.Equals(
                    fixtureProfile,
                    "r4i-cover-conflict-apply-v1",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fixtureProfile,
                    "r4i-cover-conflict-verify-v1",
                    StringComparison.OrdinalIgnoreCase))
            {
                RunCoverConflictFixture(dataPath, fixturePath, fixtureProfile);
                return;
            }

            var pdfPath = Path.Combine(fixturePath, "rdte-book.pdf");
            var epubPath = Path.Combine(fixturePath, "rdte-book.epub");
            var cbzPath = Path.Combine(fixturePath, "rdte-comic.cbz");
            var flacPath = Path.Combine(fixturePath, "rdte-soundtrack.flac");
            var bookCoverPath = Path.Combine(fixturePath, "rdte-book-cover.png");
            var comicCoverPath = Path.Combine(fixturePath, "rdte-comic-cover.png");

            EnsureFile(pdfPath, "%PDF-1.4\n% SemperSupra deterministic RDTE fixture\n");
            EnsureFile(epubPath, "SemperSupra RDTE EPUB placeholder\n");
            EnsureFile(cbzPath, "SemperSupra RDTE CBZ placeholder\n");
            EnsureFile(flacPath, "fLaC\nSemperSupra RDTE audio placeholder\n");
            EnsureBytes(
                bookCoverPath,
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAACAAAAAwCAIAAAD/zu84AAAAL0lEQVR4nO3NQQEAAATAQISTWEAl+N0C7HK647N6vQMAAAAAAAAAAAAAAAAAAI5bNqQBoI/pblYAAAAASUVORK5CYII="));
            EnsureBytes(
                comicCoverPath,
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAACAAAAAwCAIAAAD/zu84AAAAMklEQVR4nO3NMQEAMAjAsDFxKEEiAjEBXyqgiax+l/3TOwAAAAAAAAAAAAAAAAAAYLkBV0oBvtuG7qwAAAAASUVORK5CYII="));

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

                UpsertGame(new Game("RDTE Manual Media Book")
                {
                    Id = GameManualMedia,
                    GameId = "rdte-manual-media-book",
                    SourceId = SourceManual,
                    IsInstalled = true,
                    OverrideInstallState = true,
                    InstallDirectory = fixturePath,
                    Manual = pdfPath,
                    CoverImage = null,
                    Notes = "RDTE explicit admission media fixture: " + pdfPath
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

        private void RunNativePersistenceFixture(
            string dataPath,
            string fixturePath)
        {
            string result = "PASS";
            string detail = string.Empty;
            var categoryMembershipCount = 0;
            var actionCount = 0;
            var filterPresetCount = 0;
            var coverCount = 0;
            var localFilesPresent = false;

            try
            {
                var expectations = new[]
                {
                    new
                    {
                        GameId = GameBook,
                        CategoryId = EnrichmentCategoryBook,
                        ActionName = "Read",
                        ActionPathName = "rdte-book.pdf"
                    },
                    new
                    {
                        GameId = GameComic,
                        CategoryId = EnrichmentCategoryComic,
                        ActionName = "Read",
                        ActionPathName = "rdte-comic.cbz"
                    },
                    new
                    {
                        GameId = GameAudio,
                        CategoryId = EnrichmentCategoryAudio,
                        ActionName = "Listen",
                        ActionPathName = "rdte-soundtrack.flac"
                    },
                    new
                    {
                        GameId = GameManualMedia,
                        CategoryId = EnrichmentCategoryBook,
                        ActionName = "Read",
                        ActionPathName = "rdte-book.pdf"
                    }
                };

                foreach (var expected in expectations)
                {
                    var game = PlayniteApi.Database.Games.Get(expected.GameId);
                    if (game == null)
                    {
                        throw new InvalidOperationException(
                            "Expected enriched fixture game is unavailable: " +
                            expected.GameId);
                    }

                    if (game.CategoryIds == null ||
                        !game.CategoryIds.Contains(expected.CategoryId))
                    {
                        throw new InvalidOperationException(
                            "Expected native media category membership did not survive uninstall for " +
                            game.Name + ".");
                    }
                    categoryMembershipCount++;

                    var actions = game.GameActions == null
                        ? new List<GameAction>()
                        : game.GameActions
                            .Where(action =>
                                !action.IsPlayAction &&
                                string.Equals(
                                    action.Name,
                                    expected.ActionName,
                                    StringComparison.Ordinal) &&
                                string.Equals(
                                    Path.GetFileName(action.Path),
                                    expected.ActionPathName,
                                    StringComparison.Ordinal))
                            .ToList();

                    if (actions.Count != 1)
                    {
                        throw new InvalidOperationException(
                            "Expected native non-play media action did not survive uninstall for " +
                            game.Name + ".");
                    }
                    actionCount++;
                }

                var presets = new[]
                {
                    new
                    {
                        Id = EnrichmentFilterPresetBooks,
                        Name = EnrichmentFilterPresetBooksName,
                        CategoryId = EnrichmentCategoryBook
                    },
                    new
                    {
                        Id = EnrichmentFilterPresetComics,
                        Name = EnrichmentFilterPresetComicsName,
                        CategoryId = EnrichmentCategoryComic
                    },
                    new
                    {
                        Id = EnrichmentFilterPresetAudio,
                        Name = EnrichmentFilterPresetAudioName,
                        CategoryId = EnrichmentCategoryAudio
                    }
                };

                foreach (var expected in presets)
                {
                    var preset = PlayniteApi.Database.FilterPresets.Get(expected.Id);
                    var categoryIds =
                        preset == null ||
                        preset.Settings == null ||
                        preset.Settings.Category == null
                            ? null
                            : preset.Settings.Category.Ids;

                    if (preset == null ||
                        !string.Equals(
                            preset.Name,
                            expected.Name,
                            StringComparison.Ordinal) ||
                        categoryIds == null ||
                        categoryIds.Count != 1 ||
                        categoryIds[0] != expected.CategoryId)
                    {
                        throw new InvalidOperationException(
                            "Expected native media shelf did not survive uninstall: " +
                            expected.Name + ".");
                    }
                    filterPresetCount++;
                }

                foreach (var gameId in new[] { GameBook, GameComic })
                {
                    var game = PlayniteApi.Database.Games.Get(gameId);
                    if (game == null ||
                        string.IsNullOrWhiteSpace(game.CoverImage))
                    {
                        throw new InvalidOperationException(
                            "Expected native CoverImage did not survive uninstall for " +
                            gameId + ".");
                    }

                    var fullPath =
                        PlayniteApi.Database.GetFullFilePath(game.CoverImage);
                    if (string.IsNullOrWhiteSpace(fullPath) ||
                        !File.Exists(fullPath))
                    {
                        throw new InvalidOperationException(
                            "Native CoverImage database file is unavailable after uninstall for " +
                            gameId + ".");
                    }
                    coverCount++;
                }

                var mediaFiles = new[]
                {
                    Path.Combine(fixturePath, "rdte-book.pdf"),
                    Path.Combine(fixturePath, "rdte-book.epub"),
                    Path.Combine(fixturePath, "rdte-comic.cbz"),
                    Path.Combine(fixturePath, "rdte-soundtrack.flac")
                };
                localFilesPresent = mediaFiles.All(File.Exists);
                if (!localFilesPresent)
                {
                    throw new InvalidOperationException(
                        "Local media evidence was removed or changed by plugin uninstall.");
                }

                detail =
                    "Native categories, non-play actions, shelves, covers, and local media survived product uninstall.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            File.WriteAllText(
                Path.Combine(dataPath, "native-persistence-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-native-persistence-fixture/v1",
                        result = result,
                        category_memberships = categoryMembershipCount,
                        custom_actions = actionCount,
                        filter_presets = filterPresetCount,
                        cover_images = coverCount,
                        local_files_present = localFilesPresent,
                        detail = detail
                    },
                    true));
        }

        private void RunUserCategoryOverrideFixture(
            string dataPath,
            string fixtureProfile)
        {
            var remove = string.Equals(
                fixtureProfile,
                "r4i-user-category-override-remove-v1",
                StringComparison.OrdinalIgnoreCase);
            var restore = string.Equals(
                fixtureProfile,
                "r4i-user-category-override-restore-v1",
                StringComparison.OrdinalIgnoreCase);
            var mode = remove ? "remove" : restore ? "restore" : "verify";

            string result = "PASS";
            string detail = string.Empty;
            bool bookMembershipPresent = false;
            int otherManagedMemberships = 0;

            try
            {
                var category = PlayniteApi.Database.Categories.Get(EnrichmentCategoryBook);
                var game = PlayniteApi.Database.Games.Get(GameBook);
                if (category == null ||
                    !string.Equals(category.Name, EnrichmentCategoryBookName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Expected managed Book category is unavailable or renamed.");
                }
                if (game == null)
                {
                    throw new InvalidOperationException("Humble Ebook fixture is unavailable.");
                }

                var categories = game.CategoryIds == null
                    ? new List<Guid>()
                    : new List<Guid>(game.CategoryIds);

                if (remove)
                {
                    if (!categories.Contains(EnrichmentCategoryBook))
                    {
                        throw new InvalidOperationException(
                            "Managed Book membership is absent before user-override injection.");
                    }
                    categories.Remove(EnrichmentCategoryBook);
                    game.CategoryIds = categories;
                    PlayniteApi.Database.Games.Update(game);
                }
                else if (restore && !categories.Contains(EnrichmentCategoryBook))
                {
                    categories.Add(EnrichmentCategoryBook);
                    game.CategoryIds = categories;
                    PlayniteApi.Database.Games.Update(game);
                }

                var verifiedBook = PlayniteApi.Database.Games.Get(GameBook);
                bookMembershipPresent =
                    verifiedBook != null &&
                    verifiedBook.CategoryIds != null &&
                    verifiedBook.CategoryIds.Contains(EnrichmentCategoryBook);

                var comic = PlayniteApi.Database.Games.Get(GameComic);
                var audio = PlayniteApi.Database.Games.Get(GameAudio);
                var manualMedia = PlayniteApi.Database.Games.Get(GameManualMedia);
                if (comic != null && comic.CategoryIds != null &&
                    comic.CategoryIds.Contains(EnrichmentCategoryComic)) otherManagedMemberships++;
                if (audio != null && audio.CategoryIds != null &&
                    audio.CategoryIds.Contains(EnrichmentCategoryAudio)) otherManagedMemberships++;
                if (manualMedia != null && manualMedia.CategoryIds != null &&
                    manualMedia.CategoryIds.Contains(EnrichmentCategoryBook)) otherManagedMemberships++;

                var expectedBookMembership = restore;
                if (bookMembershipPresent != expectedBookMembership)
                {
                    throw new InvalidOperationException(
                        restore
                            ? "Managed Book membership was not restored for fixture cleanup."
                            : "User-removed Book membership was reasserted.");
                }
                if (otherManagedMemberships != 3)
                {
                    throw new InvalidOperationException(
                        "Unrelated managed category memberships changed during user-override rep.");
                }

                detail = remove
                    ? "Managed Book membership removed through Playnite SDK."
                    : restore
                        ? "Managed Book membership restored for fixture cleanup."
                        : "User-removed Book membership remains absent.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            File.WriteAllText(
                Path.Combine(dataPath, "user-category-override-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-user-category-override-fixture/v1",
                        mode = mode,
                        result = result,
                        book_game_id = GameBook.ToString(),
                        category_id = EnrichmentCategoryBook.ToString(),
                        category_name = EnrichmentCategoryBookName,
                        book_membership_present = bookMembershipPresent,
                        other_managed_memberships = otherManagedMemberships,
                        detail = detail
                    },
                    true));
        }

        private void RunUserActionOverrideFixture(
            string dataPath,
            string fixtureProfile)
        {
            var remove = string.Equals(
                fixtureProfile,
                "r4i-user-action-override-remove-v1",
                StringComparison.OrdinalIgnoreCase);
            var restore = string.Equals(
                fixtureProfile,
                "r4i-user-action-override-restore-v1",
                StringComparison.OrdinalIgnoreCase);
            var mode = remove ? "remove" : restore ? "restore" : "verify";

            string result = "PASS";
            string detail = string.Empty;
            bool bookActionPresent = false;
            int otherManagedActions = 0;

            try
            {
                var game = PlayniteApi.Database.Games.Get(GameBook);
                if (game == null)
                {
                    throw new InvalidOperationException("Humble Ebook fixture is unavailable.");
                }

                var evidencePath = game.Manual ?? string.Empty;
                var workingDir = Path.GetDirectoryName(evidencePath) ?? string.Empty;
                Func<GameAction, bool> matchesManagedRead = action =>
                    action != null &&
                    action.Type == GameActionType.File &&
                    !action.IsPlayAction &&
                    string.Equals(action.Name, "Read", StringComparison.Ordinal) &&
                    string.Equals(action.Path, evidencePath, StringComparison.Ordinal) &&
                    string.Equals(action.WorkingDir ?? string.Empty, workingDir, StringComparison.Ordinal) &&
                    string.IsNullOrEmpty(action.Arguments);

                var actions = game.GameActions == null
                    ? new List<GameAction>()
                    : new List<GameAction>(game.GameActions);
                var managedReads = actions.Where(matchesManagedRead).ToList();

                if (remove)
                {
                    if (managedReads.Count != 1)
                    {
                        throw new InvalidOperationException(
                            "Expected exactly one managed Ebook Read action before user-override injection.");
                    }

                    actions.Remove(managedReads[0]);
                    game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(actions);
                    PlayniteApi.Database.Games.Update(game);
                }
                else if (restore && managedReads.Count == 0)
                {
                    actions.Add(new GameAction
                    {
                        Type = GameActionType.File,
                        Name = "Read",
                        Path = evidencePath,
                        WorkingDir = workingDir,
                        Arguments = string.Empty,
                        IsPlayAction = false,
                        TrackingMode = TrackingMode.Default
                    });
                    game.GameActions = new System.Collections.ObjectModel.ObservableCollection<GameAction>(actions);
                    PlayniteApi.Database.Games.Update(game);
                }

                var verifiedBook = PlayniteApi.Database.Games.Get(GameBook);
                bookActionPresent =
                    verifiedBook != null &&
                    verifiedBook.GameActions != null &&
                    verifiedBook.GameActions.Any(matchesManagedRead);

                var comic = PlayniteApi.Database.Games.Get(GameComic);
                var audio = PlayniteApi.Database.Games.Get(GameAudio);
                var manualMedia = PlayniteApi.Database.Games.Get(GameManualMedia);

                if (HasManagedAction(comic, "Read")) otherManagedActions++;
                if (HasManagedAction(audio, "Listen")) otherManagedActions++;
                if (HasManagedAction(manualMedia, "Read")) otherManagedActions++;

                var expectedBookAction = restore;
                if (bookActionPresent != expectedBookAction)
                {
                    throw new InvalidOperationException(
                        restore
                            ? "Managed Ebook Read action was not restored for fixture cleanup."
                            : "User-removed Ebook Read action was reasserted.");
                }
                if (otherManagedActions != 3)
                {
                    throw new InvalidOperationException(
                        "Unrelated managed custom actions changed during user-override rep.");
                }

                detail = remove
                    ? "Managed Ebook Read action removed through Playnite SDK."
                    : restore
                        ? "Managed Ebook Read action restored for fixture cleanup."
                        : "User-removed Ebook Read action remains absent.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            File.WriteAllText(
                Path.Combine(dataPath, "user-action-override-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-user-action-override-fixture/v1",
                        mode = mode,
                        result = result,
                        book_game_id = GameBook.ToString(),
                        action_name = "Read",
                        book_action_present = bookActionPresent,
                        other_managed_actions = otherManagedActions,
                        detail = detail
                    },
                    true));
        }

        private static bool HasManagedAction(Game game, string actionName)
        {
            if (game == null || game.GameActions == null)
            {
                return false;
            }

            return game.GameActions.Any(action =>
                action != null &&
                action.Type == GameActionType.File &&
                !action.IsPlayAction &&
                string.Equals(action.Name, actionName, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(action.Path) &&
                string.IsNullOrEmpty(action.Arguments));
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


        private void RunCoverConflictFixture(
            string dataPath,
            string fixturePath,
            string fixtureProfile)
        {
            var apply = string.Equals(
                fixtureProfile,
                "r4i-cover-conflict-apply-v1",
                StringComparison.OrdinalIgnoreCase);
            var externalPath = Path.Combine(fixturePath, "external-book-cover.png");
            var statePath = Path.Combine(dataPath, "cover-conflict-state.txt");
            EnsureBytes(
                externalPath,
                Convert.FromBase64String(
                    "iVBORw0KGgoAAAANSUhEUgAAACAAAAAwCAIAAAD/zu84AAAAMklEQVR4nO3NMQEAMAjAsDFxiEAdUjEBXyqgiex6l/3TOwAAAAAAAAAAAAAAAAAAYLkBNiIBoDCEHzMAAAAASUVORK5CYII="));

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

                if (apply)
                {
                    if (string.IsNullOrWhiteSpace(game.CoverImage))
                    {
                        throw new InvalidOperationException(
                            "Expected plugin-projected Book CoverImage before external mutation.");
                    }

                    var externalDatabasePath =
                        PlayniteApi.Database.AddFile(externalPath, game.Id);
                    game.CoverImage = externalDatabasePath;
                    PlayniteApi.Database.Games.Update(game);
                    File.WriteAllText(statePath, externalDatabasePath);
                }

                if (!File.Exists(statePath))
                {
                    throw new InvalidOperationException(
                        "External cover state receipt is unavailable.");
                }

                var expectedDatabasePath = File.ReadAllText(statePath).Trim();
                var verified = PlayniteApi.Database.Games.Get(GameBook);
                var preserved =
                    verified != null &&
                    string.Equals(
                        verified.CoverImage,
                        expectedDatabasePath,
                        StringComparison.Ordinal);

                if (!preserved)
                {
                    throw new InvalidOperationException(
                        "Externally changed Book CoverImage is not present.");
                }

                var fullPath =
                    PlayniteApi.Database.GetFullFilePath(expectedDatabasePath);
                if (string.IsNullOrWhiteSpace(fullPath) ||
                    !File.Exists(fullPath))
                {
                    throw new InvalidOperationException(
                        "Externally changed Book cover database file is unavailable.");
                }

                detail = apply
                    ? "External Book CoverImage mutation applied and verified."
                    : "External Book CoverImage mutation remains present.";
            }
            catch (Exception exception)
            {
                result = "FAIL";
                detail = exception.Message;
            }

            var current = PlayniteApi.Database.Games.Get(GameBook);
            File.WriteAllText(
                Path.Combine(dataPath, "cover-conflict-receipt.json"),
                Serialization.ToJson(
                    new
                    {
                        schema = "sempersupra-playnite-r4i-cover-conflict-fixture/v1",
                        mode = apply ? "apply" : "verify",
                        result = result,
                        book_game_id = GameBook.ToString(),
                        cover_image_present =
                            current != null &&
                            !string.IsNullOrWhiteSpace(current.CoverImage),
                        external_source_name = Path.GetFileName(externalPath),
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

        private static void EnsureBytes(string path, byte[] content)
        {
            if (!File.Exists(path) ||
                !File.ReadAllBytes(path).SequenceEqual(content))
            {
                File.WriteAllBytes(path, content);
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
