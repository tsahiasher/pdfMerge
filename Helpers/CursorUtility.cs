using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace pdfMerge.Helpers
{
    /// <summary>
    /// Utility loading Open Hand and Closed Hand cursors from the project Cursors folder / assembly resources.
    /// </summary>
    public static class CursorUtility
    {
        private static Cursor? _openHand;
        private static Cursor? _closedHand;
        private static Cursor? _downwardPen;
        private static Cursor? _highlighterPen;

        public static Cursor OpenHand => _openHand ??= LoadCursorFromProject("openhand.cur", Cursors.Hand);
        public static Cursor ClosedHand => _closedHand ??= LoadCursorFromProject("closedhand.cur", Cursors.SizeAll);
        public static Cursor RotatedPen => _downwardPen ??= CreateDownwardPenCursor();
        public static Cursor HighlighterPen => _highlighterPen ??= CreateHighlighterPenCursor();

        public static Cursor CreateDownwardPenCursor()
        {
            try
            {
                int size = 32;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var outlineBrush = System.Windows.Media.Brushes.White;
                    var bodyBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E293B"));
                    var accentBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0284C7"));
                    var nibBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E2E8F0"));
                    var tipPen = new System.Windows.Media.Pen(System.Windows.Media.Brushes.Black, 1.2);
                    tipPen.Freeze();

                    // 1. Draw outer white outline for high contrast
                    var outlineGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = outlineGeom.Open())
                    {
                        ctx.BeginFigure(new Point(1, 30), true, true);
                        ctx.LineTo(new Point(0, 23), true, true);
                        ctx.LineTo(new Point(20, 3), true, true);
                        ctx.LineTo(new Point(28, 11), true, true);
                        ctx.LineTo(new Point(8, 31), true, true);
                    }
                    outlineGeom.Freeze();
                    dc.DrawGeometry(outlineBrush, new System.Windows.Media.Pen(outlineBrush, 2), outlineGeom);

                    // 2. Draw dark barrel
                    var bodyGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = bodyGeom.Open())
                    {
                        ctx.BeginFigure(new Point(7, 16), true, true);
                        ctx.LineTo(new Point(20, 3), true, true);
                        ctx.LineTo(new Point(28, 11), true, true);
                        ctx.LineTo(new Point(15, 24), true, true);
                    }
                    bodyGeom.Freeze();
                    dc.DrawGeometry(bodyBrush, null, bodyGeom);

                    // 3. Draw blue metallic grip collar
                    var collarGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = collarGeom.Open())
                    {
                        ctx.BeginFigure(new Point(5, 18), true, true);
                        ctx.LineTo(new Point(8, 15), true, true);
                        ctx.LineTo(new Point(16, 23), true, true);
                        ctx.LineTo(new Point(13, 26), true, true);
                    }
                    collarGeom.Freeze();
                    dc.DrawGeometry(accentBrush, null, collarGeom);

                    // 4. Draw silver nib cone pointing down
                    var nibGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = nibGeom.Open())
                    {
                        ctx.BeginFigure(new Point(1, 30), true, true);
                        ctx.LineTo(new Point(5, 18), true, true);
                        ctx.LineTo(new Point(13, 26), true, true);
                    }
                    nibGeom.Freeze();
                    dc.DrawGeometry(nibBrush, null, nibGeom);

                    // 5. Draw nib split slit
                    dc.DrawLine(tipPen, new Point(1, 30), new Point(7, 24));
                }
                rtb.Render(dv);

                return CreateCursorFromBitmap(rtb, 1, 30);
            }
            catch
            {
                return Cursors.Pen;
            }
        }

        public static Cursor CreateHighlighterPenCursor()
        {
            try
            {
                int size = 32;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    var outlineBrush = System.Windows.Media.Brushes.White;
                    var bodyBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FACC15"));
                    var darkCollarBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#334155"));
                    var chiselBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF08A"));
                    var edgePen = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#CA8A04")), 1.2);
                    edgePen.Freeze();

                    // 1. Draw outer white outline for high contrast
                    var outlineGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = outlineGeom.Open())
                    {
                        ctx.BeginFigure(new Point(1, 29), true, true);
                        ctx.LineTo(new Point(7, 29), true, true);
                        ctx.LineTo(new Point(28, 8), true, true);
                        ctx.LineTo(new Point(22, 2), true, true);
                        ctx.LineTo(new Point(1, 23), true, true);
                    }
                    outlineGeom.Freeze();
                    dc.DrawGeometry(outlineBrush, new System.Windows.Media.Pen(outlineBrush, 2), outlineGeom);

                    // 2. Draw fluorescent yellow marker body
                    var bodyGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = bodyGeom.Open())
                    {
                        ctx.BeginFigure(new Point(7, 17), true, true);
                        ctx.LineTo(new Point(22, 2), true, true);
                        ctx.LineTo(new Point(28, 8), true, true);
                        ctx.LineTo(new Point(13, 23), true, true);
                    }
                    bodyGeom.Freeze();
                    dc.DrawGeometry(bodyBrush, null, bodyGeom);

                    // 3. Draw dark collar band
                    var collarGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = collarGeom.Open())
                    {
                        ctx.BeginFigure(new Point(5, 19), true, true);
                        ctx.LineTo(new Point(8, 16), true, true);
                        ctx.LineTo(new Point(14, 22), true, true);
                        ctx.LineTo(new Point(11, 25), true, true);
                    }
                    collarGeom.Freeze();
                    dc.DrawGeometry(darkCollarBrush, null, collarGeom);

                    // 4. Draw chisel wedge nib
                    var chiselGeom = new System.Windows.Media.StreamGeometry();
                    using (var ctx = chiselGeom.Open())
                    {
                        ctx.BeginFigure(new Point(1, 29), true, true);
                        ctx.LineTo(new Point(7, 29), true, true);
                        ctx.LineTo(new Point(10, 24), true, true);
                        ctx.LineTo(new Point(4, 20), true, true);
                    }
                    chiselGeom.Freeze();
                    dc.DrawGeometry(chiselBrush, null, chiselGeom);

                    // 5. Draw chisel tip line
                    dc.DrawLine(edgePen, new Point(1, 29), new Point(7, 29));
                }
                rtb.Render(dv);

                return CreateCursorFromBitmap(rtb, 2, 29);
            }
            catch
            {
                return Cursors.Pen;
            }
        }

        private static Cursor CreateCursorFromBitmap(System.Windows.Media.Imaging.BitmapSource bmp, int hotX, int hotY)
        {
            var formattedBmp = new System.Windows.Media.Imaging.FormatConvertedBitmap(bmp, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int width = formattedBmp.PixelWidth;
            int height = formattedBmp.PixelHeight;
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formattedBmp.CopyPixels(pixels, stride, 0);

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // CUR Header
            bw.Write((short)0); // Reserved
            bw.Write((short)2); // Type 2 = CUR
            bw.Write((short)1); // 1 Image

            // Directory Entry
            bw.Write((byte)width);
            bw.Write((byte)height);
            bw.Write((byte)0); // Colors
            bw.Write((byte)0); // Reserved
            bw.Write((short)hotX);
            bw.Write((short)hotY);

            int imageSize = 40 + (width * height * 4) + (width * height / 8);
            bw.Write(imageSize);
            bw.Write(22); // Offset to image data

            // BITMAPINFOHEADER
            bw.Write(40); // biSize
            bw.Write(width); // biWidth
            bw.Write(height * 2); // biHeight (double for XOR + AND mask)
            bw.Write((short)1); // biPlanes
            bw.Write((short)32); // biBitCount
            bw.Write(0); // biCompression
            bw.Write(width * height * 4); // biSizeImage
            bw.Write(0); // biXPelsPerMeter
            bw.Write(0); // biYPelsPerMeter
            bw.Write(0); // biClrUsed
            bw.Write(0); // biClrImportant

            // Pixel Data (bottom-up)
            for (int y = height - 1; y >= 0; y--)
            {
                int rowStart = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int px = rowStart + (x * 4);
                    bw.Write(pixels[px]);     // B
                    bw.Write(pixels[px + 1]); // G
                    bw.Write(pixels[px + 2]); // R
                    bw.Write(pixels[px + 3]); // A
                }
            }

            // 1-bit AND mask
            byte[] andMask = new byte[width * height / 8];
            bw.Write(andMask);

            bw.Flush();
            ms.Position = 0;

            return new Cursor(ms);
        }

        private static Cursor LoadCursorFromProject(string fileName, Cursor fallback)
        {
            try
            {
                // 1. Try WPF Assembly Resource Stream (pack://application:,,,/Cursors/filename)
                var resourceUri = new Uri($"/Cursors/{fileName}", UriKind.Relative);
                var streamInfo = Application.GetResourceStream(resourceUri);
                if (streamInfo?.Stream != null)
                {
                    return new Cursor(streamInfo.Stream);
                }
            }
            catch { }

            try
            {
                // 2. Try Relative File Path (Cursors/filename)
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cursors", fileName);
                if (File.Exists(localPath))
                {
                    using var stream = File.OpenRead(localPath);
                    return new Cursor(stream);
                }

                string curDir = Path.Combine(Directory.GetCurrentDirectory(), "Cursors", fileName);
                if (File.Exists(curDir))
                {
                    using var stream = File.OpenRead(curDir);
                    return new Cursor(stream);
                }
            }
            catch { }

            return fallback;
        }
    }
}
