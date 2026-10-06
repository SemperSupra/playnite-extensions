using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MediaLibraryEnrichment
{
    public sealed partial class MediaLibraryEnrichmentPlugin
    {
        private FilterPresetReconcileReceipt ApplyFilterPresetEnrichment(
            FilterPresetLedger ledger,
            string ledgerPath)
        {
            var operations = BuildFilterPresetApplyPlan(ledger);
            var receipt = new FilterPresetReconcileReceipt
            {
                Mode = "apply",
                CandidateCount = operations.Count,
                PlanSha256 = HashFilterPresetPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                if (string.Equals(operation.Outcome, "NOOP", StringComparison.Ordinal))
                {
                    receipt.NoopCount++;
                    CommitRecoveredFilterPresetLedgerIfNeeded(
                        operation,
                        ledger,
                        ledgerPath);
                    continue;
                }

                if (string.Equals(operation.Outcome, "USER_OVERRIDE", StringComparison.Ordinal))
                {
                    receipt.UserOverrideCount++;
                    var overrideEntry =
                        FindFilterPresetLedgerEntry(ledger, operation.PresetId);
                    if (overrideEntry != null &&
                        !string.Equals(
                            overrideEntry.Status,
                            "USER_OVERRIDDEN",
                            StringComparison.Ordinal))
                    {
                        overrideEntry.Status = "USER_OVERRIDDEN";
                        ledger.Save(ledgerPath);
                    }
                    continue;
                }

                if (operation.Outcome.StartsWith("CONFLICT", StringComparison.Ordinal))
                {
                    receipt.ConflictCount++;
                    continue;
                }

                if (!string.Equals(operation.Outcome, "ADD_PRESET", StringComparison.Ordinal))
                {
                    continue;
                }

                var presetId = Guid.Parse(operation.PresetId);
                var categoryId = Guid.Parse(operation.CategoryId);
                var category = PlayniteApi.Database.Categories.Get(categoryId);
                if (category == null ||
                    !string.Equals(category.Name, operation.CategoryName, StringComparison.Ordinal))
                {
                    operation.Outcome = "CONFLICT_CATEGORY_PRECONDITION";
                    operation.Detail = "Owned category changed before preset apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var currentPreset = PlayniteApi.Database.FilterPresets.Get(presetId);
                var conflictingPreset = PlayniteApi.Database.FilterPresets.FirstOrDefault(item =>
                    item.Id != presetId &&
                    string.Equals(item.Name, operation.PresetName, StringComparison.Ordinal));

                if (conflictingPreset != null ||
                    (currentPreset != null &&
                     !FilterPresetMatchesDesired(
                         currentPreset,
                         operation.PresetName,
                         categoryId)))
                {
                    operation.Outcome = "CONFLICT_PRESET_PRECONDITION";
                    operation.Detail = "Preset identity or settings changed before apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                if (currentPreset != null)
                {
                    operation.Outcome = "PRECONDITION_CHANGED_NOOP";
                    operation.Detail = "Desired preset appeared before apply.";
                    receipt.NoopCount++;
                    CommitRecoveredFilterPresetLedgerIfNeeded(
                        operation,
                        ledger,
                        ledgerPath);
                    continue;
                }

                var entry = FindFilterPresetLedgerEntry(ledger, operation.PresetId);
                if (entry == null)
                {
                    entry = new FilterPresetLedgerEntry
                    {
                        Kind = operation.Kind,
                        PresetId = operation.PresetId,
                        PresetName = operation.PresetName,
                        CategoryId = operation.CategoryId,
                        CategoryName = operation.CategoryName,
                        Status = "PLANNED"
                    };
                    ledger.Entries.Add(entry);
                }
                else
                {
                    entry.Status = "PLANNED";
                }

                ledger.Save(ledgerPath);

                PlayniteApi.Database.FilterPresets.Add(
                    CreateDesiredFilterPreset(
                        presetId,
                        operation.PresetName,
                        categoryId));

                var verified = PlayniteApi.Database.FilterPresets.Get(presetId);
                if (!FilterPresetMatchesDesired(
                    verified,
                    operation.PresetName,
                    categoryId))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Filter preset did not match desired state after apply.";
                    throw new InvalidOperationException(operation.Detail);
                }

                entry.Status = "COMMITTED";
                ledger.Save(ledgerPath);

                operation.Outcome = "APPLIED";
                operation.Detail = "Native filter preset applied and verified.";
                receipt.AppliedCount++;
            }

            return receipt;
        }

        private List<FilterPresetOperationReceipt> BuildFilterPresetApplyPlan(
            FilterPresetLedger ledger)
        {
            var operations = new List<FilterPresetOperationReceipt>();

            foreach (var spec in FilterPresetEnrichmentPolicy.All())
            {
                var operation = new FilterPresetOperationReceipt
                {
                    Kind = spec.Kind,
                    PresetId = spec.PresetId.ToString(),
                    PresetName = spec.PresetName,
                    CategoryId = spec.CategoryId.ToString(),
                    CategoryName = spec.CategoryName
                };

                var category = PlayniteApi.Database.Categories.Get(spec.CategoryId);
                if (category == null ||
                    !string.Equals(category.Name, spec.CategoryName, StringComparison.Ordinal))
                {
                    operation.Outcome = "CONFLICT_CATEGORY_PRECONDITION";
                    operation.Detail = "Required owned category is absent or changed.";
                    operations.Add(operation);
                    continue;
                }

                var preset = PlayniteApi.Database.FilterPresets.Get(spec.PresetId);
                var conflictingPreset = PlayniteApi.Database.FilterPresets.FirstOrDefault(item =>
                    item.Id != spec.PresetId &&
                    string.Equals(item.Name, spec.PresetName, StringComparison.Ordinal));

                var desiredPresent =
                    preset != null &&
                    FilterPresetMatchesDesired(
                        preset,
                        spec.PresetName,
                        spec.CategoryId);
                var identityConflict =
                    conflictingPreset != null ||
                    (preset != null && !desiredPresent);
                var ledgerEntry = FindFilterPresetLedgerEntry(
                    ledger,
                    spec.PresetId.ToString());
                var decision = FilterPresetEnrichmentPolicy.DecidePresence(
                    desiredPresent,
                    identityConflict,
                    ledgerEntry == null ? null : ledgerEntry.Status);

                if (decision == FilterPresetPresenceDecision.Conflict)
                {
                    operation.Outcome = "CONFLICT_PRESET_IDENTITY";
                    operation.Detail = "Desired preset name or stable ID is already occupied by different state.";
                }
                else if (decision == FilterPresetPresenceDecision.Noop)
                {
                    operation.Outcome = "NOOP";
                    operation.Detail = "Desired native filter preset already exists.";
                }
                else if (decision == FilterPresetPresenceDecision.UserOverride)
                {
                    operation.Outcome = "USER_OVERRIDE";
                    operation.Detail =
                        "Previously managed filter preset is absent outside plugin rollback; preserve the external/user override.";
                }
                else
                {
                    operation.Outcome = "ADD_PRESET";
                    operation.Detail = "Missing owned native filter preset.";
                }

                operations.Add(operation);
            }

            return operations
                .OrderBy(item => item.PresetId, StringComparer.Ordinal)
                .ToList();
        }

        private FilterPresetReconcileReceipt RollbackFilterPresetEnrichment(
            FilterPresetLedger ledger,
            string ledgerPath)
        {
            var committed = ledger.Entries
                .Where(entry =>
                    string.Equals(entry.Status, "COMMITTED", StringComparison.Ordinal) ||
                    string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
                .OrderBy(entry => entry.PresetId, StringComparer.Ordinal)
                .ToList();

            var operations = committed
                .Select(entry => new FilterPresetOperationReceipt
                {
                    Kind = entry.Kind,
                    PresetId = entry.PresetId,
                    PresetName = entry.PresetName,
                    CategoryId = entry.CategoryId,
                    CategoryName = entry.CategoryName,
                    Outcome = "ROLLBACK_PRESET",
                    Detail = "Remove only the exact filter preset recorded by the ownership ledger."
                })
                .ToList();

            var receipt = new FilterPresetReconcileReceipt
            {
                Mode = "rollback",
                CandidateCount = operations.Count,
                PlanSha256 = HashFilterPresetPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                var entry = FindFilterPresetLedgerEntry(ledger, operation.PresetId);
                var presetId = Guid.Parse(operation.PresetId);
                var categoryId = Guid.Parse(operation.CategoryId);
                var preset = PlayniteApi.Database.FilterPresets.Get(presetId);

                if (preset == null)
                {
                    operation.Outcome = "ROLLBACK_NOOP";
                    operation.Detail = "Owned filter preset is already absent.";
                    receipt.NoopCount++;
                    if (entry != null)
                    {
                        entry.Status = "ROLLED_BACK";
                    }
                    continue;
                }

                if (entry == null)
                {
                    operation.Outcome = "CONFLICT_OWNERSHIP";
                    operation.Detail = "Ledger does not authorize removal of this filter preset.";
                    receipt.ConflictCount++;
                    continue;
                }

                if (!FilterPresetMatchesDesired(
                    preset,
                    entry.PresetName,
                    categoryId))
                {
                    operation.Outcome = "CONFLICT_PRESET_CHANGED";
                    operation.Detail = "Owned filter preset changed externally; preserved.";
                    receipt.ConflictCount++;
                    continue;
                }

                var removed = PlayniteApi.Database.FilterPresets.Remove(presetId);
                var verified = PlayniteApi.Database.FilterPresets.Get(presetId);
                if (!removed || verified != null)
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Owned filter preset remained after rollback.";
                    throw new InvalidOperationException(operation.Detail);
                }

                entry.Status = "ROLLED_BACK";
                operation.Outcome = "ROLLBACK_APPLIED";
                operation.Detail = "Owned filter preset removed and verified.";
                receipt.RollbackAppliedCount++;
            }

            ledger.Save(ledgerPath);
            return receipt;
        }

        private static FilterPreset CreateDesiredFilterPreset(
            Guid presetId,
            string presetName,
            Guid categoryId)
        {
            return new FilterPreset
            {
                Id = presetId,
                Name = presetName,
                Settings = new FilterPresetSettings
                {
                    UseAndFilteringStyle = true,
                    Category = new IdItemFilterItemProperties(categoryId)
                },
                SortingOrder = SortOrder.Name,
                SortingOrderDirection = SortOrderDirection.Ascending,
                GroupingOrder = GroupableField.None,
                ShowInFullscreeQuickSelection = true
            };
        }

        private static bool FilterPresetMatchesDesired(
            FilterPreset preset,
            string presetName,
            Guid categoryId)
        {
            if (preset == null ||
                preset.Settings == null ||
                preset.Settings.Category == null)
            {
                return false;
            }

            var ids = preset.Settings.Category.Ids;
            return string.Equals(preset.Name, presetName, StringComparison.Ordinal) &&
                preset.Settings.UseAndFilteringStyle &&
                ids != null &&
                ids.Count == 1 &&
                ids[0] == categoryId &&
                string.IsNullOrEmpty(preset.Settings.Category.Text) &&
                preset.SortingOrder == SortOrder.Name &&
                preset.SortingOrderDirection == SortOrderDirection.Ascending &&
                preset.GroupingOrder == GroupableField.None &&
                preset.ShowInFullscreeQuickSelection;
        }

        private void CommitRecoveredFilterPresetLedgerIfNeeded(
            FilterPresetOperationReceipt operation,
            FilterPresetLedger ledger,
            string ledgerPath)
        {
            var entry = FindFilterPresetLedgerEntry(ledger, operation.PresetId);
            if (entry != null &&
                string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
            {
                entry.Status = "COMMITTED";
                ledger.Save(ledgerPath);
            }
        }

        private static FilterPresetLedgerEntry FindFilterPresetLedgerEntry(
            FilterPresetLedger ledger,
            string presetId)
        {
            return ledger.Entries.FirstOrDefault(entry =>
                string.Equals(entry.PresetId, presetId, StringComparison.Ordinal));
        }

        private static string HashFilterPresetPlan(
            IEnumerable<FilterPresetOperationReceipt> operations)
        {
            var material = string.Join(
                "\n",
                operations
                    .OrderBy(item => item.PresetId ?? string.Empty, StringComparer.Ordinal)
                    .Select(item => string.Join(
                        "|",
                        item.Kind ?? string.Empty,
                        item.PresetId ?? string.Empty,
                        item.PresetName ?? string.Empty,
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
