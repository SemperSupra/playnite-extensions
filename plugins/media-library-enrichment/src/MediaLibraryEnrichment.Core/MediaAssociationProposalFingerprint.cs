using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MediaLibraryEnrichment
{
    // A change-detection token for local review, NOT authorization to Apply.
    // RootBinding must be provisioned by a trusted local caller, never guessed
    // from a basename, and never sent with actual user data to public CI.
    public static class MediaAssociationProposalFingerprint
    {
        private const string Contract = "mle-review-proposal/v1";

        public static string Compute(
            MediaAssociationReviewSuggestion suggestion,
            string localRootBinding)
        {
            if (suggestion == null)
            {
                throw new ArgumentNullException(nameof(suggestion));
            }
            if (string.IsNullOrEmpty(localRootBinding) ||
                localRootBinding.Length != 64 ||
                !localRootBinding.All(IsHex))
            {
                throw new ArgumentException(
                    "An explicit 32-byte hex local root-binding token is required.",
                    nameof(localRootBinding));
            }

            Guid id;
            if (!Guid.TryParse(suggestion.PlayniteId, out id) ||
                suggestion.Rule != "EXACT_TITLE_STEM_REVIEW_ONLY" ||
                suggestion.Disposition != "REVIEW_REQUIRED" ||
                !IsSupportedKind(suggestion.MediaKindHint) ||
                suggestion.FileNames == null ||
                suggestion.FileNames.Length < 1 ||
                suggestion.FileNames.Length > 64)
            {
                throw new InvalidOperationException("Unqualified review proposal.");
            }

            foreach (var name in suggestion.FileNames)
            {
                if (string.IsNullOrWhiteSpace(name) ||
                    name != name.Trim() ||
                    name.IndexOf('/') >= 0 ||
                    name.IndexOf('\\') >= 0 ||
                    name.IndexOf(':') >= 0 ||
                    name.Any(c =>
                    {
                        var kind = char.GetUnicodeCategory(c);
                        return char.IsControl(c) ||
                            kind == UnicodeCategory.Format ||
                            kind == UnicodeCategory.LineSeparator ||
                            kind == UnicodeCategory.ParagraphSeparator;
                    }) ||
                    !string.Equals(
                        MediaCandidateClassifier.ClassifyKind(name, null),
                        suggestion.MediaKindHint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Unsafe proposal filename or media kind.");
                }
            }
            var filenames = suggestion.FileNames
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(name => name, StringComparer.Ordinal).ToArray();
            if (filenames.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                    != filenames.Length)
            {
                throw new InvalidOperationException("Ambiguous duplicate review filenames.");
            }

            using (var payload = new MemoryStream())
            using (var writer = new BinaryWriter(payload, Encoding.UTF8, true))
            {
                // BinaryWriter length-prefixes each UTF-8 field. This avoids
                // delimiter injection and makes a stable versioned canonical IR.
                writer.Write(Contract);
                writer.Write(id.ToString("D"));
                writer.Write(localRootBinding.ToLowerInvariant());
                writer.Write(suggestion.Rule);
                writer.Write(suggestion.Disposition);
                writer.Write(suggestion.MediaKindHint);
                writer.Write(filenames.Length);
                foreach (var name in filenames)
                {
                    writer.Write(name);
                }
                writer.Flush();

                using (var hash = SHA256.Create())
                {
                    return BitConverter.ToString(hash.ComputeHash(payload.ToArray()))
                        .Replace("-", "").ToLowerInvariant();
                }
            }
        }

        public static bool MatchesCurrentProposal(
            string expectedFingerprint,
            MediaAssociationReviewSuggestion currentSuggestion,
            string currentRootBinding)
        {
            if (string.IsNullOrWhiteSpace(expectedFingerprint) ||
                expectedFingerprint.Length != 64 ||
                !expectedFingerprint.All(IsHex))
            {
                return false;
            }
            // Invalid current evidence must fail closed, not silently compare
            // against a stale or partly filled plan.
            try
            {
                return string.Equals(
                    expectedFingerprint, Compute(currentSuggestion, currentRootBinding),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static bool IsSupportedKind(string kind)
        {
            return kind == "book" || kind == "comic" || kind == "audio";
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9') ||
                   (value >= 'a' && value <= 'f') ||
                   (value >= 'A' && value <= 'F');
        }
    }
}
