using System;
using System.Linq;
using System.Windows.Forms;

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
                string selectedRoot;
                using (var picker = new FolderBrowserDialog())
                {
                    picker.Description = "Select one folder to inspect read-only (top-level files only).";
                    picker.ShowNewFolderButton = false;

                    if (picker.ShowDialog() != DialogResult.OK ||
                        string.IsNullOrWhiteSpace(picker.SelectedPath))
                    {
                        return;
                    }
                    selectedRoot = picker.SelectedPath;
                }

                var result = MediaFileInventory.InspectImmediateFiles(selectedRoot);
                var kinds = string.Join(
                    ", ",
                    result.SupportedFiles
                        .GroupBy(item => item.Kind ?? "unresolved")
                        .OrderBy(group => group.Key, StringComparer.Ordinal)
                        .Select(group => group.Key + "=" + group.Count()));

                PlayniteApi.Dialogs.ShowMessage(
                    "Read-only inspection complete. No Playnite records were modified.\n\n" +
                    "Entries examined: " + result.EntriesExamined + "\n" +
                    "Supported media filenames: " + result.SupportedFiles.Length + "\n" +
                    "Kinds: " + (string.IsNullOrEmpty(kinds) ? "none" : kinds) + "\n" +
                    "Reparse-point entries skipped: " + result.ReparsePointsSkipped + "\n\n" +
                    "Nothing has been associated with a Playnite record.\n" +
                    "This inspection does not authorize Apply, covers, or actions.",
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
    }
}
