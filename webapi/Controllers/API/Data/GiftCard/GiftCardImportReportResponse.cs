namespace webapi.Controllers.API.Data.GiftCard
{
    public class GiftCardImportReportResponse
    {
        public bool DryRun { get; set; }
        public int TotalRows { get; set; }
        /// <summary>Rows that pass validation (dry run) / were actually inserted (commit).</summary>
        public int Imported { get; set; }
        public long TotalBalanceCents { get; set; }
        public List<RowError> Errors { get; set; } = new();

        public class RowError
        {
            public int Line { get; set; }       // 1-based line number in the uploaded file
            public string? Code { get; set; }
            public string Reason { get; set; } = null!;
        }
    }
}
