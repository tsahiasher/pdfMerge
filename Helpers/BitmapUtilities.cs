using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pdfMerge.Models;

namespace pdfMerge.Helpers
{
    public static class BitmapUtilities
    {
        public static BitmapSource RotateBitmap(BitmapSource source, int angle)
        {
            var transformed = new TransformedBitmap(source, new RotateTransform(angle));
            transformed.Freeze();
            return transformed;
        }

        public static BitmapSource ConvertToGrayscale(BitmapSource source)
        {
            var grayBitmap = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            grayBitmap.Freeze();
            return grayBitmap;
        }

        public static BitmapSource RenderSignatureOverlayOnThumbnail(BitmapSource baseThumb, AppliedSignature sig)
        {
            int width = baseThumb.PixelWidth;
            int height = baseThumb.PixelHeight;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(baseThumb, new Rect(0, 0, width, height));

                double sigX = width * sig.RelX;
                double sigY = height * sig.RelY;
                double sigW = width * sig.RelWidth;
                double sigH = height * sig.RelHeight;

                dc.DrawImage(sig.SignatureImage, new Rect(sigX, sigY, sigW, sigH));
            }

            var rtb = new RenderTargetBitmap(width, height, baseThumb.DpiX, baseThumb.DpiY, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
