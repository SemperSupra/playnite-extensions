using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MediaLibraryEnrichment
{
    public sealed partial class MediaLibraryEnrichmentPlugin
    {
        private CoverReconcileReceipt ApplyCoverEnrichment(
            CoverEvidenceSnapshot evidence,
            CoverLedger ledger,
            string ledgerPath)
        {
            var operations = BuildCoverApplyPlan(evidence, ledger);
            var receipt = new CoverReconcileReceipt
            {
                Mode = "apply",
                CandidateCount = operations.Count,
                PlanSha256 = HashCoverPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                if (string.Equals(operation.Outcome, "NOOP", StringComparison.Ordinal))
                {
                    receipt.NoopCount++;
                    CommitRecoveredCoverLedgerIfNeeded(
                        operation,
                        ledger,
                        ledgerPath);
                    continue;
                }

                if (string.Equals(operation.Outcome, "USER_OVERRIDE", StringComparison.Ordinal))
                {
                    receipt.UserOverrideCount++;
                    var overrideEntry = FindCoverLedgerEntry(
                        ledger,
                        operation.PlayniteId,
                        operation.EvidenceKey);
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

                if (!string.Equals(operation.Outcome, "ADD_COVER", StringComparison.Ordinal))
                {
                    continue;
                }

                var evidenceItem = evidence.Items.FirstOrDefault(item =>
                    string.Equals(
                        item.PlayniteId,
                        operation.PlayniteId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        item.EvidenceKey,
                        operation.EvidenceKey,
                        StringComparison.Ordinal));
                if (evidenceItem == null ||
                    !EvidenceFileMatches(evidenceItem))
                {
                    operation.Outcome = "CONFLICT_EVIDENCE_PRECONDITION";
                    operation.Detail = "Cover evidence changed before apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var gameId = Guid.Parse(operation.PlayniteId);
                var game = PlayniteApi.Database.Games.Get(gameId);
                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Game disappeared before cover apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(game.CoverImage))
                {
                    operation.Outcome = "CONFLICT_COVER_PRECONDITION";
                    operation.Detail = "CoverImage became non-empty before apply.";
                    receipt.ConflictCount++;
                    continue;
                }

                var entry = FindCoverLedgerEntry(
                    ledger,
                    operation.PlayniteId,
                    operation.EvidenceKey);
                if (entry == null)
                {
                    entry = new CoverLedgerEntry
                    {
                        PlayniteId = operation.PlayniteId,
                        EvidenceKey = operation.EvidenceKey,
                        ContentSha256 = operation.ContentSha256,
                        PriorCoverImage = game.CoverImage ?? string.Empty,
                        Status = "PLANNED"
                    };
                    ledger.Entries.Add(entry);
                }
                else
                {
                    entry.ContentSha256 = operation.ContentSha256;
                    entry.PriorCoverImage = game.CoverImage ?? string.Empty;
                    entry.Status = "PLANNED";
                }

                ledger.Save(ledgerPath);

                var databasePath =
                    PlayniteApi.Database.AddFile(evidenceItem.LocalPath, game.Id);
                entry.AppliedCoverImage = databasePath;
                entry.Status = "APPLYING";
                ledger.Save(ledgerPath);

                game.CoverImage = databasePath;
                PlayniteApi.Database.Games.Update(game);

                var verified = PlayniteApi.Database.Games.Get(game.Id);
                if (verified == null ||
                    !string.Equals(
                        verified.CoverImage,
                        databasePath,
                        StringComparison.Ordinal))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "CoverImage did not match plugin-applied database file.";
                    throw new InvalidOperationException(operation.Detail);
                }

                entry.Status = "COMMITTED";
                ledger.Save(ledgerPath);

                operation.Outcome = "APPLIED";
                operation.Detail = "Native CoverImage applied and verified.";
                receipt.AppliedCount++;
            }

            return receipt;
        }

        private List<CoverOperationReceipt> BuildCoverApplyPlan(
            CoverEvidenceSnapshot evidence,
            CoverLedger ledger)
        {
            var operations = new List<CoverOperationReceipt>();

            foreach (var item in evidence.Items
                .OrderBy(value => value.PlayniteId ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(value => value.EvidenceKey ?? string.Empty, StringComparer.Ordinal))
            {
                var operation = new CoverOperationReceipt
                {
                    PlayniteId = item.PlayniteId,
                    EvidenceKey = item.EvidenceKey,
                    ContentSha256 = (item.ContentSha256 ?? string.Empty).ToLowerInvariant(),
                    LocalEvidenceName = string.IsNullOrWhiteSpace(item.LocalPath)
                        ? string.Empty
                        : Path.GetFileName(item.LocalPath)
                };

                Guid gameId;
                if (!Guid.TryParse(item.PlayniteId, out gameId))
                {
                    operation.Outcome = "CONFLICT_GAME_ID";
                    operation.Detail = "Cover evidence has an invalid Playnite item ID.";
                    operations.Add(operation);
                    continue;
                }

                var game = PlayniteApi.Database.Games.Get(gameId);
                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Cover evidence targets a missing game.";
                    operations.Add(operation);
                    continue;
                }

                var entry = FindCoverLedgerEntry(
                    ledger,
                    item.PlayniteId,
                    item.EvidenceKey);
                var matchesOwned =
                    entry != null &&
                    !string.IsNullOrWhiteSpace(entry.AppliedCoverImage) &&
                    string.Equals(
                        game.CoverImage,
                        entry.AppliedCoverImage,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        entry.ContentSha256,
                        operation.ContentSha256,
                        StringComparison.OrdinalIgnoreCase);

                var decision = CoverEnrichmentPolicy.Decide(
                    EvidenceFileMatches(item),
                    !string.IsNullOrWhiteSpace(game.CoverImage),
                    matchesOwned,
                    entry == null ? null : entry.Status);

                switch (decision)
                {
                    case CoverPlanDecision.Add:
                        operation.Outcome = "ADD_COVER";
                        operation.Detail = "Missing CoverImage with valid normalized cover evidence.";
                        break;
                    case CoverPlanDecision.Noop:
                        operation.Outcome = "NOOP";
                        operation.Detail = "Plugin-owned CoverImage already matches desired evidence.";
                        break;
                    case CoverPlanDecision.UserOverride:
                        operation.Outcome = "USER_OVERRIDE";
                        operation.Detail =
                            "Previously managed CoverImage is absent outside plugin rollback; preserve the external/user override.";
                        break;
                    case CoverPlanDecision.ConflictEvidence:
                        operation.Outcome = "CONFLICT_EVIDENCE";
                        operation.Detail = "Normalized cover evidence is missing or hash-mismatched.";
                        break;
                    default:
                        operation.Outcome = "CONFLICT_COVER_PRESENT";
                        operation.Detail = "Existing CoverImage is user/external state and is preserved.";
                        break;
                }

                operations.Add(operation);
            }

            return operations;
        }

        private CoverReconcileReceipt RollbackCoverEnrichment(
            CoverLedger ledger,
            string ledgerPath)
        {
            var committed = ledger.Entries
                .Where(entry =>
                    string.Equals(entry.Status, "COMMITTED", StringComparison.Ordinal) ||
                    string.Equals(entry.Status, "APPLYING", StringComparison.Ordinal) ||
                    string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal))
                .OrderBy(entry => entry.PlayniteId, StringComparer.Ordinal)
                .ThenBy(entry => entry.EvidenceKey, StringComparer.Ordinal)
                .ToList();

            var operations = committed.Select(entry => new CoverOperationReceipt
            {
                PlayniteId = entry.PlayniteId,
                EvidenceKey = entry.EvidenceKey,
                ContentSha256 = entry.ContentSha256,
                Outcome = "ROLLBACK_COVER",
                Detail = "Restore prior CoverImage only if current state still equals the plugin-applied value."
            }).ToList();

            var receipt = new CoverReconcileReceipt
            {
                Mode = "rollback",
                CandidateCount = operations.Count,
                PlanSha256 = HashCoverPlan(operations),
                Operations = operations
            };

            foreach (var operation in operations)
            {
                var entry = FindCoverLedgerEntry(
                    ledger,
                    operation.PlayniteId,
                    operation.EvidenceKey);
                var game = PlayniteApi.Database.Games.Get(Guid.Parse(operation.PlayniteId));

                if (game == null)
                {
                    operation.Outcome = "CONFLICT_GAME_MISSING";
                    operation.Detail = "Cannot rollback cover because the game no longer exists.";
                    receipt.ConflictCount++;
                    continue;
                }

                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.AppliedCoverImage))
                {
                    operation.Outcome = "CONFLICT_OWNERSHIP";
                    operation.Detail = "Cover ledger does not identify an applied database file.";
                    receipt.ConflictCount++;
                    continue;
                }

                if (!string.Equals(
                        game.CoverImage,
                        entry.AppliedCoverImage,
                        StringComparison.Ordinal))
                {
                    operation.Outcome = "CONFLICT_COVER_CHANGED";
                    operation.Detail = "CoverImage changed externally; preserved.";
                    receipt.ConflictCount++;
                    continue;
                }

                game.CoverImage = string.IsNullOrWhiteSpace(entry.PriorCoverImage)
                    ? null
                    : entry.PriorCoverImage;
                PlayniteApi.Database.Games.Update(game);

                var verified = PlayniteApi.Database.Games.Get(game.Id);
                var expected = string.IsNullOrWhiteSpace(entry.PriorCoverImage)
                    ? string.Empty
                    : entry.PriorCoverImage;
                var observed = verified == null || string.IsNullOrWhiteSpace(verified.CoverImage)
                    ? string.Empty
                    : verified.CoverImage;
                if (!string.Equals(expected, observed, StringComparison.Ordinal))
                {
                    operation.Outcome = "FAILED_VERIFY";
                    operation.Detail = "Prior CoverImage was not restored after rollback.";
                    throw new InvalidOperationException(operation.Detail);
                }

                PlayniteApi.Database.RemoveFile(entry.AppliedCoverImage);
                entry.Status = "ROLLED_BACK";
                ledger.Save(ledgerPath);

                operation.Outcome = "ROLLBACK_APPLIED";
                operation.Detail = "Plugin-owned CoverImage reverted and database file removed.";
                receipt.RollbackAppliedCount++;
            }

            ledger.Save(ledgerPath);
            return receipt;
        }

        private void CommitRecoveredCoverLedgerIfNeeded(
            CoverOperationReceipt operation,
            CoverLedger ledger,
            string ledgerPath)
        {
            var entry = FindCoverLedgerEntry(
                ledger,
                operation.PlayniteId,
                operation.EvidenceKey);
            if (entry != null &&
                (string.Equals(entry.Status, "PLANNED", StringComparison.Ordinal) ||
                 string.Equals(entry.Status, "APPLYING", StringComparison.Ordinal)))
            {
                entry.Status = "COMMITTED";
                ledger.Save(ledgerPath);
            }
        }

        private static CoverLedgerEntry FindCoverLedgerEntry(
            CoverLedger ledger,
            string playniteId,
            string evidenceKey)
        {
            return ledger.Entries.FirstOrDefault(entry =>
                string.Equals(entry.PlayniteId, playniteId, StringComparison.Ordinal) &&
                string.Equals(entry.EvidenceKey, evidenceKey, StringComparison.Ordinal));
        }

        private static bool EvidenceFileMatches(CoverEvidenceItem item)
        {
            if (item == null ||
                string.IsNullOrWhiteSpace(item.LocalPath) ||
                string.IsNullOrWhiteSpace(item.ContentSha256) ||
                !File.Exists(item.LocalPath))
            {
                return false;
            }

            return string.Equals(
                ComputeFileSha256(item.LocalPath),
                item.ContentSha256,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeFileSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return string.Concat(
                    sha.ComputeHash(stream).Select(value => value.ToString("x2")));
            }
        }

        private static string HashCoverPlan(
            IEnumerable<CoverOperationReceipt> operations)
        {
            var material = string.Join(
                "\n",
                operations
                    .OrderBy(item => item.PlayniteId ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(item => item.EvidenceKey ?? string.Empty, StringComparer.Ordinal)
                    .Select(item => string.Join(
                        "|",
                        item.PlayniteId ?? string.Empty,
                        item.EvidenceKey ?? string.Empty,
                        item.ContentSha256 ?? string.Empty,
                        item.LocalEvidenceName ?? string.Empty,
                        item.Outcome ?? string.Empty)));

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                return string.Concat(hash.Select(value => value.ToString("x2")));
            }
        }
    }
}
