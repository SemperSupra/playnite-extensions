using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MediaLibraryEnrichment
{
    public sealed class MediaLibraryEnrichmentPlugin : GenericPlugin
    {
        public static readonly Guid PluginGuid =
            Guid.Parse("4d1dfe5e-5df3-4a8d-bc0b-b6c2f9ab1377");

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
            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);

            var candidates = CaptureCandidates();
            File.WriteAllText(
                Path.Combine(dataPath, "observation-receipt.json"),
                Serialization.ToJson(
                    new MediaObservationReceipt
                    {
                        Schema = "sempersupra-media-library-enrichment-observation/v1",
                        FixtureContract = "media-raw-v1",
                        CandidateCount = candidates.Length,
                        Candidates = candidates
                    },
                    true));

            var settingsPath = Path.Combine(dataPath, "settings.json");
            var ledgerPath = Path.Combine(dataPath, "category-ledger.json");
            var reconcilePath = Path.Combine(dataPath, "r4i-receipt.json");
            var actionLedgerPath = Path.Combine(dataPath, "action-ledger.json");
            var actionReconcilePath = Path.Combine(dataPath, "action-r4i-receipt.json");

            var settings = MediaLibraryEnrichmentSettings.LoadOrCreate(settingsPath);
            var ledger = CategoryLedger.LoadOrCreate(ledgerPath);
            var actionLedger = ActionLedger.LoadOrCreate(actionLedgerPath);

            CategoryReconcileReceipt receipt;
            if (string.Equals(settings.Mode, "apply", StringComparison.Ordinal))
            {
                receipt = ApplyCategoryEnrichment(candidates, ledger, ledgerPath);
            }
            else if (string.Equals(settings.Mode, "rollback", StringComparison.Ordinal))
            {
                receipt = RollbackCategoryEnrichment(ledger, ledgerPath);
            }
            else
            {
                receipt = new CategoryReconcileReceipt
                {
                    Mode = "observe",
                    CandidateCount = candidates.Length,
                    PlanSha256 = HashPlan(new CategoryOperationReceipt[0])
                };
            }

            File.WriteAllText(
                reconcilePath,
                Serialization.ToJson(receipt, true));

            ActionReconcileReceipt actionReceipt;
            if (string.Equals(settings.Mode, "apply", StringComparison.Ordinal))
            {
                actionReceipt = ApplyActionEnrichment(
                    candidates,
                    actionLedger,
                    actionLedgerPath);
            }
            else if (string.Equals(settings.Mode, "rollback", StringComparison.Ordinal))
            {
                actionReceipt = RollbackActionEnrichment(
                    actionLedger,
                    actionLedgerPath);
            }
            else
            {
                actionReceipt = new ActionReconcileReceipt
                {
                    Mode = "observe",
                    CandidateCount = candidates.Length,
                    PlanSha256 = HashActionPlan(new ActionOperationReceipt[0])
                };
            }

            File.WriteAllText(
                actionReconcilePath,
                Serialization.ToJson(actionReceipt, true));
        }

        private MediaObservation[] CaptureCandidates()
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
                    LocalEvidenceName =
                        MediaCandidateClassifier.NormalizeLocalEvidenceName(game.Manual),
                    CoverMissing = string.IsNullOrWhiteSpace(game.CoverImage)
                })
                .OrderBy(item => item.PlayniteId, StringComparer.Ordinal)
                .ToArray();
        }

        private CategoryReconcileReceipt ApplyCategoryEnrichment(
            MediaObservation[] candidates,
            CategoryLedger ledger,
            string ledgerPath)
        {
            var operations = BuildApplyPlan(candidates);
            var receipt = new CategoryReconcileReceipt
            {
                Mode = "apply",
                CandidateCount = candidates.Length,
                PlanSha256 = HashPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                if (string.Equals(operation.Outcome, "NOOP", StringComparison.Ordinal))
                {
                    receipt.NoopCount++;
                    CommitRecoveredLedgerIfNeeded(operation, ledger, ledgerPath);
                    continue;
                }

                if (operation.Outcome.StartsWith("CONFLICT", StringComparison.Ordinal))
                {
                    receipt.ConflictCount++;
                    continue;
                }

                if (!string.Equals(operation.Outcome, "ADD_MEMBERSHIP", StringComparison.Ordinal))
                {
                    continue;
                }

                var gameId = Guid.Parse(operation.PlayniteId);
                var categoryId = Guid.Parse(operation.CategoryId);
                var currentGame = PlayniteApi.Database.Games.Get(gameId);
                if (currentGame == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Game disappeared before apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var categories = currentGame.CategoryIds == null
                    ? new List<Guid>()
                    : new List<Guid>(currentGame.CategoryIds);

                if (categories.Contains(categoryId))
                {
                    operation.Outcome = "PRECONDITION_CHANGED_NOOP";
                    operation.Detail = "Desired membership appeared before apply.";
                    receipt.NoopCount++;
                    CommitRecoveredLedgerIfNeeded(operation, ledger, ledgerPath);
                    continue;
                }

                var category = PlayniteApi.Database.Categories.Get(categoryId);
                var conflictingCategory = PlayniteApi.Database.Categories
                    .FirstOrDefault(item =>
                        item.Id != categoryId &&
                        string.Equals(
                            item.Name,
                            operation.CategoryName,
                            StringComparison.Ordinal));

                if (conflictingCategory != null ||
                    (category != null &&
                     !string.Equals(
                         category.Name,
                         operation.CategoryName,
                         StringComparison.Ordinal)))
                {
                    operation.Outcome = "CONFLICT_CATEGORY_PRECONDITION";
                    operation.Detail = "Category identity/name precondition changed before apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var ledgerEntry = FindLedgerEntry(ledger, operation.PlayniteId, operation.CategoryId);
                if (ledgerEntry == null)
                {
                    ledgerEntry = new CategoryLedgerEntry
                    {
                        PlayniteId = operation.PlayniteId,
                        Kind = operation.Kind,
                        CategoryId = operation.CategoryId,
                        CategoryName = operation.CategoryName,
                        PriorMembership = false,
                        CategoryCreated = category == null,
                        Status = "PLANNED"
                    };
                    ledger.Entries.Add(ledgerEntry);
                }
                else
                {
                    ledgerEntry.Status = "PLANNED";
                }

                ledger.Save(ledgerPath);

                using (PlayniteApi.Database.BufferedUpdate())
                {
                    if (category == null)
                    {
                        PlayniteApi.Database.Categories.Add(
                            new Category
                            {
                                Id = categoryId,
                                Name = operation.CategoryName
                            });
                    }

                    categories.Add(categoryId);
                    currentGame.CategoryIds = categories;
                    PlayniteApi.Database.Games.Update(currentGame);
                }

                var verifiedGame = PlayniteApi.Database.Games.Get(gameId);
                if (verifiedGame == null ||
                    verifiedGame.CategoryIds == null ||
                    !verifiedGame.CategoryIds.Contains(categoryId))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Category membership was not present after apply.";
                    throw new InvalidOperationException(operation.Detail);
                }

                ledgerEntry.Status = "COMMITTED";
                ledger.Save(ledgerPath);

                operation.Outcome = "APPLIED";
                operation.Detail = "Category membership applied and verified.";
                receipt.AppliedCount++;
            }

            return receipt;
        }

        private List<CategoryOperationReceipt> BuildApplyPlan(MediaObservation[] candidates)
        {
            var operations = new List<CategoryOperationReceipt>();

            foreach (var candidate in candidates)
            {
                var spec = CategoryEnrichmentPolicy.ForKind(candidate.Kind);
                if (spec == null)
                {
                    operations.Add(
                        new CategoryOperationReceipt
                        {
                            PlayniteId = candidate.PlayniteId,
                            Name = candidate.Name,
                            Kind = candidate.Kind,
                            Outcome = "SKIP_UNRESOLVED",
                            Detail = "No owned category mapping exists for this media kind."
                        });
                    continue;
                }

                var operation = new CategoryOperationReceipt
                {
                    PlayniteId = candidate.PlayniteId,
                    Name = candidate.Name,
                    Kind = candidate.Kind,
                    CategoryId = spec.CategoryId.ToString(),
                    CategoryName = spec.CategoryName
                };

                var gameId = Guid.Parse(candidate.PlayniteId);
                var game = PlayniteApi.Database.Games.Get(gameId);
                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Game does not exist at plan time.";
                    operations.Add(operation);
                    continue;
                }

                var category = PlayniteApi.Database.Categories.Get(spec.CategoryId);
                var conflictingCategory = PlayniteApi.Database.Categories
                    .FirstOrDefault(item =>
                        item.Id != spec.CategoryId &&
                        string.Equals(item.Name, spec.CategoryName, StringComparison.Ordinal));

                if (conflictingCategory != null ||
                    (category != null &&
                     !string.Equals(category.Name, spec.CategoryName, StringComparison.Ordinal)))
                {
                    operation.Outcome = "CONFLICT_CATEGORY_IDENTITY";
                    operation.Detail = "Desired category name or ID is already owned by different state.";
                    operations.Add(operation);
                    continue;
                }

                var membershipPresent =
                    game.CategoryIds != null &&
                    game.CategoryIds.Contains(spec.CategoryId);

                operation.Outcome = membershipPresent
                    ? "NOOP"
                    : "ADD_MEMBERSHIP";
                operation.Detail = membershipPresent
                    ? "Desired membership already present."
                    : "Missing owned category membership.";
                operations.Add(operation);
            }

            return operations
                .OrderBy(item => item.PlayniteId, StringComparer.Ordinal)
                .ThenBy(item => item.CategoryId ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private CategoryReconcileReceipt RollbackCategoryEnrichment(
            CategoryLedger ledger,
            string ledgerPath)
        {
            var committed = ledger.Entries
                .Where(entry =>
                    string.Equals(entry.Status, "COMMITTED", StringComparison.Ordinal) ||
                    string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
                .OrderBy(entry => entry.PlayniteId, StringComparer.Ordinal)
                .ThenBy(entry => entry.CategoryId, StringComparer.Ordinal)
                .ToList();

            var operations = committed
                .Select(entry => new CategoryOperationReceipt
                {
                    PlayniteId = entry.PlayniteId,
                    Kind = entry.Kind,
                    CategoryId = entry.CategoryId,
                    CategoryName = entry.CategoryName,
                    Outcome = "ROLLBACK_MEMBERSHIP",
                    Detail = "Remove only the membership recorded by the ownership ledger."
                })
                .ToList();

            var receipt = new CategoryReconcileReceipt
            {
                Mode = "rollback",
                CandidateCount = committed.Count,
                PlanSha256 = HashPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                var entry = FindLedgerEntry(ledger, operation.PlayniteId, operation.CategoryId);
                var gameId = Guid.Parse(operation.PlayniteId);
                var categoryId = Guid.Parse(operation.CategoryId);
                var game = PlayniteApi.Database.Games.Get(gameId);

                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Cannot rollback membership because the game no longer exists.";
                    receipt.ConflictCount++;
                    continue;
                }

                var categories = game.CategoryIds == null
                    ? new List<Guid>()
                    : new List<Guid>(game.CategoryIds);

                if (!categories.Contains(categoryId))
                {
                    operation.Outcome = "ROLLBACK_NOOP";
                    operation.Detail = "Owned membership is already absent.";
                    receipt.NoopCount++;
                    if (entry != null)
                    {
                        entry.Status = "ROLLED_BACK";
                    }
                    continue;
                }

                if (entry == null || entry.PriorMembership)
                {
                    operation.Outcome = "CONFLICT_OWNERSHIP";
                    operation.Detail = "Ledger does not authorize removal of this membership.";
                    receipt.ConflictCount++;
                    continue;
                }

                categories.Remove(categoryId);
                game.CategoryIds = categories;
                PlayniteApi.Database.Games.Update(game);

                var verifiedGame = PlayniteApi.Database.Games.Get(gameId);
                if (verifiedGame != null &&
                    verifiedGame.CategoryIds != null &&
                    verifiedGame.CategoryIds.Contains(categoryId))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Category membership remained after rollback.";
                    throw new InvalidOperationException(operation.Detail);
                }

                entry.Status = "ROLLED_BACK";
                operation.Outcome = "ROLLBACK_APPLIED";
                operation.Detail = "Owned membership removed and verified.";
                receipt.RollbackAppliedCount++;
            }

            var createdCategories = ledger.Entries
                .Where(entry => entry.CategoryCreated)
                .GroupBy(entry => entry.CategoryId, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();

            foreach (var entry in createdCategories)
            {
                var categoryId = Guid.Parse(entry.CategoryId);
                var category = PlayniteApi.Database.Categories.Get(categoryId);
                if (category == null)
                {
                    continue;
                }

                if (!string.Equals(category.Name, entry.CategoryName, StringComparison.Ordinal))
                {
                    receipt.ConflictCount++;
                    receipt.Operations.Add(
                        new CategoryOperationReceipt
                        {
                            CategoryId = entry.CategoryId,
                            CategoryName = entry.CategoryName,
                            Outcome = "CONFLICT_CATEGORY_CHANGED",
                            Detail = "Category name changed externally; object retained."
                        });
                    continue;
                }

                var inUse = PlayniteApi.Database.Games.Any(game =>
                    game.CategoryIds != null &&
                    game.CategoryIds.Contains(categoryId));

                if (inUse)
                {
                    receipt.ConflictCount++;
                    receipt.Operations.Add(
                        new CategoryOperationReceipt
                        {
                            CategoryId = entry.CategoryId,
                            CategoryName = entry.CategoryName,
                            Outcome = "CONFLICT_CATEGORY_IN_USE",
                            Detail = "Category is referenced by other state; object retained."
                        });
                    continue;
                }

                var removed = PlayniteApi.Database.Categories.Remove(categoryId);
                var verifiedCategory = PlayniteApi.Database.Categories.Get(categoryId);
                if (!removed || verifiedCategory != null)
                {
                    throw new InvalidOperationException(
                        "Plugin-created category object remained after rollback.");
                }

                receipt.Operations.Add(
                    new CategoryOperationReceipt
                    {
                        CategoryId = entry.CategoryId,
                        CategoryName = entry.CategoryName,
                        Outcome = "CATEGORY_REMOVED",
                        Detail = "Plugin-created category object removed and verified after memberships cleared."
                    });
            }

            ledger.Save(ledgerPath);
            return receipt;
        }

        private void CommitRecoveredLedgerIfNeeded(
            CategoryOperationReceipt operation,
            CategoryLedger ledger,
            string ledgerPath)
        {
            if (string.IsNullOrWhiteSpace(operation.CategoryId))
            {
                return;
            }

            var entry = FindLedgerEntry(ledger, operation.PlayniteId, operation.CategoryId);
            if (entry != null &&
                string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
            {
                entry.Status = "COMMITTED";
                ledger.Save(ledgerPath);
            }
        }

        private static CategoryLedgerEntry FindLedgerEntry(
            CategoryLedger ledger,
            string playniteId,
            string categoryId)
        {
            return ledger.Entries.FirstOrDefault(entry =>
                string.Equals(entry.PlayniteId, playniteId, StringComparison.Ordinal) &&
                string.Equals(entry.CategoryId, categoryId, StringComparison.Ordinal));
        }

        private ActionReconcileReceipt ApplyActionEnrichment(
            MediaObservation[] candidates,
            ActionLedger ledger,
            string ledgerPath)
        {
            var operations = BuildActionApplyPlan(candidates);
            var receipt = new ActionReconcileReceipt
            {
                Mode = "apply",
                CandidateCount = candidates.Length,
                PlanSha256 = HashActionPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                if (string.Equals(operation.Outcome, "NOOP", StringComparison.Ordinal))
                {
                    receipt.NoopCount++;
                    CommitRecoveredActionLedgerIfNeeded(operation, ledger, ledgerPath);
                    continue;
                }

                if (operation.Outcome.StartsWith("CONFLICT", StringComparison.Ordinal))
                {
                    receipt.ConflictCount++;
                    continue;
                }

                if (!string.Equals(operation.Outcome, "ADD_ACTION", StringComparison.Ordinal))
                {
                    continue;
                }

                var game = PlayniteApi.Database.Games.Get(Guid.Parse(operation.PlayniteId));
                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Game disappeared before action apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var spec = ActionEnrichmentPolicy.ForKind(operation.Kind);
                var evidencePath = MediaCandidateClassifier.ResolveLocalEvidencePath(
                    game.Manual,
                    game.Notes);
                if (spec == null || string.IsNullOrWhiteSpace(evidencePath) || !File.Exists(evidencePath))
                {
                    operation.Outcome = "CONFLICT_EVIDENCE_CHANGED";
                    operation.Detail = "Local media evidence changed before action apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var actions = game.GameActions == null
                    ? new ObservableCollection<GameAction>()
                    : new ObservableCollection<GameAction>(game.GameActions);

                if (actions.Any(action => ActionMatchesDesired(action, spec, evidencePath)))
                {
                    operation.Outcome = "PRECONDITION_CHANGED_NOOP";
                    operation.Detail = "Desired action appeared before apply.";
                    receipt.NoopCount++;
                    CommitRecoveredActionLedgerIfNeeded(operation, ledger, ledgerPath);
                    continue;
                }

                if (actions.Any(action =>
                    string.Equals(action.Name, spec.ActionName, StringComparison.Ordinal)))
                {
                    operation.Outcome = "CONFLICT_ACTION_NAME";
                    operation.Detail = "Same-name external action appeared before apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var workingDir = Path.GetDirectoryName(evidencePath) ?? string.Empty;
                var ledgerEntry = FindActionLedgerEntry(
                    ledger,
                    operation.PlayniteId,
                    operation.SemanticKey);

                if (ledgerEntry == null)
                {
                    ledgerEntry = new ActionLedgerEntry
                    {
                        PlayniteId = operation.PlayniteId,
                        Kind = operation.Kind,
                        SemanticKey = operation.SemanticKey,
                        ActionName = spec.ActionName,
                        Path = evidencePath,
                        WorkingDir = workingDir,
                        LocalEvidenceName = operation.LocalEvidenceName,
                        Status = "PLANNED"
                    };
                    ledger.Entries.Add(ledgerEntry);
                }
                else
                {
                    ledgerEntry.Path = evidencePath;
                    ledgerEntry.WorkingDir = workingDir;
                    ledgerEntry.Status = "PLANNED";
                }

                ledger.Save(ledgerPath);

                actions.Add(new GameAction
                {
                    Type = GameActionType.File,
                    Name = spec.ActionName,
                    Path = evidencePath,
                    WorkingDir = workingDir,
                    Arguments = string.Empty,
                    IsPlayAction = false,
                    TrackingMode = TrackingMode.Default
                });

                game.GameActions = actions;
                PlayniteApi.Database.Games.Update(game);

                var verified = PlayniteApi.Database.Games.Get(game.Id);
                if (verified == null ||
                    verified.GameActions == null ||
                    !verified.GameActions.Any(action =>
                        ActionMatchesDesired(action, spec, evidencePath)))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Custom media action was not present after apply.";
                    throw new InvalidOperationException(operation.Detail);
                }

                ledgerEntry.Status = "COMMITTED";
                ledger.Save(ledgerPath);

                operation.Outcome = "APPLIED";
                operation.Detail = "Custom media action applied and verified.";
                receipt.AppliedCount++;
            }

            return receipt;
        }

        private List<ActionOperationReceipt> BuildActionApplyPlan(
            MediaObservation[] candidates)
        {
            var operations = new List<ActionOperationReceipt>();

            foreach (var candidate in candidates)
            {
                var spec = ActionEnrichmentPolicy.ForKind(candidate.Kind);
                if (spec == null)
                {
                    continue;
                }

                var operation = new ActionOperationReceipt
                {
                    PlayniteId = candidate.PlayniteId,
                    Name = candidate.Name,
                    Kind = candidate.Kind,
                    SemanticKey = spec.SemanticKey,
                    ActionName = spec.ActionName,
                    LocalEvidenceName = candidate.LocalEvidenceName,
                    IsPlayAction = false
                };

                var game = PlayniteApi.Database.Games.Get(Guid.Parse(candidate.PlayniteId));
                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Game does not exist at plan time.";
                    operations.Add(operation);
                    continue;
                }

                var evidencePath = MediaCandidateClassifier.ResolveLocalEvidencePath(
                    game.Manual,
                    game.Notes);
                if (string.IsNullOrWhiteSpace(evidencePath) || !File.Exists(evidencePath))
                {
                    operation.Outcome = "CONFLICT_EVIDENCE_MISSING";
                    operation.Detail = "No usable local media evidence exists.";
                    operations.Add(operation);
                    continue;
                }

                operation.LocalEvidenceName =
                    MediaCandidateClassifier.NormalizeLocalEvidenceName(evidencePath);

                var actions = game.GameActions ?? new ObservableCollection<GameAction>();
                if (actions.Any(action => ActionMatchesDesired(action, spec, evidencePath)))
                {
                    operation.Outcome = "NOOP";
                    operation.Detail = "Desired custom action already exists.";
                }
                else if (actions.Any(action =>
                    string.Equals(action.Name, spec.ActionName, StringComparison.Ordinal)))
                {
                    operation.Outcome = "CONFLICT_ACTION_NAME";
                    operation.Detail = "Same-name action exists with different state.";
                }
                else
                {
                    operation.Outcome = "ADD_ACTION";
                    operation.Detail = "Missing owned custom media action.";
                }

                operations.Add(operation);
            }

            return operations
                .OrderBy(item => item.PlayniteId, StringComparer.Ordinal)
                .ThenBy(item => item.SemanticKey ?? string.Empty, StringComparer.Ordinal)
                .ToList();
        }

        private ActionReconcileReceipt RollbackActionEnrichment(
            ActionLedger ledger,
            string ledgerPath)
        {
            var committed = ledger.Entries
                .Where(entry =>
                    string.Equals(entry.Status, "COMMITTED", StringComparison.Ordinal) ||
                    string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
                .OrderBy(entry => entry.PlayniteId, StringComparer.Ordinal)
                .ThenBy(entry => entry.SemanticKey, StringComparer.Ordinal)
                .ToList();

            var operations = committed
                .Select(entry => new ActionOperationReceipt
                {
                    PlayniteId = entry.PlayniteId,
                    Kind = entry.Kind,
                    SemanticKey = entry.SemanticKey,
                    ActionName = entry.ActionName,
                    LocalEvidenceName = entry.LocalEvidenceName,
                    IsPlayAction = false,
                    Outcome = "ROLLBACK_ACTION",
                    Detail = "Remove only the exact action recorded by the ownership ledger."
                })
                .ToList();

            var receipt = new ActionReconcileReceipt
            {
                Mode = "rollback",
                CandidateCount = committed.Count,
                PlanSha256 = HashActionPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                var entry = FindActionLedgerEntry(
                    ledger,
                    operation.PlayniteId,
                    operation.SemanticKey);
                var game = PlayniteApi.Database.Games.Get(Guid.Parse(operation.PlayniteId));
                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Cannot rollback action because the game no longer exists.";
                    receipt.ConflictCount++;
                    continue;
                }

                var actions = game.GameActions == null
                    ? new ObservableCollection<GameAction>()
                    : new ObservableCollection<GameAction>(game.GameActions);

                var exact = actions.FirstOrDefault(action =>
                    ActionMatchesLedger(action, entry));

                if (exact == null)
                {
                    var sameName = actions.Any(action =>
                        string.Equals(
                            action.Name,
                            entry == null ? operation.ActionName : entry.ActionName,
                            StringComparison.Ordinal));

                    if (sameName)
                    {
                        operation.Outcome = "CONFLICT_ACTION_CHANGED";
                        operation.Detail = "Owned action changed externally; preserved.";
                        receipt.ConflictCount++;
                        continue;
                    }

                    operation.Outcome = "ROLLBACK_NOOP";
                    operation.Detail = "Owned action is already absent.";
                    receipt.NoopCount++;
                    if (entry != null)
                    {
                        entry.Status = "ROLLED_BACK";
                    }
                    continue;
                }

                actions.Remove(exact);
                game.GameActions = actions;
                PlayniteApi.Database.Games.Update(game);

                var verified = PlayniteApi.Database.Games.Get(game.Id);
                if (verified != null &&
                    verified.GameActions != null &&
                    verified.GameActions.Any(action =>
                        ActionMatchesLedger(action, entry)))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Owned action remained after rollback.";
                    throw new InvalidOperationException(operation.Detail);
                }

                entry.Status = "ROLLED_BACK";
                operation.Outcome = "ROLLBACK_APPLIED";
                operation.Detail = "Owned custom action removed and verified.";
                receipt.RollbackAppliedCount++;
            }

            ledger.Save(ledgerPath);
            return receipt;
        }

        private static bool ActionMatchesDesired(
            GameAction action,
            ActionEnrichmentSpec spec,
            string evidencePath)
        {
            if (action == null || spec == null)
            {
                return false;
            }

            return action.Type == GameActionType.File &&
                !action.IsPlayAction &&
                string.Equals(action.Name, spec.ActionName, StringComparison.Ordinal) &&
                string.Equals(action.Path, evidencePath, StringComparison.Ordinal) &&
                string.Equals(
                    action.WorkingDir ?? string.Empty,
                    Path.GetDirectoryName(evidencePath) ?? string.Empty,
                    StringComparison.Ordinal) &&
                string.IsNullOrEmpty(action.Arguments);
        }

        private static bool ActionMatchesLedger(
            GameAction action,
            ActionLedgerEntry entry)
        {
            if (action == null || entry == null)
            {
                return false;
            }

            return action.Type == GameActionType.File &&
                !action.IsPlayAction &&
                string.Equals(action.Name, entry.ActionName, StringComparison.Ordinal) &&
                string.Equals(action.Path, entry.Path, StringComparison.Ordinal) &&
                string.Equals(
                    action.WorkingDir ?? string.Empty,
                    entry.WorkingDir ?? string.Empty,
                    StringComparison.Ordinal) &&
                string.IsNullOrEmpty(action.Arguments);
        }

        private void CommitRecoveredActionLedgerIfNeeded(
            ActionOperationReceipt operation,
            ActionLedger ledger,
            string ledgerPath)
        {
            var entry = FindActionLedgerEntry(
                ledger,
                operation.PlayniteId,
                operation.SemanticKey);
            if (entry != null &&
                string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
            {
                entry.Status = "COMMITTED";
                ledger.Save(ledgerPath);
            }
        }

        private static ActionLedgerEntry FindActionLedgerEntry(
            ActionLedger ledger,
            string playniteId,
            string semanticKey)
        {
            return ledger.Entries.FirstOrDefault(entry =>
                string.Equals(entry.PlayniteId, playniteId, StringComparison.Ordinal) &&
                string.Equals(entry.SemanticKey, semanticKey, StringComparison.Ordinal));
        }

        private static string HashActionPlan(
            IEnumerable<ActionOperationReceipt> operations)
        {
            var material = string.Join(
                "\n",
                operations
                    .OrderBy(item => item.PlayniteId ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(item => item.SemanticKey ?? string.Empty, StringComparer.Ordinal)
                    .Select(item => string.Join(
                        "|",
                        item.PlayniteId ?? string.Empty,
                        item.Kind ?? string.Empty,
                        item.SemanticKey ?? string.Empty,
                        item.ActionName ?? string.Empty,
                        item.LocalEvidenceName ?? string.Empty,
                        item.IsPlayAction ? "play" : "custom",
                        item.Outcome ?? string.Empty)));

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                return string.Concat(hash.Select(item => item.ToString("x2")));
            }
        }

        private static string HashPlan(IEnumerable<CategoryOperationReceipt> operations)
        {
            var material = string.Join(
                "\n",
                operations
                    .OrderBy(item => item.PlayniteId ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(item => item.CategoryId ?? string.Empty, StringComparer.Ordinal)
                    .Select(item => string.Join(
                        "|",
                        item.PlayniteId ?? string.Empty,
                        item.Kind ?? string.Empty,
                        item.CategoryId ?? string.Empty,
                        item.CategoryName ?? string.Empty,
                        item.Outcome ?? string.Empty)));

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                return string.Concat(hash.Select(item => item.ToString("x2")));
            }
        }
    }
}
