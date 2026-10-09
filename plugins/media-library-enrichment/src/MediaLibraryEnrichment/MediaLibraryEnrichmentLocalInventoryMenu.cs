using System;
using System.Linq;

namespace MediaLibraryEnrichment
{
    public sealed partial class MediaLibraryEnrichmentPlugin
    {
        // Explicit user interaction is the only entry point for this read-only
        // operation. Never invoke from application startup or Apply mode.
        private void ShowLocalInventoryPreview()
        {
            try
            {
                // Playnite owns the modal picker. Cancel returns an empty path
                // and must perform no enumeration.
                var selectedRoot = PlayniteApi.Dialogs.SelectFolder();
                if (string.IsNullOrWhiteSpace(selectedRoot))
                {
                    return;
                }

                PlayniteApi.Dialogs.ShowMessage(
                    BuildLocalInventoryPreviewSummary(selectedRoot),
                    "Media Library Enrichment — Local Inventory");
            }
            catch (Exception exception)
            {
                // Avoid printing selected paths or filenames in logs or receipts.
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Local inventory stopped without changing Playnite data. " +
                    "Failure type: " + exception.GetType().Name + ".",
                    "Media Library Enrichment — Local Inventory");
            }
        }
        // Separate, deterministic non-mutating core for native RDTE.
        // Caller explicitly supplies a selected root; never invoked at startup.
        private string BuildLocalInventoryPreviewSummary(string selectedRoot)
        {
            var result = MediaFileInventory.InspectImmediateFiles(selectedRoot);
            var kinds = string.Join(
                ", ",
                result.SupportedFiles
                    .GroupBy(item => item.Kind ?? "unresolved")
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Key + "=" + group.Count()));

            return "Read-only inspection complete. No Playnite records were modified.\n\n" +
                "Entries examined: " + result.EntriesExamined + "\n" +
                "Supported media filenames: " + result.SupportedFiles.Length + "\n" +
                "Kinds: " + (string.IsNullOrEmpty(kinds) ? "none" : kinds) + "\n" +
                "Reparse-point entries skipped: " + result.ReparsePointsSkipped + "\n\n" +
                "Nothing has been associated with a Playnite record.\n" +
                "This inspection does not authorize Apply, covers, or actions.";
        }
    }
}
