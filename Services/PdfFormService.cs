using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using pdfMerge.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace pdfMerge.Services
{
    public class ExtractedTextLine
    {
        public Rect NormalizedBounds { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service for extracting AcroForm fields and text geometry from PDF pages.
    /// </summary>
    public static class PdfFormService
    {
        private static readonly HashSet<string> OffValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Off", "/Off", "false", "0", "", "no"
        };

        public static bool NormalizeBoolValue(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return false;
            string val = rawValue.Trim();
            if (OffValues.Contains(val)) return false;
            return true;
        }

        /// <summary>
        /// Extracts interactive AcroForm fields from a specific PDF page.
        /// Returns descriptors in unrotated normalized [0..1] coordinates.
        /// </summary>
        public static async Task<List<FormFieldDescriptor>> ExtractFormFieldsAsync(string filePath, int pageIndex)
        {
            var results = new List<FormFieldDescriptor>();
            string fullPath = Path.GetFullPath(filePath);

            if (PdfService.IsSupportedImageFile(fullPath))
            {
                return results; // Image files have no AcroForms
            }

            await Task.Run(() =>
            {
                try
                {
                    string? pwd = PdfSecurityService.GetPassword(fullPath);
                    using var stream = File.OpenRead(fullPath);
                    using var doc = !string.IsNullOrEmpty(pwd)
                        ? PdfReader.Open(stream, pwd, PdfDocumentOpenMode.Import)
                        : PdfReader.Open(stream, PdfDocumentOpenMode.Import);

                    if (pageIndex < 0 || pageIndex >= doc.PageCount) return;

                    var page = doc.Pages[pageIndex];
                    double pageWidthPt = page.Width.Point;
                    double pageHeightPt = page.Height.Point;

                    if (pageWidthPt <= 0 || pageHeightPt <= 0) return;

                    // Inspect /Annots array on the page
                    var annotsArray = page.Elements.GetArray("/Annots");
                    if (annotsArray != null)
                    {
                        foreach (var annotRef in annotsArray)
                        {
                            PdfDictionary? annotDict = null;
                            if (annotRef is PdfReference r && r.Value is PdfDictionary dictRef)
                            {
                                annotDict = dictRef;
                            }
                            else if (annotRef is PdfDictionary dictDirect)
                            {
                                annotDict = dictDirect;
                            }

                            if (annotDict == null) continue;

                            string subtype = annotDict.Elements.GetName("/Subtype");
                            if (!string.Equals(subtype, "/Widget", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var descriptor = ParseWidgetToDescriptor(annotDict, pageWidthPt, pageHeightPt);
                            if (descriptor != null)
                            {
                                results.Add(descriptor);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting AcroForm fields from {fullPath}: {ex.Message}");
                }
            });

            return results;
        }

        private static FormFieldDescriptor? ParseWidgetToDescriptor(PdfDictionary widget, double pageWidthPt, double pageHeightPt)
        {
            try
            {
                var rectArray = widget.Elements.GetArray("/Rect");
                if (rectArray == null || rectArray.Elements.Count < 4) return null;

                double llx = GetArrayDouble(rectArray, 0);
                double lly = GetArrayDouble(rectArray, 1);
                double urx = GetArrayDouble(rectArray, 2);
                double ury = GetArrayDouble(rectArray, 3);

                Rect normRect = EditorCoordinateService.PdfRectToNormalizedBaseRect(llx, lly, urx, ury, pageWidthPt, pageHeightPt);

                string name = widget.Elements.GetString("/T");
                if (string.IsNullOrEmpty(name))
                {
                    name = widget.Elements.GetName("/T");
                }
                if (string.IsNullOrEmpty(name))
                {
                    name = $"Field_{Math.Round(normRect.X, 3)}_{Math.Round(normRect.Y, 3)}";
                }

                string ft = widget.Elements.GetName("/FT");
                int flags = widget.Elements.GetInteger("/Ff");

                // Check parent dictionary for inherited /FT or /T if missing
                if (string.IsNullOrEmpty(ft) && widget.Elements.GetObject("/Parent") is PdfDictionary parent)
                {
                    ft = parent.Elements.GetName("/FT");
                    if (string.IsNullOrEmpty(name))
                    {
                        name = parent.Elements.GetString("/T");
                    }
                    if (flags == 0)
                    {
                        flags = parent.Elements.GetInteger("/Ff");
                    }
                }

                FormFieldType fieldType = FormFieldType.Text;
                bool isMultiline = (flags & 0x1000) != 0 || normRect.Height >= 0.025;

                if (string.Equals(ft, "/Btn", StringComparison.OrdinalIgnoreCase))
                {
                    if ((flags & 0x8000) != 0) // Radio button flag
                    {
                        fieldType = FormFieldType.RadioButton;
                    }
                    else
                    {
                        fieldType = FormFieldType.CheckBox;
                    }
                }
                else if (string.Equals(ft, "/Ch", StringComparison.OrdinalIgnoreCase))
                {
                    fieldType = FormFieldType.Choice;
                }
                else if (string.Equals(ft, "/Sig", StringComparison.OrdinalIgnoreCase))
                {
                    fieldType = FormFieldType.Signature;
                }

                string rawValue = widget.Elements.GetString("/V");
                if (string.IsNullOrEmpty(rawValue))
                {
                    rawValue = widget.Elements.GetName("/V");
                }

                if (!string.IsNullOrEmpty(rawValue) && (rawValue.Contains('\n') || rawValue.Contains('\r')))
                {
                    isMultiline = true;
                }

                var options = new List<string>();
                if (fieldType == FormFieldType.Choice)
                {
                    var optArray = widget.Elements.GetArray("/Opt");
                    if (optArray != null)
                    {
                        foreach (var optItem in optArray)
                        {
                            string optStr = optItem.ToString() ?? "";
                            if (!string.IsNullOrEmpty(optStr)) options.Add(optStr);
                        }
                    }
                }

                return new FormFieldDescriptor
                {
                    Name = name,
                    FieldType = fieldType,
                    RelX = normRect.X,
                    RelY = normRect.Y,
                    RelWidth = normRect.Width,
                    RelHeight = normRect.Height,
                    IsMultiline = isMultiline,
                    IsReadOnly = (flags & 1) != 0,
                    DefaultValue = rawValue?.TrimStart('/') ?? "",
                    Options = options
                };
            }
            catch
            {
                return null;
            }
        }

        private static double GetArrayDouble(PdfArray array, int index)
        {
            if (index >= array.Elements.Count) return 0;
            var elem = array.Elements[index];
            if (elem is PdfReal real) return real.Value;
            if (elem is PdfInteger integer) return integer.Value;
            if (double.TryParse(elem.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                return parsed;
            return 0;
        }

        /// <summary>
        /// Extracts geometric text lines from a PDF page's content streams for text highlighting.
        /// </summary>
        public static async Task<List<ExtractedTextLine>> ExtractTextLinesAsync(string filePath, int pageIndex)
        {
            var lines = new List<ExtractedTextLine>();
            string fullPath = Path.GetFullPath(filePath);

            if (PdfService.IsSupportedImageFile(fullPath))
            {
                return lines;
            }

            await Task.Run(() =>
            {
                try
                {
                    string? pwd = PdfSecurityService.GetPassword(fullPath);
                    using var pigDoc = !string.IsNullOrEmpty(pwd)
                        ? UglyToad.PdfPig.PdfDocument.Open(fullPath, new UglyToad.PdfPig.ParsingOptions { Password = pwd })
                        : UglyToad.PdfPig.PdfDocument.Open(fullPath);
                    if (pageIndex < 0 || pageIndex >= pigDoc.NumberOfPages) return;

                    var pigPage = pigDoc.GetPage(pageIndex + 1);
                    double pageWidth = pigPage.Width;
                    double pageHeight = pigPage.Height;

                    if (pageWidth <= 0 || pageHeight <= 0) return;

                    var words = pigPage.GetWords()
                        .OrderByDescending(w => w.BoundingBox.Bottom)
                        .ThenBy(w => w.BoundingBox.Left)
                        .ToList();

                    if (words.Count == 0) return;

                    var currentLineWords = new List<UglyToad.PdfPig.Content.Word>();

                    foreach (var word in words)
                    {
                        if (currentLineWords.Count == 0)
                        {
                            currentLineWords.Add(word);
                        }
                        else
                        {
                            double avgBottom = currentLineWords.Average(w => w.BoundingBox.Bottom);
                            double avgHeight = currentLineWords.Average(w => w.BoundingBox.Height);
                            if (Math.Abs(word.BoundingBox.Bottom - avgBottom) <= Math.Max(3.0, avgHeight * 0.50))
                            {
                                currentLineWords.Add(word);
                            }
                            else
                            {
                                AddExtractedLine(lines, currentLineWords, pageWidth, pageHeight);
                                currentLineWords.Clear();
                                currentLineWords.Add(word);
                            }
                        }
                    }

                    if (currentLineWords.Count > 0)
                    {
                        AddExtractedLine(lines, currentLineWords, pageWidth, pageHeight);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error extracting text lines with PdfPig from {fullPath}: {ex.Message}");
                }
            });

            return lines;
        }

        private static void AddExtractedLine(List<ExtractedTextLine> lines, List<UglyToad.PdfPig.Content.Word> currentLineWords, double pageWidth, double pageHeight)
        {
            if (currentLineWords.Count == 0) return;
            var orderedLine = currentLineWords.OrderBy(w => w.BoundingBox.Left).ToList();
            double minL = orderedLine.Min(w => w.BoundingBox.Left);
            double maxR = orderedLine.Max(w => w.BoundingBox.Right);
            double maxT = orderedLine.Max(w => w.BoundingBox.Top);
            double minB = orderedLine.Min(w => w.BoundingBox.Bottom);

            double normX = Math.Clamp(minL / pageWidth, 0, 0.999);
            double normY = Math.Clamp((pageHeight - maxT) / pageHeight, 0, 0.999);
            double normW = Math.Clamp((maxR - minL) / pageWidth, 0.001, 1.0 - normX);
            double normH = Math.Clamp((maxT - minB) / pageHeight, 0.001, 1.0 - normY);

            string lineText = string.Join(" ", orderedLine.Select(w => w.Text));
            lines.Add(new ExtractedTextLine
            {
                Text = lineText,
                NormalizedBounds = new Rect(normX, normY, normW, normH)
            });
        }
    }
}
