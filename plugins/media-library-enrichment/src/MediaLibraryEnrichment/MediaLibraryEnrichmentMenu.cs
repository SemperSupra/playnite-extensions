using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public sealed partial class MediaLibraryEnrichmentPlugin
    {
        private const string MenuSectionName = "@Media Library Enrichment";

        public override IEnumerable<MainMenuItem> GetMainMenuItems(
            GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                MenuSection = MenuSectionName,
                Description = "Preview current enrichment",
                Action = _ => ShowPreview()
            };

            yield return new MainMenuItem
            {
                MenuSection = MenuSectionName,
                Description = "Apply and enable reconciliation",
                Action = _ => ExecuteMode("apply", false)
            };

            yield return new MainMenuItem
            {
                MenuSection = MenuSectionName,
                Description = "Observe only (disable changes)",
                Action = _ => ExecuteMode("observe", false)
            };

            yield return new MainMenuItem
            {
                MenuSection = MenuSectionName,
                Description = "Rollback owned changes",
                Action = _ => ExecuteMode("rollback", true)
            };
        }

        private void ShowPreview()
        {
            try
            {
                PlayniteApi.Dialogs.ShowMessage(
                    BuildPreviewSummary(),
                    "Media Library Enrichment Preview");
            }
            catch (Exception exception)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Preview failed: " + exception.Message,
                    "Media Library Enrichment");
            }
        }

        private string BuildPreviewSummary()
        {
            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);

            var admissionEvidence = MediaAdmissionEvidenceSnapshot.LoadOrEmpty(
                Path.Combine(dataPath, "admission-evidence.json"));
            var candidates = CaptureCandidates(admissionEvidence);
            var categoryLedger = CategoryLedger.LoadOrCreate(
                Path.Combine(dataPath, "category-ledger.json"));
            var actionLedger = ActionLedger.LoadOrCreate(
                Path.Combine(dataPath, "action-ledger.json"));
            var coverEvidence = CoverEvidenceSnapshot.LoadOrEmpty(
                Path.Combine(dataPath, "cover-evidence.json"));
            var coverLedger = CoverLedger.LoadOrCreate(
                Path.Combine(dataPath, "cover-ledger.json"));

            var categoryPlan = BuildApplyPlan(candidates, categoryLedger);
            var actionPlan = BuildActionApplyPlan(candidates, actionLedger);
            var coverPlan = BuildCoverApplyPlan(coverEvidence, coverLedger);

            var kindSummary = string.Join(
                ", ",
                candidates
                    .GroupBy(item => item.Kind ?? "unresolved")
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Key + "=" + group.Count()));

            var reasonSummary = string.Join(
                ", ",
                candidates
                    .GroupBy(item => item.ClassificationReason ?? "unresolved")
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Key + "=" + group.Count()));

            var planned =
                categoryPlan.Count(item => string.Equals(
                    item.Outcome,
                    "ADD_MEMBERSHIP",
                    StringComparison.Ordinal)) +
                actionPlan.Count(item => string.Equals(
                    item.Outcome,
                    "ADD_ACTION",
                    StringComparison.Ordinal)) +
                coverPlan.Count(item => string.Equals(
                    item.Outcome,
                    "ADD_COVER",
                    StringComparison.Ordinal));

            var conflicts =
                categoryPlan.Count(item =>
                    (item.Outcome ?? string.Empty).StartsWith(
                        "CONFLICT",
                        StringComparison.Ordinal)) +
                actionPlan.Count(item =>
                    (item.Outcome ?? string.Empty).StartsWith(
                        "CONFLICT",
                        StringComparison.Ordinal)) +
                coverPlan.Count(item =>
                    (item.Outcome ?? string.Empty).StartsWith(
                        "CONFLICT",
                        StringComparison.Ordinal));

            var userOverrides =
                categoryPlan.Count(item => string.Equals(
                    item.Outcome,
                    "USER_OVERRIDE",
                    StringComparison.Ordinal)) +
                actionPlan.Count(item => string.Equals(
                    item.Outcome,
                    "USER_OVERRIDE",
                    StringComparison.Ordinal)) +
                coverPlan.Count(item => string.Equals(
                    item.Outcome,
                    "USER_OVERRIDE",
                    StringComparison.Ordinal));

            return "No changes were applied.\n\n" +
                "Candidates: " + candidates.Length + "\n" +
                "Kinds: " + (string.IsNullOrEmpty(kindSummary)
                    ? "none"
                    : kindSummary) + "\n" +
                "Classification reasons: " + (string.IsNullOrEmpty(reasonSummary)
                    ? "none"
                    : reasonSummary) + "\n" +
                "Planned owned changes: " + planned + "\n" +
                "User overrides preserved: " + userOverrides + "\n" +
                "Conflicts requiring no automatic mutation: " + conflicts + "\n" +
                "Managed shelves: " +
                FilterPresetEnrichmentPolicy.All().Length +
                " (reconciled after categories are present).";
        }

        private void ExecuteMode(string mode, bool returnToObserve)
        {
            try
            {
                PlayniteApi.Dialogs.ShowMessage(
                    ExecuteModeCore(mode, returnToObserve),
                    "Media Library Enrichment");
            }
            catch (Exception exception)
            {
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Reconciliation failed and observe mode was requested as the fail-safe: " +
                    exception.Message,
                    "Media Library Enrichment");
            }
        }

        private string ExecuteModeCore(string mode, bool returnToObserve)
        {
            try
            {
                SaveMode(mode);
                RunReconciliation();
                var summary = BuildReceiptSummary(mode);

                if (returnToObserve)
                {
                    SaveMode("observe");
                    summary += "\n\nMode returned to observe after rollback.";
                }

                return summary;
            }
            catch
            {
                try
                {
                    SaveMode("observe");
                }
                catch
                {
                    // Preserve the original failure; startup normalization remains fail-safe.
                }

                throw;
            }
        }

        private void SaveMode(string mode)
        {
            var dataPath = GetPluginUserDataPath();
            Directory.CreateDirectory(dataPath);
            var settingsPath = Path.Combine(dataPath, "settings.json");
            var settings = MediaLibraryEnrichmentSettings.LoadOrCreate(settingsPath);
            settings.Mode = mode;
            settings.Save(settingsPath);
        }

        private string BuildReceiptSummary(string mode)
        {
            var dataPath = GetPluginUserDataPath();

            CategoryReconcileReceipt categories;
            ActionReconcileReceipt actions;
            FilterPresetReconcileReceipt presets;
            CoverReconcileReceipt covers;
            Exception error;

            if (!Serialization.TryFromJsonFile(
                    Path.Combine(dataPath, "r4i-receipt.json"),
                    out categories,
                    out error) ||
                categories == null)
            {
                throw new InvalidDataException(
                    "Category reconciliation receipt is unavailable.",
                    error);
            }

            if (!Serialization.TryFromJsonFile(
                    Path.Combine(dataPath, "action-r4i-receipt.json"),
                    out actions,
                    out error) ||
                actions == null)
            {
                throw new InvalidDataException(
                    "Action reconciliation receipt is unavailable.",
                    error);
            }

            if (!Serialization.TryFromJsonFile(
                    Path.Combine(dataPath, "filter-preset-r4i-receipt.json"),
                    out presets,
                    out error) ||
                presets == null)
            {
                throw new InvalidDataException(
                    "Filter-preset reconciliation receipt is unavailable.",
                    error);
            }

            if (!Serialization.TryFromJsonFile(
                    Path.Combine(dataPath, "cover-r4i-receipt.json"),
                    out covers,
                    out error) ||
                covers == null)
            {
                throw new InvalidDataException(
                    "Cover reconciliation receipt is unavailable.",
                    error);
            }

            var applied =
                categories.AppliedCount +
                actions.AppliedCount +
                presets.AppliedCount +
                covers.AppliedCount;
            var rollbackApplied =
                categories.RollbackAppliedCount +
                actions.RollbackAppliedCount +
                presets.RollbackAppliedCount +
                covers.RollbackAppliedCount;
            var noop =
                categories.NoopCount +
                actions.NoopCount +
                presets.NoopCount +
                covers.NoopCount;
            var userOverrides =
                categories.UserOverrideCount +
                actions.UserOverrideCount +
                presets.UserOverrideCount +
                covers.UserOverrideCount;
            var conflicts =
                categories.ConflictCount +
                actions.ConflictCount +
                presets.ConflictCount +
                covers.ConflictCount;

            return "Mode: " + mode + "\n" +
                "Applied: " + applied + "\n" +
                "Rollback applied: " + rollbackApplied + "\n" +
                "No-op: " + noop + "\n" +
                "User overrides preserved: " + userOverrides + "\n" +
                "Conflicts preserved: " + conflicts;
        }
    }
}
