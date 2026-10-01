using System.Collections.Generic;

namespace MediaLibraryEnrichment
{
    public sealed class CategoryOwnershipRecord
    {
        public string PlayniteId { get; set; }
        public string CategoryId { get; set; }
        public string CategoryName { get; set; }
    }

    public sealed class CategoryOwnershipLedger
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-category-ledger/v1";

        public List<CategoryOwnershipRecord> Records { get; set; } =
            new List<CategoryOwnershipRecord>();
    }

    public sealed class CategoryProjectionReceipt
    {
        public string Schema { get; set; } =
            "sempersupra-media-library-enrichment-category-receipt/v1";

        public string Mode { get; set; }
        public int CandidateCount { get; set; }
        public int PlannedOperationCount { get; set; }
        public int AppliedOperationCount { get; set; }
        public int ConflictCount { get; set; }
        public string[] Operations { get; set; }
    }
}
