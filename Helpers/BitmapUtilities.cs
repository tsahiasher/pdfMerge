using System;
using System.Collections.Generic;
using System.Globalization;
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
            int normalized = ((angle % 360) + 360) % 360;
            if (normalized == 0) return source;

            var transformed = new TransformedBitmap(source, new RotateTransform(normalized));
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
            return RenderCompositeThumbnail(baseThumb, new PageEditorData(), signatures);
        }

        public static BitmapSource RenderCompositeThumbnail(BitmapSource baseThumb, PageEditorData editorData, IEnumerable<AppliedSignature>? extraSignatures = null)
        {
            int width = baseThumb.PixelWidth;
            int height = baseThumb.PixelHeight;

            if (width <= 0) width = 350;
            if (height <= 0) height = 500;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                // 1. Draw base page
                dc.DrawImage(baseThumb, new Rect(0, 0, width, height));

                // 2. Draw Text Highlights
                if (editorData.TextHighlights.Count > 0)
                {
                    var hlBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15")) { Opacity = 0.40 };
                    hlBrush.Freeze();

                    foreach (var th in editorData.TextHighlights)
                    {
                        foreach (var lr in th.LineRects)
                        {
                            Rect screenRect = new Rect(lr.X * width, lr.Y * height, lr.Width * width, lr.Height * height);
                            dc.DrawRectangle(hlBrush, null, screenRect);
                        }
                    }
                }

                // 3. Draw Freehand Highlights
                if (editorData.FreehandHighlights.Count > 0)
                {
                    foreach (var fh in editorData.FreehandHighlights)
                    {
                        if (fh.Points.Count < 2) continue;

                        Color hlColor;
                        try { hlColor = (Color)ColorConverter.ConvertFromString(fh.ColorHex); }
                        catch { hlColor = (Color)ColorConverter.ConvertFromString("#FACC15"); }
                        hlColor.A = (byte)(255 * (fh.Opacity > 0 ? fh.Opacity : 0.38));

                        double strokePx = Math.Max(2, (fh.DisplayPixelThickness > 0 ? fh.DisplayPixelThickness : 20) * ((double)width / 600.0));
                        var pen = new Pen(new SolidColorBrush(hlColor), strokePx)
                        {
                            StartLineCap = PenLineCap.Round,
                            EndLineCap = PenLineCap.Round,
                            LineJoin = PenLineJoin.Round
                        };
                        pen.Freeze();

                        var streamGeom = new StreamGeometry();
                        using (var ctx = streamGeom.Open())
                        {
                            ctx.BeginFigure(new Point(fh.Points[0].X * width, fh.Points[0].Y * height), false, false);
                            for (int i = 1; i < fh.Points.Count; i++)
                            {
                                ctx.LineTo(new Point(fh.Points[i].X * width, fh.Points[i].Y * height), true, true);
                            }
                        }
                        streamGeom.Freeze();
                        dc.DrawGeometry(null, pen, streamGeom);
                    }
                }

                // 4. Draw Pen Strokes
                if (editorData.PenStrokes.Count > 0)
                {
                    foreach (var stroke in editorData.PenStrokes)
                    {
                        if (stroke.Points.Count < 2) continue;

                        Color strokeColor;
                        try { strokeColor = (Color)ColorConverter.ConvertFromString(stroke.ColorHex); }
                        catch { strokeColor = Colors.Black; }

                        double strokePx = Math.Max(1, (stroke.DisplayPixelThickness > 0 ? stroke.DisplayPixelThickness : 3) * ((double)width / 600.0));
                        var pen = new Pen(new SolidColorBrush(strokeColor), strokePx)
                        {
                            StartLineCap = PenLineCap.Round,
                            EndLineCap = PenLineCap.Round,
                            LineJoin = PenLineJoin.Round
                        };
                        pen.Freeze();

                        var streamGeom = new StreamGeometry();
                        using (var ctx = streamGeom.Open())
                        {
                            ctx.BeginFigure(new Point(stroke.Points[0].X * width, stroke.Points[0].Y * height), false, false);
                            for (int i = 1; i < stroke.Points.Count; i++)
                            {
                                ctx.LineTo(new Point(stroke.Points[i].X * width, stroke.Points[i].Y * height), true, true);
                            }
                        }
                        streamGeom.Freeze();
                        dc.DrawGeometry(null, pen, streamGeom);
                    }
                }

                // 5. Draw AcroForm Values
                if (editorData.FormValues.Count > 0)
                {
                    var textBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A"));
                    textBrush.Freeze();
                    var checkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                    checkBrush.Freeze();
                    var checkPen = new Pen(checkBrush, Math.Max(1.5, width * 0.005))
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round
                    };
                    checkPen.Freeze();
                    var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

                    foreach (var kvp in editorData.FormValues)
                    {
                        var val = kvp.Value;
                        if (val.RelWidth <= 0 || val.RelHeight <= 0) continue;

                        double fx = val.RelX * width;
                        double fy = val.RelY * height;
                        double fw = val.RelWidth * width;
                        double fh = val.RelHeight * height;
                        var fieldRect = new Rect(fx, fy, fw, fh);

                        if (val.FieldType == FormFieldType.CheckBox || val.FieldType == FormFieldType.RadioButton)
                        {
                            if (val.BoolValue)
                            {
                                var badgeRect = new Rect(fx, fy, fw, fh);
                                var badgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                                badgeBrush.Freeze();
                                dc.DrawRoundedRectangle(badgeBrush, null, badgeRect, Math.Max(1, Math.Min(fw, fh) * 0.18), Math.Max(1, Math.Min(fw, fh) * 0.18));

                                double pad = Math.Min(fw, fh) * 0.20;
                                var p1 = new Point(fx + pad, fy + fh * 0.52);
                                var p2 = new Point(fx + fw * 0.42, fy + fh - pad);
                                var p3 = new Point(fx + fw - pad, fy + pad);

                                var whitePen = new Pen(Brushes.White, Math.Max(1.5, Math.Min(fw, fh) * 0.18))
                                {
                                    StartLineCap = PenLineCap.Round,
                                    EndLineCap = PenLineCap.Round,
                                    LineJoin = PenLineJoin.Round
                                };
                                whitePen.Freeze();

                                var geom = new StreamGeometry();
                                using (var ctx = geom.Open())
                                {
                                    ctx.BeginFigure(p1, false, false);
                                    ctx.LineTo(p2, true, true);
                                    ctx.LineTo(p3, true, true);
                                }
                                geom.Freeze();
                                dc.DrawGeometry(null, whitePen, geom);
                            }
                        }
                        else if (!string.IsNullOrEmpty(val.TextValue))
                        {
                            bool isMulti = val.IsMultiline || val.RelHeight >= 0.025 || val.TextValue.Contains('\n') || val.TextValue.Contains('\r');
                            double fontSize = Math.Max(7, Math.Min(16, isMulti ? Math.Min(fh * 0.35, width * 0.028) : fh * 0.65));
                            var formattedText = new FormattedText(
                                val.TextValue,
                                CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                fontSize,
                                textBrush,
                                96
                            )
                            {
                                MaxTextWidth = Math.Max(10, fw - 2),
                                MaxTextHeight = Math.Max(10, fh - 2)
                            };

                            dc.PushClip(new RectangleGeometry(fieldRect));
                            Point textOrigin = isMulti
                                ? new Point(fx + 2, fy + 2)
                                : new Point(fx + 2, fy + Math.Max(0, (fh - formattedText.Height) / 2.0));
                            dc.DrawText(formattedText, textOrigin);
                            dc.Pop();
                        }
                    }
                }

                // 6. Draw Placed Signatures
                if (editorData.Signatures.Count > 0)
                {
                    foreach (var sig in editorData.Signatures)
                    {
                        if (sig.SignatureImage == null) continue;
                        double sx = width * Math.Clamp(sig.RelX, 0, 0.99);
                        double sy = height * Math.Clamp(sig.RelY, 0, 0.99);
                        double sw = width * Math.Clamp(sig.RelWidth, 0.01, 1.0 - sig.RelX);
                        double sh = height * Math.Clamp(sig.RelHeight, 0.01, 1.0 - sig.RelY);

                        dc.DrawImage(sig.SignatureImage, new Rect(sx, sy, sw, sh));
                    }
                }

                // 7. Draw Extra / Legacy Signatures
                if (extraSignatures != null)
                {
                    foreach (var sig in extraSignatures)
                    {
                        if (sig.SignatureImage == null) continue;
                        double sx = width * Math.Clamp(sig.RelX, 0, 0.99);
                        double sy = height * Math.Clamp(sig.RelY, 0, 0.99);
                        double sw = width * Math.Clamp(sig.RelWidth, 0.01, 1.0 - sig.RelX);
                        double sh = height * Math.Clamp(sig.RelHeight, 0.01, 1.0 - sig.RelY);

                        dc.DrawImage(sig.SignatureImage, new Rect(sx, sy, sw, sh));
                    }
                }
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
