using System.Collections.Generic;
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
            return RenderSignatureOverlayOnThumbnail(baseThumb, new[] { sig });
        }

        public static BitmapSource RenderSignatureOverlayOnThumbnail(BitmapSource baseThumb, IEnumerable<AppliedSignature> signatures)
        {
            int width = baseThumb.PixelWidth;
            int height = baseThumb.PixelHeight;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(baseThumb, new Rect(0, 0, width, height));

                foreach (var sig in signatures)
                {
                    double relX = System.Math.Clamp(sig.RelX, 0, 0.99);
                    double relY = System.Math.Clamp(sig.RelY, 0, 0.99);
                    double relW = System.Math.Clamp(sig.RelWidth, 0.01, 1.0 - relX);
                    double relH = System.Math.Clamp(sig.RelHeight, 0.01, 1.0 - relY);

                    double sigX = width * relX;
                    double sigY = height * relY;
                    double sigW = width * relW;
                    double sigH = height * relH;

                    dc.DrawImage(sig.SignatureImage, new Rect(sigX, sigY, sigW, sigH));
                }
            }

            // Always render at standard 96 DPI to maintain exact 1:1 Pixel-to-DIP mapping without DPI scaling distortion
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
