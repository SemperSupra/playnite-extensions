using System;
using System.IO;
using System.Linq;

namespace MediaLibraryEnrichment
{
    // Read-only evidence metadata. Does not prove media authenticity, approve a
    // binding, lock a file, or authorize a Playnite mutation.
    public static class MediaLocalFileEvidencePreflight
    {
        public static MediaLocalEvidenceSnapshot Inspect(
            string explicitlySelectedRoot,
            MediaAssociationReviewSuggestion reviewProposal,
            string localRootBinding)
        {
            // Validate rule, exact identity, filename set, and binding first.
            var proposalToken = MediaAssociationProposalFingerprint.Compute(
                reviewProposal, localRootBinding);
            if (reviewProposal.FileNames.Length > 64)
            {
                throw new InvalidOperationException("Too many files in review proposal.");
            }

            // Reuse the bounded, immediate-files-only root validator. It rejects
            // whole-volume, UNC, drive-relative, and reparse ancestor roots.
            var inventory = MediaFileInventory.InspectImmediateFiles(explicitlySelectedRoot);
            var canonicalRoot = Path.GetFullPath(explicitlySelectedRoot);
            var entries = reviewProposal.FileNames
                .Select(name =>
                {
                    var matches = inventory.SupportedFiles.Where(item =>
                        string.Equals(item.FileName, name,
                            StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (matches.Length != 1 ||
                        !string.Equals(matches[0].Kind, reviewProposal.MediaKindHint,
                            StringComparison.Ordinal) ||
                        !string.Equals(matches[0].FileName, name, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Review filename missing, renamed, or ambiguous.");
                    }

                    var file = new FileInfo(Path.Combine(canonicalRoot, name));
                    file.Refresh();
                    if (!file.Exists ||
                        (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                        (file.Attributes & FileAttributes.Directory) != 0 ||
                        file.Length <= 0)
                    {
                        throw new InvalidOperationException(
                            "File evidence is missing, empty, or a reparse point.");
                    }
                    return new MediaLocalEvidenceEntry
                    {
                        FileName = name,
                        Length = file.Length,
                        LastWriteUtcTicks = file.LastWriteTimeUtc.Ticks
                    };
                })
                .OrderBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.FileName, StringComparer.Ordinal)
                .ToArray();

            return new MediaLocalEvidenceSnapshot
            {
                Schema = "mle-local-evidence-preflight/v1",
                ProposalFingerprint = proposalToken,
                CanonicalRoot = canonicalRoot,
                Entries = entries
            };
        }

        public static bool MatchesFreshEvidence(
            MediaLocalEvidenceSnapshot prior,
            string explicitlySelectedRoot,
            MediaAssociationReviewSuggestion currentProposal,
            string currentRootBinding)
        {
            if (prior == null ||
                prior.Schema != "mle-local-evidence-preflight/v1" ||
                prior.Entries == null ||
                prior.Entries.Length < 1 ||
                prior.Entries.Length > 64 ||
                string.IsNullOrWhiteSpace(prior.CanonicalRoot))
            {
                return false;
            }

            // Merely detects common metadata drift. The local review receipt
            // is NOT an authorization or an anti-TOCTOU file handle.
            try
            {
                var now = Inspect(explicitlySelectedRoot, currentProposal, currentRootBinding);
                if (!string.Equals(prior.ProposalFingerprint, now.ProposalFingerprint,
                        StringComparison.Ordinal) ||
                    !string.Equals(prior.CanonicalRoot, now.CanonicalRoot,
                        StringComparison.OrdinalIgnoreCase) ||
                    prior.Entries.Length != now.Entries.Length)
                {
                    return false;
                }
                for (var index = 0; index < now.Entries.Length; index++)
                {
                    var left = prior.Entries[index];
                    var right = now.Entries[index];
                    if (left == null ||
                        !string.Equals(left.FileName, right.FileName, StringComparison.Ordinal) ||
                        left.Length != right.Length ||
                        left.LastWriteUtcTicks != right.LastWriteUtcTicks)
                    {
                        return false;
                    }
                }
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public sealed class MediaLocalEvidenceSnapshot
    {
        public string Schema { get; set; }
        // Owner-local ephemeral only. Never serialize selected roots or file
        // basenames into public CI artifacts or other remote logs.
        public string CanonicalRoot { get; set; }
        public string ProposalFingerprint { get; set; }
        public MediaLocalEvidenceEntry[] Entries { get; set; }
    }

    public sealed class MediaLocalEvidenceEntry
    {
        public string FileName { get; set; }
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
    }
}
