using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using pdfMerge.Models;

using WinPdfDocument = Windows.Data.Pdf.PdfDocument;
using WinPdfPage = Windows.Data.Pdf.PdfPage;
using WinPdfRenderOptions = Windows.Data.Pdf.PdfPageRenderOptions;

using PdfSharpDocument = PdfSharp.Pdf.PdfDocument;
using PdfSharpPage = PdfSharp.Pdf.PdfPage;
using PdfSharpReader = PdfSharp.Pdf.IO.PdfReader;
using PdfSharpOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;

using Windows.Storage;

namespace pdfMerge.Services
{
    public class PdfService
    {
        /// <summary>
        /// Gets total page count of a PDF file using Windows.Data.Pdf.
        /// </summary>
        public async Task<int> GetPageCountAsync(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
                WinPdfDocument pdfDoc = await WinPdfDocument.LoadFromFileAsync(file);
                return (int)pdfDoc.PageCount;
            }
            catch
            {
                // Fallback to PdfSharp if needed
                using var stream = File.OpenRead(fullPath);
                using var doc = PdfSharpReader.Open(stream, PdfSharpOpenMode.Import);
                return doc.PageCount;
            }
        }

        /// <summary>
        /// Renders a specific page of a PDF file as a WPF BitmapImage thumbnail.
        /// </summary>
        public async Task<BitmapSource?> RenderPageThumbnailAsync(string filePath, int pageIndex, uint targetWidth = 350)
        {
            string fullPath = Path.GetFullPath(filePath);
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
                WinPdfDocument pdfDoc = await WinPdfDocument.LoadFromFileAsync(file);

                if (pageIndex < 0 || pageIndex >= pdfDoc.PageCount)
                    return null;

                using WinPdfPage page = pdfDoc.GetPage((uint)pageIndex);

                using var randomAccessStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var options = new WinPdfRenderOptions
                {
                    DestinationWidth = targetWidth
                };

                await page.RenderToStreamAsync(randomAccessStream, options);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = randomAccessStream.AsStream();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze(); // Make it cross-thread accessible

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error rendering PDF thumbnail for {fullPath} page {pageIndex}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Merges, rotates, and saves selected PDF pages into a new PDF document.
        /// </summary>
        public async Task MergeAndSavePdfAsync(IEnumerable<PdfPageItem> pageItems, string outputPath)
        {
            await Task.Run(() =>
            {
                using var outputDocument = new PdfSharpDocument();

                var sourceDocsCache = new Dictionary<string, PdfSharpDocument>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    foreach (var item in pageItems)
                    {
                        string fullSourcePath = Path.GetFullPath(item.SourceFilePath);
                        if (!sourceDocsCache.TryGetValue(fullSourcePath, out var sourceDoc))
                        {
                            sourceDoc = PdfSharpReader.Open(fullSourcePath, PdfSharpOpenMode.Import);
                            sourceDocsCache[fullSourcePath] = sourceDoc;
                        }

                        if (item.OriginalPageIndex >= 0 && item.OriginalPageIndex < sourceDoc.PageCount)
                        {
                            var page = outputDocument.AddPage(sourceDoc.Pages[item.OriginalPageIndex]);

                            if (item.Rotation != 0)
                            {
                                page.Rotate = (page.Rotate + item.Rotation) % 360;
                            }
                        }
                    }

                    string fullOutputPath = Path.GetFullPath(outputPath);
                    outputDocument.Save(fullOutputPath);
                }
                finally
                {
                    foreach (var doc in sourceDocsCache.Values)
                    {
                        doc.Dispose();
                    }
                }
            });
        }
    }
}
