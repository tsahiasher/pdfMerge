using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        public static async Task<int> GetPageCountAsync(string filePath, CancellationToken token = default)
        {
            string fullPath = Path.GetFullPath(filePath);

            if (IsSupportedImageFile(fullPath))
            {
                return 1; // Images are loaded as 1-page documents
            }

            token.ThrowIfCancellationRequested();

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
                var pdfDoc = await WinPdfDocument.LoadFromFileAsync(file);
                token.ThrowIfCancellationRequested();
                return (int)pdfDoc.PageCount;
            }
            catch (OperationCanceledException) { throw; }
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
        public static async Task<BitmapSource?> RenderPageThumbnailAsync(string filePath, int pageIndex, uint targetWidth = 350, CancellationToken token = default)
        {
            string fullPath = Path.GetFullPath(filePath);

            if (IsSupportedImageFile(fullPath))
            {
                token.ThrowIfCancellationRequested();
                return await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
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
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading image thumbnail for {fullPath}: {ex.Message}");
                        return null;
                    }
                }, token);
            }

            token.ThrowIfCancellationRequested();

            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
                var pdfDoc = await WinPdfDocument.LoadFromFileAsync(file);

                if (pageIndex < 0 || pageIndex >= pdfDoc.PageCount)
                    return null;

                token.ThrowIfCancellationRequested();

                using WinPdfPage page = pdfDoc.GetPage((uint)pageIndex);

                using var randomAccessStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var options = new WinPdfRenderOptions
                {
                    DestinationWidth = targetWidth
                };

                await page.RenderToStreamAsync(randomAccessStream, options);

                token.ThrowIfCancellationRequested();

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = randomAccessStream.AsStream();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error rendering PDF thumbnail for {fullPath} page {pageIndex}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Checks whether any of the source PDF files in the page list contain PDF bookmarks (outlines).
        /// </summary>
        public static bool HasBookmarks(IEnumerable<PdfPageItem> pageItems)
        {
            var pdfPaths = pageItems
                .Select(p => Path.GetFullPath(p.SourceFilePath))
                .Where(p => !IsSupportedImageFile(p))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var filePath in pdfPaths)
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        using var stream = File.OpenRead(filePath);
                        using var doc = PdfSharpReader.Open(stream, PdfSharpOpenMode.Import);
                        if (doc.Outlines != null && doc.Outlines.Count > 0)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error checking bookmarks in '{filePath}': {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Merges, rotates, and saves selected PDF and Image pages into a new PDF document, optionally recreating bookmarks.
        /// </summary>
        public static async Task MergeAndSavePdfAsync(IEnumerable<PdfPageItem> pageItems, string outputPath, bool recreateBookmarks = false)
        {
            var pageList = pageItems.ToList();
            await Task.Run(() =>
            {
                using var outputDocument = new PdfSharpDocument();
                var sourceDocsCache = new Dictionary<string, PdfSharpDocument>(StringComparer.OrdinalIgnoreCase);
                var pageMap = new Dictionary<(string FilePath, int PageIndex), PdfSharpPage>();

                try
                {
                    foreach (var item in pageList)
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

                            pageMap[(fullSourcePath, item.OriginalPageIndex)] = page;
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

                                pageMap[(fullSourcePath, item.OriginalPageIndex)] = page;
                            }
                        }
                    }

                    if (recreateBookmarks)
                    {
                        RecreateBookmarks(outputDocument, sourceDocsCache, pageMap);
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

        private class DestInfo
        {
            public int PageIndex { get; set; } = -1;
            public string FitType { get; set; } = "/XYZ";
            public double? Left { get; set; }
            public double? Bottom { get; set; }
            public double? Right { get; set; }
            public double? Top { get; set; }
            public double? Zoom { get; set; }
        }

        private class BookmarkItem
        {
            public string Title { get; set; } = string.Empty;
            public string SourceFilePath { get; set; } = string.Empty;
            public int OriginalPageIndex { get; set; } = -1;
            public string FitType { get; set; } = "/XYZ";
            public double? Left { get; set; }
            public double? Bottom { get; set; }
            public double? Right { get; set; }
            public double? Top { get; set; }
            public double? Zoom { get; set; }
            public List<BookmarkItem> Children { get; set; } = new();
        }

        private static void RecreateBookmarks(
            PdfSharpDocument outputDocument,
            Dictionary<string, PdfSharpDocument> sourceDocsCache,
            Dictionary<(string FilePath, int PageIndex), PdfSharpPage> pageMap)
        {
            try
            {
                var rawBookmarks = new List<BookmarkItem>();

                foreach (var kvp in sourceDocsCache)
                {
                    string filePath = kvp.Key;
                    var doc = kvp.Value;

                    if (doc.Outlines == null || doc.Outlines.Count == 0)
                        continue;

                    foreach (PdfSharp.Pdf.PdfOutline outline in doc.Outlines)
                    {
                        var item = ExtractBookmarkItem(outline, doc, filePath);
                        if (item != null)
                            rawBookmarks.Add(item);
                    }
                }

                if (rawBookmarks.Count == 0)
                    return;

                var flatBookmarks = new List<BookmarkItem>();

                void Flatten(List<BookmarkItem> list)
                {
                    foreach (var bookmark in list)
                    {
                        flatBookmarks.Add(bookmark);
                        if (bookmark.Children.Count > 0)
                            Flatten(bookmark.Children);
                    }
                }

                Flatten(rawBookmarks);

                var sectionOutlinesDict = new Dictionary<string, PdfSharp.Pdf.PdfOutline>(StringComparer.OrdinalIgnoreCase);

                foreach (var bm in flatBookmarks)
                {
                    if (bm.OriginalPageIndex < 0)
                        continue;

                    var key = (bm.SourceFilePath, bm.OriginalPageIndex);
                    if (!pageMap.TryGetValue(key, out var destPage) || destPage == null)
                        continue;

                    string title = bm.Title.Trim();
                    if (string.IsNullOrEmpty(title))
                        continue;

                    PdfSharp.Pdf.PdfOutline createdOutline;
                    var match = System.Text.RegularExpressions.Regex.Match(title, @"^(\d+(?:\.\d+)*)");

                    if (match.Success)
                    {
                        string secNum = match.Value;
                        string parentSecNum = GetParentSectionNumber(secNum);

                        if (!string.IsNullOrEmpty(parentSecNum) &&
                            sectionOutlinesDict.TryGetValue(parentSecNum, out var parentOutline))
                        {
                            createdOutline = parentOutline.Outlines.Add(title, destPage, true);
                        }
                        else
                        {
                            createdOutline = outputDocument.Outlines.Add(title, destPage, true);
                        }

                        sectionOutlinesDict[secNum] = createdOutline;
                    }
                    else
                    {
                        createdOutline = outputDocument.Outlines.Add(title, destPage, true);
                    }

                    try
                    {
                        var destArray = BuildDestinationArray(outputDocument, destPage, bm);
                        if (destArray == null)
                            continue;

                        createdOutline.Elements.Remove("/A");
                        createdOutline.Elements["/Dest"] = destArray;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error setting bookmark destination for '{title}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error recreating bookmarks: {ex.Message}");
            }
        }

        private static PdfSharp.Pdf.PdfArray? BuildDestinationArray(
            PdfSharpDocument outputDocument,
            PdfSharpPage destPage,
            BookmarkItem bookmark)
        {
            if (destPage.Reference == null)
                return null;

            var destArray = new PdfSharp.Pdf.PdfArray(outputDocument);
            destArray.Elements.Add(destPage.Reference);

            string fitType = NormalizeFitType(bookmark.FitType);

            switch (fitType.ToUpperInvariant())
            {
                case "/FIT":
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/Fit"));
                    break;

                case "/FITB":
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/FitB"));
                    break;

                case "/FITH":
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/FitH"));
                    AddNullableNumber(destArray, bookmark.Top);
                    break;

                case "/FITBH":
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/FitBH"));
                    AddNullableNumber(destArray, bookmark.Top);
                    break;

                case "/FITV":
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/FitV"));
                    AddNullableNumber(destArray, bookmark.Left);
                    break;

                case "/FITBV":
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/FitBV"));
                    AddNullableNumber(destArray, bookmark.Left);
                    break;

                case "/FITR":
                    if (bookmark.Left.HasValue &&
                        bookmark.Bottom.HasValue &&
                        bookmark.Right.HasValue &&
                        bookmark.Top.HasValue)
                    {
                        destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/FitR"));
                        destArray.Elements.Add(new PdfSharp.Pdf.PdfReal(bookmark.Left.Value));
                        destArray.Elements.Add(new PdfSharp.Pdf.PdfReal(bookmark.Bottom.Value));
                        destArray.Elements.Add(new PdfSharp.Pdf.PdfReal(bookmark.Right.Value));
                        destArray.Elements.Add(new PdfSharp.Pdf.PdfReal(bookmark.Top.Value));
                    }
                    else
                    {
                        destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/XYZ"));
                        AddNullableNumber(destArray, bookmark.Left);
                        AddNullableNumber(destArray, bookmark.Top);
                        AddNullableNumber(destArray, bookmark.Zoom);
                    }
                    break;

                case "/XYZ":
                default:
                    destArray.Elements.Add(new PdfSharp.Pdf.PdfName("/XYZ"));
                    AddNullableNumber(destArray, bookmark.Left);
                    AddNullableNumber(destArray, bookmark.Top);
                    AddNullableNumber(destArray, bookmark.Zoom);
                    break;
            }

            return destArray;
        }

        private static void AddNullableNumber(PdfSharp.Pdf.PdfArray array, double? value)
        {
            if (value.HasValue)
                array.Elements.Add(new PdfSharp.Pdf.PdfReal(value.Value));
            else
                array.Elements.Add(PdfSharp.Pdf.PdfNull.Value);
        }

        private static string NormalizeFitType(string? fitType)
        {
            if (string.IsNullOrWhiteSpace(fitType))
                return "/XYZ";

            string value = fitType.Trim();
            if (!value.StartsWith("/", StringComparison.Ordinal))
                value = "/" + value;

            return value;
        }

        private static string GetParentSectionNumber(string secNum)
        {
            int lastDot = secNum.LastIndexOf('.');
            if (lastDot > 0)
                return secNum.Substring(0, lastDot);

            return string.Empty;
        }

        private static int GetPageIndexInDoc(PdfSharpDocument doc, PdfSharpPage? targetPage)
        {
            if (targetPage == null)
                return -1;

            for (int i = 0; i < doc.Pages.Count; i++)
            {
                if (doc.Pages[i] == targetPage)
                    return i;
            }

            return -1;
        }

        private static DestInfo GetOutlineDestInfo(PdfSharp.Pdf.PdfOutline outline, PdfSharpDocument doc)
        {
            var info = new DestInfo();

            try
            {
                PdfSharp.Pdf.PdfItem? destObj = null;

                if (outline.Elements.ContainsKey("/Dest"))
                {
                    destObj = outline.Elements["/Dest"];
                }
                else if (outline.Elements.ContainsKey("/A"))
                {
                    var actionObj = Dereference(outline.Elements["/A"]);
                    if (actionObj is PdfSharp.Pdf.PdfDictionary actionDict &&
                        actionDict.Elements.ContainsKey("/D"))
                    {
                        destObj = actionDict.Elements["/D"];
                    }
                }

                if (destObj != null)
                    ExtractDestInfoFromObject(destObj, doc, info);

                if (info.PageIndex < 0 && outline.DestinationPage != null)
                    info.PageIndex = GetPageIndexInDoc(doc, outline.DestinationPage);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resolving outline destination info: {ex.Message}");
            }

            return info;
        }

        private static void ExtractDestInfoFromObject(
            PdfSharp.Pdf.PdfItem? destObj,
            PdfSharpDocument doc,
            DestInfo info)
        {
            if (destObj == null)
                return;

            destObj = Dereference(destObj);

            if (destObj is PdfSharp.Pdf.PdfString pStr)
            {
                destObj = ResolveNamedDestObject(pStr.Value, doc);
            }
            else if (destObj is PdfSharp.Pdf.PdfName pName)
            {
                destObj = ResolveNamedDestObject(pName.Value, doc);
            }

            destObj = Dereference(destObj);

            if (destObj is PdfSharp.Pdf.PdfDictionary destDict && destDict.Elements.ContainsKey("/D"))
            {
                destObj = Dereference(destDict.Elements["/D"]);
            }

            if (destObj is PdfSharp.Pdf.PdfArray arr && arr.Elements.Count > 0)
            {
                ResolveDestinationPage(arr.Elements[0], doc, info);

                if (arr.Elements.Count > 1)
                {
                    string fitName = arr.Elements[1]?.ToString() ?? string.Empty;
                    info.FitType = NormalizeFitType(fitName);
                }

                switch (info.FitType.ToUpperInvariant())
                {
                    case "/XYZ":
                        if (arr.Elements.Count > 2) info.Left = ParseDoubleValue(arr.Elements[2]);
                        if (arr.Elements.Count > 3) info.Top = ParseDoubleValue(arr.Elements[3]);
                        if (arr.Elements.Count > 4) info.Zoom = ParseDoubleValue(arr.Elements[4]);
                        break;

                    case "/FITH":
                    case "/FITBH":
                        if (arr.Elements.Count > 2) info.Top = ParseDoubleValue(arr.Elements[2]);
                        break;

                    case "/FITV":
                    case "/FITBV":
                        if (arr.Elements.Count > 2) info.Left = ParseDoubleValue(arr.Elements[2]);
                        break;

                    case "/FITR":
                        if (arr.Elements.Count > 2) info.Left = ParseDoubleValue(arr.Elements[2]);
                        if (arr.Elements.Count > 3) info.Bottom = ParseDoubleValue(arr.Elements[3]);
                        if (arr.Elements.Count > 4) info.Right = ParseDoubleValue(arr.Elements[4]);
                        if (arr.Elements.Count > 5) info.Top = ParseDoubleValue(arr.Elements[5]);
                        break;
                }
            }
            else if (destObj is PdfSharp.Pdf.PdfInteger pInt)
            {
                int idx = pInt.Value;
                if (idx >= 0 && idx < doc.Pages.Count)
                    info.PageIndex = idx;
            }
        }

        private static void ResolveDestinationPage(
            PdfSharp.Pdf.PdfItem pageItem,
            PdfSharpDocument doc,
            DestInfo info)
        {
            var resolved = Dereference(pageItem);

            if (pageItem is PdfSharp.Pdf.Advanced.PdfReference pageRef)
            {
                for (int i = 0; i < doc.Pages.Count; i++)
                {
                    if (doc.Pages[i].Reference == pageRef || doc.Pages[i] == pageRef.Value)
                    {
                        info.PageIndex = i;
                        return;
                    }
                }
            }

            if (resolved is PdfSharp.Pdf.PdfPage page)
            {
                info.PageIndex = GetPageIndexInDoc(doc, page);
                return;
            }

            if (resolved is PdfSharp.Pdf.PdfInteger pageNum)
            {
                int idx = pageNum.Value;
                if (idx >= 0 && idx < doc.Pages.Count)
                    info.PageIndex = idx;
            }
        }

        private static PdfSharp.Pdf.PdfItem? Dereference(PdfSharp.Pdf.PdfItem? item)
        {
            if (item is PdfSharp.Pdf.Advanced.PdfReference reference)
                return reference.Value;

            return item;
        }

        private static double? ParseDoubleValue(PdfSharp.Pdf.PdfItem item)
        {
            if (item is PdfSharp.Pdf.PdfReal real)
                return real.Value;

            if (item is PdfSharp.Pdf.PdfInteger integer)
                return integer.Value;

            return null;
        }

        private static PdfSharp.Pdf.PdfItem? ResolveNamedDestObject(string name, PdfSharpDocument doc)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            try
            {
                string normalizedName = name.Trim('/', '(', ')');
                var catalog = doc.Internals.Catalog;

                if (catalog != null && catalog.Elements.ContainsKey("/Dests"))
                {
                    var destsObj = Dereference(catalog.Elements["/Dests"]);
                    if (destsObj is PdfSharp.Pdf.PdfDictionary destsDict)
                    {
                        string slashKey = "/" + normalizedName;

                        if (destsDict.Elements.ContainsKey(slashKey))
                            return destsDict.Elements[slashKey];

                        if (destsDict.Elements.ContainsKey(normalizedName))
                            return destsDict.Elements[normalizedName];
                    }
                }

                if (catalog != null && catalog.Elements.ContainsKey("/Names"))
                {
                    var namesObj = Dereference(catalog.Elements["/Names"]);
                    if (namesObj is PdfSharp.Pdf.PdfDictionary namesDict &&
                        namesDict.Elements.ContainsKey("/Dests"))
                    {
                        var destsTreeObj = Dereference(namesDict.Elements["/Dests"]);
                        var result = FindNamedDestinationInNameTree(destsTreeObj, normalizedName);
                        if (result != null)
                            return result;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resolving named destination '{name}': {ex.Message}");
            }

            return null;
        }

        private static PdfSharp.Pdf.PdfItem? FindNamedDestinationInNameTree(
            PdfSharp.Pdf.PdfItem? treeItem,
            string targetName)
        {
            treeItem = Dereference(treeItem);
            if (treeItem is not PdfSharp.Pdf.PdfDictionary tree)
                return null;

            if (tree.Elements.ContainsKey("/Names"))
            {
                var namesObj = Dereference(tree.Elements["/Names"]);
                if (namesObj is PdfSharp.Pdf.PdfArray namesArray)
                {
                    for (int i = 0; i + 1 < namesArray.Elements.Count; i += 2)
                    {
                        string key = GetDestinationName(namesArray.Elements[i]);
                        if (string.Equals(key, targetName, StringComparison.Ordinal))
                            return namesArray.Elements[i + 1];
                    }
                }
            }

            if (tree.Elements.ContainsKey("/Kids"))
            {
                var kidsObj = Dereference(tree.Elements["/Kids"]);
                if (kidsObj is PdfSharp.Pdf.PdfArray kidsArray)
                {
                    foreach (var kid in kidsArray.Elements)
                    {
                        var result = FindNamedDestinationInNameTree(kid, targetName);
                        if (result != null)
                            return result;
                    }
                }
            }

            return null;
        }

        private static string GetDestinationName(PdfSharp.Pdf.PdfItem item)
        {
            var resolved = Dereference(item);

            if (resolved is PdfSharp.Pdf.PdfString str)
                return str.Value;

            if (resolved is PdfSharp.Pdf.PdfName name)
                return name.Value.TrimStart('/');

            return resolved?.ToString()?.Trim('/', '(', ')') ?? string.Empty;
        }

        private static BookmarkItem? ExtractBookmarkItem(
            PdfSharp.Pdf.PdfOutline outline,
            PdfSharpDocument doc,
            string filePath)
        {
            var destInfo = GetOutlineDestInfo(outline, doc);

            var item = new BookmarkItem
            {
                Title = outline.Title ?? string.Empty,
                SourceFilePath = filePath,
                OriginalPageIndex = destInfo.PageIndex,
                FitType = destInfo.FitType,
                Left = destInfo.Left,
                Bottom = destInfo.Bottom,
                Right = destInfo.Right,
                Top = destInfo.Top,
                Zoom = destInfo.Zoom
            };

            if (outline.Outlines != null && outline.Outlines.Count > 0)
            {
                foreach (PdfSharp.Pdf.PdfOutline child in outline.Outlines)
                {
                    var childItem = ExtractBookmarkItem(child, doc, filePath);
                    if (childItem != null)
                        item.Children.Add(childItem);
                }
            }

            return item;
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
