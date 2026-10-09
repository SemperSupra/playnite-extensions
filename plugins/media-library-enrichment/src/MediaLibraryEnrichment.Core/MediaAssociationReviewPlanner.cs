using System;
using System.Collections.Generic;
using System.Linq;

namespace MediaLibraryEnrichment
{
    // Discovery proposals are NOT admission evidence or authorization to mutate.
    // The caller must supply a trusted admission snapshot and an explicit
    // read-only inventory from a user-selected directory.
    public static class MediaAssociationReviewPlanner
    {
        public static MediaAssociationReviewPlan Build(
            IEnumerable<MediaObservation> admittedObservations,
            MediaFileInventoryResult inventory)
        {
            if (admittedObservations == null || inventory == null ||
                inventory.SupportedFiles == null)
            {
                throw new ArgumentNullException("A complete admission snapshot and inventory are required.");
            }

            // Bound enumeration before materializing potentially lazy/untrusted inputs.
            var games = admittedObservations.Take(10001).ToArray();
            var files = inventory.SupportedFiles;
            if (games.Length > 10000 || files.Length > 10000)
            {
                throw new InvalidOperationException("Review input exceeds the bounded item limit.");
            }
            if (games.Any(game => game == null) || files.Any(file => file == null))
            {
                throw new InvalidOperationException("Null review inputs are prohibited.");
            }

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file.FileName) ||
                    file.FileName == "." || file.FileName == ".." ||
                    file.FileName.IndexOf('/') >= 0 ||
                    file.FileName.IndexOf('\\') >= 0 ||
                    file.FileName.IndexOf(':') >= 0 ||
                    file.FileName.Any(char.IsControl) ||
                    !string.Equals(file.FileName, file.FileName.Trim(),
                        StringComparison.Ordinal) ||
                    !IsSupportedKind(file.Kind) ||
                    !string.Equals(
                        MediaCandidateClassifier.ClassifyKind(file.FileName, null),
                        file.Kind,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Untrusted file inventory entry.");
                }
            }

            // A second observation with the same normalized title must not be
            // ignored merely because it failed admission; collision remains real.
            var duplicateTitles = new HashSet<string>(
                games.Where(game => !string.IsNullOrWhiteSpace(game.Name))
                    .GroupBy(game => game.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key),
                StringComparer.OrdinalIgnoreCase);

            var eligible = games.Where(game =>
                !string.IsNullOrWhiteSpace(game.Name) &&
                !duplicateTitles.Contains(game.Name.Trim()) &&
                Guid.TryParse(game.PlayniteId, out var ignored) &&
                !string.IsNullOrWhiteSpace(game.AdmissionProducerKind) &&
                !string.IsNullOrWhiteSpace(game.AdmissionEvidenceKey)).ToArray();

            // One Playnite ID must not appear as competing observations.
            var duplicateIds = new HashSet<string>(
                eligible.GroupBy(game => Guid.Parse(game.PlayniteId).ToString(),
                    StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() != 1)
                    .Select(group => group.Key), StringComparer.OrdinalIgnoreCase);
            eligible = eligible.Where(game =>
                !duplicateIds.Contains(Guid.Parse(game.PlayniteId).ToString())).ToArray();

            var titleGroups = eligible.GroupBy(
                    game => game.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            var fileGroups = files.GroupBy(
                    file => Stem(file.FileName), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(),
                    StringComparer.OrdinalIgnoreCase);

            var candidates = new List<MediaAssociationReviewSuggestion>();
            var ambiguous = 0;
            var incompatible = 0;

            foreach (var group in fileGroups.OrderBy(
                pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                MediaObservation[] gamesWithTitle;
                if (!titleGroups.TryGetValue(group.Key, out gamesWithTitle))
                {
                    continue;
                }

                var variants = group.Value;
                var kinds = variants.Select(file => file.Kind)
                    .Distinct(StringComparer.Ordinal).ToArray();
                var uniqueBasenames = variants.Select(file => file.FileName)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count();

                if (gamesWithTitle.Length != 1 ||
                    uniqueBasenames != variants.Length)
                {
                    ambiguous++;
                    continue;
                }
                if (kinds.Length != 1 ||
                    (gamesWithTitle[0].Kind != "unresolved" &&
                     !string.Equals(gamesWithTitle[0].Kind, kinds[0],
                         StringComparison.Ordinal)))
                {
                    incompatible++;
                    continue;
                }

                candidates.Add(new MediaAssociationReviewSuggestion
                {
                    PlayniteId = gamesWithTitle[0].PlayniteId,
                    MediaKindHint = kinds[0],
                    FileNames = variants.Select(file => file.FileName)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(name => name, StringComparer.Ordinal)
                        .ToArray(),
                    Rule = "EXACT_TITLE_STEM_REVIEW_ONLY",
                    Disposition = "REVIEW_REQUIRED"
                });
            }

            return new MediaAssociationReviewPlan
            {
                EligibleObservationCount = eligible.Length,
                InventoriedSupportedFileCount = files.Length,
                AmbiguousMatchGroupCount = ambiguous,
                IncompatibleKindGroupCount = incompatible,
                Suggestions = candidates.OrderBy(
                    item => item.PlayniteId, StringComparer.Ordinal).ToArray()
            };
        }

        private static bool IsSupportedKind(string kind)
        {
            return string.Equals(kind, "book", StringComparison.Ordinal) ||
                string.Equals(kind, "comic", StringComparison.Ordinal) ||
                string.Equals(kind, "audio", StringComparison.Ordinal);
        }

        private static string Stem(string name)
        {
            var lastDot = name.LastIndexOf('.');
            if (lastDot <= 0)
            {
                throw new InvalidOperationException("Inventory file lacks a usable stem.");
            }
            var stem = name.Substring(0, lastDot).Trim();
            if (stem.Length == 0)
            {
                throw new InvalidOperationException("Inventory file lacks a usable stem.");
            }
            return stem;
        }
    }

    public sealed class MediaAssociationReviewSuggestion
    {
        public string PlayniteId { get; set; }
        public string[] FileNames { get; set; }
        public string MediaKindHint { get; set; }
        public string Rule { get; set; }
        public string Disposition { get; set; }
    }

    public sealed class MediaAssociationReviewPlan
    {
        public int EligibleObservationCount { get; set; }
        public int InventoriedSupportedFileCount { get; set; }
        public int AmbiguousMatchGroupCount { get; set; }
        public int IncompatibleKindGroupCount { get; set; }
        public MediaAssociationReviewSuggestion[] Suggestions { get; set; }
    }
}
