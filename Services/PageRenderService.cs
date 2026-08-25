using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using pdfMerge.Helpers;
using pdfMerge.Models;

namespace pdfMerge.Services
{
    /// <summary>
    /// Unified rendering pipeline for thumbnails, preview, printing, and image export.
    /// Composites annotations, drawings, form fields, and signatures onto the unrotated base page,
    /// then rotates the composite page bitmap by item.Rotation.
    /// </summary>
    public static class PageRenderService
    {
        /// <summary>
        /// Renders a full composite page bitmap including all editor annotations, signatures, rotation, and optional grayscale conversion.
        /// </summary>
        public static async Task<BitmapSource?> RenderCompositePageAsync(PdfPageItem item, uint targetWidth, bool isMonochrome = false, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            BitmapSource? bitmap = await PdfService.RenderPageThumbnailAsync(item.SourceFilePath, item.OriginalPageIndex, targetWidth, token);
            if (bitmap == null && item.OriginalThumbnail != null)
            {
                bitmap = item.OriginalThumbnail;
            }

            token.ThrowIfCancellationRequested();

            if (bitmap != null)
            {
                // 1. Composite all editor annotations, drawings, form values, and signatures onto unrotated base page
                if (item.EditorData.HasEdits || item.PageSignatures.Count > 0)
                {
                    bitmap = BitmapUtilities.RenderCompositeThumbnail(bitmap, item.EditorData, item.PageSignatures);
                }

                // 2. Rotate the composite page bitmap by item.Rotation
                if (item.Rotation != 0)
                {
                    bitmap = BitmapUtilities.RotateBitmap(bitmap, item.Rotation);
                }

                if (isMonochrome)
                {
                    bitmap = BitmapUtilities.ConvertToGrayscale(bitmap);
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Synchronously renders a composite page for printing paginator.
        /// </summary>
        public static BitmapSource? RenderCompositePageSync(PdfPageItem item, uint targetWidth, bool isMonochrome = false)
        {
            BitmapSource? bitmap = Task.Run(() => PdfService.RenderPageThumbnailAsync(item.SourceFilePath, item.OriginalPageIndex, targetWidth)).GetAwaiter().GetResult();
            if (bitmap == null && item.OriginalThumbnail != null)
            {
                bitmap = item.OriginalThumbnail;
            }

            if (bitmap != null)
            {
                // 1. Composite all editor annotations, drawings, form values, and signatures onto unrotated base page
                if (item.EditorData.HasEdits || item.PageSignatures.Count > 0)
                {
                    bitmap = BitmapUtilities.RenderCompositeThumbnail(bitmap, item.EditorData, item.PageSignatures);
                }

                // 2. Rotate the composite page bitmap by item.Rotation
                if (item.Rotation != 0)
                {
                    bitmap = BitmapUtilities.RotateBitmap(bitmap, item.Rotation);
                }

                if (isMonochrome)
                {
                    bitmap = BitmapUtilities.ConvertToGrayscale(bitmap);
                }
            }

            return bitmap;
        }
    }
}
