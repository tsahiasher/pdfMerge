using System;
using System.Windows;

namespace pdfMerge.Services
{
    /// <summary>
    /// Unified coordinate transformation engine for the PDF Page Editor.
    /// Handles bidirectional mappings across:
    /// 1. Unrotated Normalized Page Space [0.0..1.0] (top-left origin).
    /// 2. Rotated / Displayed Normalized Page Space [0.0..1.0] (for 0°, 90°, 180°, 270°).
    /// 3. Zoomed Screen Workspace Canvas Pixels.
    /// 4. PDF User Unit Points (bottom-left origin).
    /// </summary>
    public static class EditorCoordinateService
    {
        public static int NormalizeRotation(int rotation)
        {
            return ((rotation % 360) + 360) % 360;
        }

        #region Base [0..1] <-> Rotated [0..1] Point Transformations

        public static Point BaseToRotatedPoint(Point p, int rotation)
        {
            int rot = NormalizeRotation(rotation);
            return rot switch
            {
                90 => new Point(1.0 - p.Y, p.X),
                180 => new Point(1.0 - p.X, 1.0 - p.Y),
                270 => new Point(p.Y, 1.0 - p.X),
                _ => new Point(p.X, p.Y)
            };
        }

        public static Point RotatedToBasePoint(Point p, int rotation)
        {
            int rot = NormalizeRotation(rotation);
            return rot switch
            {
                90 => new Point(p.Y, 1.0 - p.X),
                180 => new Point(1.0 - p.X, 1.0 - p.Y),
                270 => new Point(1.0 - p.Y, p.X),
                _ => new Point(p.X, p.Y)
            };
        }

        #endregion

        #region Base [0..1] <-> Rotated [0..1] Rect Transformations

        public static Rect BaseToRotatedRect(Rect r, int rotation)
        {
            int rot = NormalizeRotation(rotation);
            return rot switch
            {
                90 => new Rect(1.0 - r.Y - r.Height, r.X, r.Height, r.Width),
                180 => new Rect(1.0 - r.X - r.Width, 1.0 - r.Y - r.Height, r.Width, r.Height),
                270 => new Rect(r.Y, 1.0 - r.X - r.Width, r.Height, r.Width),
                _ => new Rect(r.X, r.Y, r.Width, r.Height)
            };
        }

        public static Rect RotatedToBaseRect(Rect r, int rotation)
        {
            int rot = NormalizeRotation(rotation);
            return rot switch
            {
                90 => new Rect(r.Y, 1.0 - r.X - r.Width, r.Height, r.Width),
                180 => new Rect(1.0 - r.X - r.Width, 1.0 - r.Y - r.Height, r.Width, r.Height),
                270 => new Rect(1.0 - r.Y - r.Height, r.X, r.Height, r.Width),
                _ => new Rect(r.X, r.Y, r.Width, r.Height)
            };
        }

        #endregion

        #region Screen Pixels <-> Normalized Base [0..1]

        public static Point ScreenToBasePoint(Point screenPoint, double canvasWidth, double canvasHeight, int rotation)
        {
            if (canvasWidth <= 0) canvasWidth = 1;
            if (canvasHeight <= 0) canvasHeight = 1;

            double rx = Math.Clamp(screenPoint.X / canvasWidth, 0.0, 1.0);
            double ry = Math.Clamp(screenPoint.Y / canvasHeight, 0.0, 1.0);

            return RotatedToBasePoint(new Point(rx, ry), rotation);
        }

        public static Point BaseToScreenPoint(Point basePoint, double canvasWidth, double canvasHeight, int rotation)
        {
            Point rotP = BaseToRotatedPoint(basePoint, rotation);
            return new Point(rotP.X * canvasWidth, rotP.Y * canvasHeight);
        }

        public static Rect ScreenToBaseRect(Rect screenRect, double canvasWidth, double canvasHeight, int rotation)
        {
            if (canvasWidth <= 0) canvasWidth = 1;
            if (canvasHeight <= 0) canvasHeight = 1;

            double rx = Math.Clamp(screenRect.X / canvasWidth, 0.0, 1.0);
            double ry = Math.Clamp(screenRect.Y / canvasHeight, 0.0, 1.0);
            double rw = Math.Clamp(screenRect.Width / canvasWidth, 0.0, 1.0 - rx);
            double rh = Math.Clamp(screenRect.Height / canvasHeight, 0.0, 1.0 - ry);

            return RotatedToBaseRect(new Rect(rx, ry, rw, rh), rotation);
        }

        public static Rect BaseToScreenRect(Rect baseRect, double canvasWidth, double canvasHeight, int rotation)
        {
            Rect rotR = BaseToRotatedRect(baseRect, rotation);
            return new Rect(
                rotR.X * canvasWidth,
                rotR.Y * canvasHeight,
                rotR.Width * canvasWidth,
                rotR.Height * canvasHeight
            );
        }

        #endregion

        #region PDF User Unit Points <-> Normalized Base [0..1]

        public static Rect PdfRectToNormalizedBaseRect(double llx, double lly, double urx, double ury, double pdfWidthPoints, double pdfHeightPoints)
        {
            if (pdfWidthPoints <= 0) pdfWidthPoints = 1;
            if (pdfHeightPoints <= 0) pdfHeightPoints = 1;

            double minX = Math.Min(llx, urx);
            double maxX = Math.Max(llx, urx);
            double minY = Math.Min(lly, ury);
            double maxY = Math.Max(lly, ury);

            double relX = minX / pdfWidthPoints;
            double relY = (pdfHeightPoints - maxY) / pdfHeightPoints;
            double relW = (maxX - minX) / pdfWidthPoints;
            double relH = (maxY - minY) / pdfHeightPoints;

            return new Rect(
                Math.Clamp(relX, 0.0, 1.0),
                Math.Clamp(relY, 0.0, 1.0),
                Math.Clamp(relW, 0.001, 1.0),
                Math.Clamp(relH, 0.001, 1.0)
            );
        }

        public static (double llx, double lly, double urx, double ury) NormalizedBaseRectToPdfRect(Rect baseRect, double pdfWidthPoints, double pdfHeightPoints)
        {
            double llx = baseRect.X * pdfWidthPoints;
            double urx = (baseRect.X + baseRect.Width) * pdfWidthPoints;
            double lly = (1.0 - baseRect.Y - baseRect.Height) * pdfHeightPoints;
            double ury = (1.0 - baseRect.Y) * pdfHeightPoints;

            return (llx, lly, urx, ury);
        }

        #endregion
    }
}
