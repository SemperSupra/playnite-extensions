using System;
using System.IO;
using System.Linq;
using System.Text;

namespace MediaLibraryEnrichment
{
    public sealed partial class MediaLibraryEnrichmentPlugin
    {
        private void ShowAssociationReviewPreview()
        {
            try
            {
                // The user must choose an explicit local folder for each scan.
                // Cancel or an empty selection never triggers an inventory read.
                var selectedRoot = PlayniteApi.Dialogs.SelectFolder();
                if (string.IsNullOrWhiteSpace(selectedRoot))
                {
                    return;
                }
                PlayniteApi.Dialogs.ShowMessage(
                    BuildAssociationReviewPreviewSummary(selectedRoot),
                    "Media Library Enrichment — Suggestions (read-only)");
            }
            catch (Exception exception)
            {
                // Never echo sensitive paths or filenames in an error message.
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Review Preview stopped without modifying your library. " +
                    "Failure type: " + exception.GetType().Name + ".",
                    "Media Library Enrichment — Suggestions");
            }
        }

        // Explicitly read-only, deterministic core for native fixture qualification.
        // The output is for the local interactive UI only; never send it to logs,
        // hosted runners, telemetry, plugin data files or public CI artifacts.
        private string BuildAssociationReviewPreviewSummary(string selectedRoot)
        {
            var inventory = MediaFileInventory.InspectImmediateFiles(selectedRoot);
            var admission = MediaAdmissionEvidenceSnapshot.LoadOrEmpty(
                Path.Combine(GetPluginUserDataPath(), "admission-evidence.json"));
            var candidates = CaptureCandidates(admission);
            var plan = MediaAssociationReviewPlanner.Build(candidates, inventory);

            const int maxVisible = 12;
            var result = new StringBuilder();
            result.AppendLine("READ ONLY — suggestions require independent review.");
            result.AppendLine("No associations, categories, actions or covers were changed.");
            result.AppendLine();
            result.AppendLine("Admitted records considered: " + plan.EligibleObservationCount);
            result.AppendLine("Supported files inspected: " + plan.InventoriedSupportedFileCount);
            result.AppendLine("Review-required suggestions: " + plan.Suggestions.Length);
            result.AppendLine("Ambiguous groups suppressed: " + plan.AmbiguousMatchGroupCount);
            result.AppendLine("Kind conflicts suppressed: " + plan.IncompatibleKindGroupCount);
            result.AppendLine();

            var visible = plan.Suggestions.Take(maxVisible).ToArray();
            foreach (var suggestion in visible)
            {
                // Only show labels in this local dialog. No identifiers, paths, or
                // filesystem details are serialized into a persistent receipt.
                var game = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.PlayniteId, suggestion.PlayniteId,
                        StringComparison.OrdinalIgnoreCase));
                if (game == null)
                {
                    throw new InvalidOperationException("Review identity changed during capture.");
                }
                var names = suggestion.FileNames.Take(2)
                    .Select(SafeReviewDisplay).ToArray();
                var more = suggestion.FileNames.Length > names.Length
                    ? " (+" + (suggestion.FileNames.Length - names.Length) + " variants)"
                    : "";
                result.AppendLine("- " + SafeReviewDisplay(game.Name) + " => " +
                    string.Join(", ", names) + more + " [" + suggestion.MediaKindHint + "]");
            }
            if (plan.Suggestions.Length > maxVisible)
            {
                result.AppendLine("Additional suggestions hidden from this Preview: " +
                    (plan.Suggestions.Length - maxVisible));
            }
            result.AppendLine();
            result.Append("Names alone do not prove file identity. No Apply permission granted.");
            return result.ToString();
        }

        private static string SafeReviewDisplay(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "(unnamed)";
            }
            var safe = new string(raw.Select(character =>
                char.IsControl(character) ? ' ' : character).ToArray()).Trim();
            return safe.Length <= 72 ? safe : safe.Substring(0, 72) + "...";
        }
    }
}
