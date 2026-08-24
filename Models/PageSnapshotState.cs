using System.Collections.Generic;

namespace pdfMerge.Models
{
    public class PageSnapshotState
    {
        public string SourceFilePath { get; set; } = string.Empty;
        public int OriginalPageIndex { get; set; }
        public int OriginalDisplayPageNumber { get; set; }
        public int InitialRotation { get; set; }
        public List<AppliedSignature> InitialSignatures { get; set; } = new List<AppliedSignature>();
    }
}
