using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class FilterPresetLedger
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-filter-preset-ledger/v1";

        public List<FilterPresetLedgerEntry> Entries { get; set; } =
            new List<FilterPresetLedgerEntry>();

        public static FilterPresetLedger LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                return new FilterPresetLedger();
            }

            FilterPresetLedger loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                if (loaded.Entries == null)
                {
                    loaded.Entries = new List<FilterPresetLedgerEntry>();
                }

                return loaded;
            }

            throw new InvalidDataException(
                "Media Library Enrichment filter-preset ledger is not valid JSON.",
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

    public sealed class FilterPresetLedgerEntry
    {
        public string Kind { get; set; }
        public string PresetId { get; set; }
        public string PresetName { get; set; }
        public string CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string Status { get; set; }
    }

    public sealed class FilterPresetOperationReceipt
    {
        public string Kind { get; set; }
        public string PresetId { get; set; }
        public string PresetName { get; set; }
        public string CategoryId { get; set; }
        public string CategoryName { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
    }

    public sealed class FilterPresetReconcileReceipt
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-filter-preset-r4i/v1";

        public string Mode { get; set; }
        public string PlanSha256 { get; set; }
        public int CandidateCount { get; set; }
        public int AppliedCount { get; set; }
        public int NoopCount { get; set; }
        public int ConflictCount { get; set; }
        public int RollbackAppliedCount { get; set; }
        public List<FilterPresetOperationReceipt> Operations { get; set; } =
            new List<FilterPresetOperationReceipt>();
    }
}
