using System.Collections.Generic;

namespace AlphaSurveilance.DTOs
{
    public class BulkImportResponse
    {
        public int TotalProcessed { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<BulkImportFailure> Failures { get; set; } = new();
    }

    public class BulkImportFailure
    {
        public int RowIndex { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
