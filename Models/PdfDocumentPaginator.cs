using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace pdfMerge.Models
{
    /// <summary>
    /// DocumentPaginator for printing PDF document pages with native PrintDialog options.
    /// </summary>
    public class PdfDocumentPaginator : DocumentPaginator
    {
        private readonly List<BitmapSource> _pageBitmaps;
        private Size _pageSize;

        public PdfDocumentPaginator(List<BitmapSource> pageBitmaps, Size pageSize)
        {
            _pageBitmaps = pageBitmaps;
            _pageSize = pageSize;
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageBitmaps.Count)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));

            var bitmap = _pageBitmaps[pageNumber];

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double scale = Math.Min(_pageSize.Width / bitmap.PixelWidth, _pageSize.Height / bitmap.PixelHeight);
                double renderWidth = bitmap.PixelWidth * scale;
                double renderHeight = bitmap.PixelHeight * scale;

                double offsetX = (_pageSize.Width - renderWidth) / 2.0;
                double offsetY = (_pageSize.Height - renderHeight) / 2.0;

                dc.DrawImage(bitmap, new Rect(offsetX, offsetY, renderWidth, renderHeight));
            }

            return new DocumentPage(visual, _pageSize, new Rect(0, 0, _pageSize.Width, _pageSize.Height), new Rect(0, 0, _pageSize.Width, _pageSize.Height));
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _pageBitmaps.Count;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override IDocumentPaginatorSource? Source => null;
    }
}
