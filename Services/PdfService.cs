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

using PdfSharp.Drawing;
using Windows.Storage;

namespace pdfMerge.Services
{
    /// <summary>
    /// Stateless PDF service — all methods are static (Rec #14).
    /// </summary>
    public static class PdfService
    {
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff", ".webp"
        };

        public static bool IsSupportedImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            return ImageExtensions.Contains(ext);
        }

        public static bool IsSupportedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(ext)) return false;
            return string.Equals(ext, ".pdf", StringComparison.OrdinalIgnoreCase) || ImageExtensions.Contains(ext);
        }

        /// <summary>
        /// Gets total page count of a PDF or Image file.
        /// </summary>
        public static async Task<int> GetPageCountAsync(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);

            if (IsSupportedImageFile(fullPath))
            {
                return 1; // Images are loaded as 1-page documents
            }

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
                WinPdfDocument pdfDoc = await WinPdfDocument.LoadFromFileAsync(file);
                return (int)pdfDoc.PageCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WinRT PDF load failed for {fullPath}, falling back to PdfSharp: {ex.Message}");
                using var stream = File.OpenRead(fullPath);
                using var doc = PdfSharpReader.Open(stream, PdfSharpOpenMode.Import);
                return doc.PageCount;
            }
        }

        /// <summary>
        /// Renders a specific page of a PDF or Image as a WPF BitmapImage thumbnail.
        /// </summary>
        public static async Task<BitmapSource?> RenderPageThumbnailAsync(string filePath, int pageIndex, uint targetWidth = 350)
        {
            string fullPath = Path.GetFullPath(filePath);

            if (IsSupportedImageFile(fullPath))
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var stream = File.OpenRead(fullPath);
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.StreamSource = stream;
                        bitmap.DecodePixelWidth = (int)targetWidth;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return (BitmapSource)bitmap;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading image thumbnail for {fullPath}: {ex.Message}");
                        return null;
                    }
                });
            }

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
                bitmap.Freeze();

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error rendering PDF thumbnail for {fullPath} page {pageIndex}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Merges, rotates, and saves selected PDF and Image pages into a new PDF document.
        /// </summary>
        public static async Task MergeAndSavePdfAsync(IEnumerable<PdfPageItem> pageItems, string outputPath)
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

                        if (IsSupportedImageFile(fullSourcePath))
                        {
                            // Convert image to a high-resolution PDF page
                            using var ximg = XImage.FromFile(fullSourcePath);
                            var page = outputDocument.AddPage();
                            
                            // Set PDF page dimensions matching image aspect ratio
                            page.Width = XUnit.FromPoint(ximg.PointWidth);
                            page.Height = XUnit.FromPoint(ximg.PointHeight);

                            using var gfx = XGraphics.FromPdfPage(page);
                            gfx.DrawImage(ximg, 0, 0, page.Width.Point, page.Height.Point);

                            if (item.Rotation != 0)
                            {
                                page.Rotate = (page.Rotate + item.Rotation) % 360;
                            }

                            if (item.PageSignature != null)
                            {
                                DrawSignatureOntoPdfPage(page, item.PageSignature);
                            }
                        }
                        else
                        {
                            // Process PDF page
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

                                if (item.PageSignature != null)
                                {
                                    DrawSignatureOntoPdfPage(page, item.PageSignature);
                                }
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

        private static void DrawSignatureOntoPdfPage(PdfSharpPage page, AppliedSignature sig)
        {
            try
            {
                using var sigStream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(sig.SignatureImage));
                encoder.Save(sigStream);
                sigStream.Position = 0;

                using var sigXImg = XImage.FromStream(sigStream);

                double sigX = page.Width.Point * sig.RelX;
                double sigY = page.Height.Point * sig.RelY;
                double sigW = page.Width.Point * sig.RelWidth;
                double sigH = page.Height.Point * sig.RelHeight;

                using var sigGfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                sigGfx.DrawImage(sigXImg, sigX, sigY, sigW, sigH);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error drawing signature onto PDF page: {ex.Message}");
            }
        }
    }
}
