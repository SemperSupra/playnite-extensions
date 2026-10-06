using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class CategoryLedger
    {
        public string Schema { get; set; } = "sempersupra-media-library-enrichment-category-ledger/v1";
        public List<CategoryLedgerEntry> Entries { get; set; } = new List<CategoryLedgerEntry>();

        public static CategoryLedger LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                return new CategoryLedger();
            }

            CategoryLedger loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                if (loaded.Entries == null)
                {
                    loaded.Entries = new List<CategoryLedgerEntry>();
                }
                return loaded;
            }

            throw new InvalidDataException("Media Library Enrichment ledger is not valid JSON.", error);
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

    public sealed class CategoryLedgerEntry
    {
        public string PlayniteId { get; set; }
        public string Kind { get; set; }
        public string CategoryId { get; set; }
        public string CategoryName { get; set; }
        public bool PriorMembership { get; set; }
        public bool CategoryCreated { get; set; }
        public string Status { get; set; }
    }

    public sealed class CategoryOperationReceipt
    {
        public string PlayniteId { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
    }

    public sealed class CategoryReconcileReceipt
    {
        public string Schema { get; set; } = "sempersupra-media-library-enrichment-category-r4i/v1";
        public string Mode { get; set; }
        public string PlanSha256 { get; set; }
        public int CandidateCount { get; set; }
        public int AppliedCount { get; set; }
        public int NoopCount { get; set; }
        public int UserOverrideCount { get; set; }
        public int ConflictCount { get; set; }
        public int RollbackAppliedCount { get; set; }
        public List<CategoryOperationReceipt> Operations { get; set; } = new List<CategoryOperationReceipt>();
    }
}
