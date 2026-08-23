using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using pdfMerge.Models;

namespace pdfMerge.Services
{
    /// <summary>
    /// Contract for PDF operations and metadata management.
    /// </summary>
    public interface IPdfService
    {
        bool IsSupportedFile(string filePath);
        bool IsSupportedImageFile(string filePath);
        Task<int> GetPageCountAsync(string filePath, CancellationToken token = default);
        Task<BitmapSource?> RenderPageThumbnailAsync(string filePath, int pageIndex, uint targetWidth = 350, CancellationToken token = default);
        bool HasBookmarks(IEnumerable<PdfPageItem> pageItems);
        Task MergeAndSavePdfAsync(IEnumerable<PdfPageItem> pageItems, string outputPath, bool recreateBookmarks = false);
    }
}
