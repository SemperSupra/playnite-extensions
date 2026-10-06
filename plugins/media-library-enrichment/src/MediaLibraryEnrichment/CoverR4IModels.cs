using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.IO;

namespace MediaLibraryEnrichment
{
    public sealed class CoverEvidenceSnapshot
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-cover-evidence/v1";

        public List<CoverEvidenceItem> Items { get; set; } =
            new List<CoverEvidenceItem>();

        public static CoverEvidenceSnapshot LoadOrEmpty(string path)
        {
            if (!File.Exists(path))
            {
                return new CoverEvidenceSnapshot();
            }

            CoverEvidenceSnapshot loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                if (loaded.Items == null)
                {
                    loaded.Items = new List<CoverEvidenceItem>();
                }

                return loaded;
            }

            throw new InvalidDataException(
                "Media Library Enrichment cover evidence is not valid JSON.",
                error);
        }
    }

    public sealed class CoverEvidenceItem
    {
        public string PlayniteId { get; set; }
        public string EvidenceKey { get; set; }
        public string LocalPath { get; set; }
        public string ContentSha256 { get; set; }
        public string SourceKind { get; set; }
    }

    public sealed class CoverLedger
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-cover-ledger/v1";

        public List<CoverLedgerEntry> Entries { get; set; } =
            new List<CoverLedgerEntry>();

        public static CoverLedger LoadOrCreate(string path)
        {
            if (!File.Exists(path))
            {
                return new CoverLedger();
            }

            CoverLedger loaded;
            Exception error;
            if (Serialization.TryFromJsonFile(path, out loaded, out error) &&
                loaded != null)
            {
                if (loaded.Entries == null)
                {
                    loaded.Entries = new List<CoverLedgerEntry>();
                }

                return loaded;
            }

            throw new InvalidDataException(
                "Media Library Enrichment cover ledger is not valid JSON.",
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

    public sealed class CoverLedgerEntry
    {
        public string PlayniteId { get; set; }
        public string EvidenceKey { get; set; }
        public string ContentSha256 { get; set; }
        public string PriorCoverImage { get; set; }
        public string AppliedCoverImage { get; set; }
        public string Status { get; set; }
    }

    public sealed class CoverOperationReceipt
    {
        public string PlayniteId { get; set; }
        public string EvidenceKey { get; set; }
        public string ContentSha256 { get; set; }
        public string LocalEvidenceName { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
    }

    public sealed class CoverReconcileReceipt
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-cover-r4i/v1";

        public string Mode { get; set; }
        public string PlanSha256 { get; set; }
        public int CandidateCount { get; set; }
        public int AppliedCount { get; set; }
        public int NoopCount { get; set; }
        public int UserOverrideCount { get; set; }
        public int ConflictCount { get; set; }
        public int RollbackAppliedCount { get; set; }
        public List<CoverOperationReceipt> Operations { get; set; } =
            new List<CoverOperationReceipt>();
    }
}
