using System.Printing;

namespace pdfMerge.Models
{
    public class PrintQueueItem
    {
        public string Name { get; set; } = string.Empty;
        public PrintQueue Queue { get; set; } = null!;
    }
}
