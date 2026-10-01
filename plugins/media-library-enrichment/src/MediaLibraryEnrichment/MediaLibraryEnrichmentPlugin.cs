using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public sealed class MediaLibraryEnrichmentPlugin : GenericPlugin
    {
        public static readonly Guid PluginGuid =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377");

        private static readonly Guid CategoryBook =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1401");
        private static readonly Guid CategoryComic =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1402");
        private static readonly Guid CategoryAudio =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1403");

        public override Guid Id { get; } = PluginGuid;

        public MediaLibraryEnrichmentPlugin(IPlayniteAPI api)
            : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = false
            };
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            var candidates = ObserveCandidates();
            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);

            File.WriteAllText(
                Path.Combine(dataPath, "observation-receipt.json"),
                Serialization.ToJson(new MediaObservationReceipt
                {
                    Schema = "sempersupra-media-library-enrichment-observation/v1",
                    FixtureContract = "media-baseline-v1",
                    CandidateCount = candidates.Length,
                    Candidates = candidates
                }, true));

            var settingsPath = Path.Combine(dataPath, "settings.json");
            var ledgerPath = Path.Combine(dataPath, "category-ledger.json");
            var receiptPath = Path.Combine(dataPath, "category-receipt.json");

            var settings = MediaLibraryEnrichmentSettings.Load(settingsPath);
            var ledger = LoadLedger(ledgerPath);
            var receipt = ReconcileCategories(candidates, settings.ResolveMode(), ledger);

            File.WriteAllText(receiptPath, Serialization.ToJson(receipt, true));
            File.WriteAllText(ledgerPath, Serialization.ToJson(ledger, true));
        }

        private MediaObservation[] ObserveCandidates()
        {
            return PlayniteApi.Database.Games
                .Where(game =>
                    MediaCandidateClassifier.ShouldObserve(
                        game.Source == null ? string.Empty : game.Source.Name,
                        !string.IsNullOrWhiteSpace(game.CoverImage)))
                .Select(game => new MediaObservation
                {
                    PlayniteId = game.Id.ToString(),
                    ProviderGameId = game.GameId ?? string.Empty,
                    Name = game.Name ?? string.Empty,
                    Source = game.Source == null ? string.Empty : game.Source.Name ?? string.Empty,
                    Kind = MediaCandidateClassifier.ClassifyKind(game.Manual, game.Notes),
                    ManualPath = game.Manual ?? string.Empty,
                    CoverMissing = string.IsNullOrWhiteSpace(game.CoverImage)
                })
                .OrderBy(item => item.PlayniteId, StringComparer.Ordinal)
                .ToArray();
        }

        private CategoryProjectionReceipt ReconcileCategories(
            MediaObservation[] candidates,
            ProjectionMode mode,
            CategoryOwnershipLedger ledger)
        {
            var operations = new List<string>();
            var applied = 0;
            var conflicts = 0;

            foreach (var candidate in candidates)
            {
                Guid categoryId;
                string categoryName;
                if (!TryResolveCategory(candidate.Kind, out categoryId, out categoryName))
                {
                    continue;
                }

                var gameId = Guid.Parse(candidate.PlayniteId);
                var game = PlayniteApi.Database.Games.Get(gameId);
                if (game == null)
                {
                    continue;
                }

                var category = PlayniteApi.Database.Categories.Get(categoryId);
                var categoryIdentityMatches =
                    category == null ||
                    string.Equals(category.Name, categoryName, StringComparison.Ordinal);

                var categoryPresent =
                    game.CategoryIds != null &&
                    game.CategoryIds.Contains(categoryId);

                var ownedRecord = ledger.Records.FirstOrDefault(record =>
                    string.Equals(record.PlayniteId, candidate.PlayniteId, StringComparison.Ordinal) &&
                    string.Equals(record.CategoryId, categoryId.ToString(), StringComparison.OrdinalIgnoreCase));

                var operation = CategoryProjectionPlanner.Plan(
                    mode,
                    categoryPresent,
                    ownedRecord != null,
                    categoryIdentityMatches);

                if (mode == ProjectionMode.Rollback &&
                    ownedRecord != null &&
                    !categoryIdentityMatches)
                {
                    conflicts++;
                    operations.Add(candidate.Name + ":CONFLICT");
                    continue;
                }

                if (operation == CategoryProjectionOperation.Add)
                {
                    if (category == null)
                    {
                        PlayniteApi.Database.Categories.Add(new Category
                        {
                            Id = categoryId,
                            Name = categoryName
                        });
                    }

                    if (game.CategoryIds == null)
                    {
                        game.CategoryIds = new List<Guid>();
                    }

                    game.CategoryIds.Add(categoryId);
                    PlayniteApi.Database.Games.Update(game);

                    ledger.Records.Add(new CategoryOwnershipRecord
                    {
                        PlayniteId = candidate.PlayniteId,
                        CategoryId = categoryId.ToString(),
                        CategoryName = categoryName
                    });

                    applied++;
                    operations.Add(candidate.Name + ":ADD:" + categoryName);
                }
                else if (operation == CategoryProjectionOperation.Remove)
                {
                    game.CategoryIds.Remove(categoryId);
                    PlayniteApi.Database.Games.Update(game);
                    ledger.Records.Remove(ownedRecord);

                    applied++;
                    operations.Add(candidate.Name + ":REMOVE:" + categoryName);
                }
            }

            return new CategoryProjectionReceipt
            {
                Mode = mode.ToString().ToLowerInvariant(),
                CandidateCount = candidates.Length,
                PlannedOperationCount = operations.Count,
                AppliedOperationCount = applied,
                ConflictCount = conflicts,
                Operations = operations.ToArray()
            };
        }

        private static CategoryOwnershipLedger LoadLedger(string path)
        {
            if (!File.Exists(path))
            {
                return new CategoryOwnershipLedger();
            }

            return Serialization.FromJson<CategoryOwnershipLedger>(
                File.ReadAllText(path)) ?? new CategoryOwnershipLedger();
        }

        private static bool TryResolveCategory(
            string kind,
            out Guid categoryId,
            out string categoryName)
        {
            switch (kind)
            {
                case "book":
                    categoryId = CategoryBook;
                    categoryName = "SemperSupra.Media:Book";
                    return true;
                case "comic":
                    categoryId = CategoryComic;
                    categoryName = "SemperSupra.Media:Comic";
                    return true;
                case "audio":
                    categoryId = CategoryAudio;
                    categoryName = "SemperSupra.Media:Audio";
                    return true;
                default:
                    categoryId = Guid.Empty;
                    categoryName = null;
                    return false;
            }
        }
    }
}
