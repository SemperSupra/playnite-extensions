using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class ActionLedger
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-action-ledger/v1";

        public List<ActionLedgerEntry> Entries { get; set; } =
            new List<ActionLedgerEntry>();

        public static ActionLedger LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                return new ActionLedger();
            }

            ActionLedger loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                if (loaded.Entries == null)
                {
                    loaded.Entries = new List<ActionLedgerEntry>();
                }

                return loaded;
            }

            throw new InvalidDataException(
                "Media Library Enrichment action ledger is not valid JSON.",
                error);
        }

        public void Save(string path)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, Serialization.ToJson(this, true));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temp, path);
        }
    }

    public sealed class ActionLedgerEntry
    {
        public string PlayniteId { get; set; }
        public string Kind { get; set; }
        public string SemanticKey { get; set; }
        public string ActionName { get; set; }
        public string Path { get; set; }
        public string WorkingDir { get; set; }
        public string LocalEvidenceName { get; set; }
        public string Status { get; set; }
    }

    public sealed class ActionOperationReceipt
    {
        public string PlayniteId { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string SemanticKey { get; set; }
        public string ActionName { get; set; }
        public string LocalEvidenceName { get; set; }
        public bool IsPlayAction { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
    }

    public sealed class ActionReconcileReceipt
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-action-r4i/v1";

        public string Mode { get; set; }
        public string PlanSha256 { get; set; }
        public int CandidateCount { get; set; }
        public int AppliedCount { get; set; }
        public int NoopCount { get; set; }
        public int UserOverrideCount { get; set; }
        public int ConflictCount { get; set; }
        public int RollbackAppliedCount { get; set; }
        public List<ActionOperationReceipt> Operations { get; set; } =
            new List<ActionOperationReceipt>();
    }
}
