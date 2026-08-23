using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pdfMerge.Helpers;
using pdfMerge.Services;

namespace pdfMerge.Models
{
    /// <summary>
    /// DocumentPaginator for printing PDF document pages on-demand without holding all bitmaps in memory.
    /// </summary>
    public class PdfDocumentPaginator : DocumentPaginator
    {
        private readonly List<PdfPageItem> _pages;
        private Size _pageSize;
        private readonly bool _isMonochrome;

        public PdfDocumentPaginator(List<PdfPageItem> pages, Size pageSize, bool isMonochrome)
        {
            _pages = pages ?? new List<PdfPageItem>();
            _pageSize = pageSize;
            _isMonochrome = isMonochrome;
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pages.Count)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));

            var pageItem = _pages[pageNumber];

            // Render page on demand using unified rendering pipeline at 300 DPI print quality
            uint targetPrintWidth = (uint)Math.Clamp(_pageSize.Width * 3.125, 2400, 4800);
            BitmapSource? bitmap = PageRenderService.RenderCompositePageSync(pageItem, targetPrintWidth, _isMonochrome);

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                if (bitmap != null)
                {
                    double scale = Math.Min(_pageSize.Width / bitmap.PixelWidth, _pageSize.Height / bitmap.PixelHeight);
                    double renderWidth = bitmap.PixelWidth * scale;
                    double renderHeight = bitmap.PixelHeight * scale;

                    double offsetX = (_pageSize.Width - renderWidth) / 2.0;
                    double offsetY = (_pageSize.Height - renderHeight) / 2.0;

                    dc.DrawImage(bitmap, new Rect(offsetX, offsetY, renderWidth, renderHeight));
                }
            }

            return new DocumentPage(visual, _pageSize, new Rect(0, 0, _pageSize.Width, _pageSize.Height), new Rect(0, 0, _pageSize.Width, _pageSize.Height));
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _pages.Count;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override IDocumentPaginatorSource? Source => null;
    }
}
