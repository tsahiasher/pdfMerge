using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;

namespace pdfMerge.Models
{
    public enum FormTextSizeMode
    {
        Auto,
        Small,
        Medium,
        Large
    }

    public enum StrokeType
    {
        Pen,
        FreehandHighlight
    }

    public enum FormFieldType
    {
        Text,
        CheckBox,
        RadioButton,
        Choice,
        Signature
    }

    /// <summary>
    /// Describes an AcroForm field extracted from a PDF page.
    /// Bounding boxes and positions are stored in normalized [0..1] unrotated page coordinates.
    /// </summary>
    public class FormFieldDescriptor
    {
        public string Name { get; set; } = string.Empty;
        public FormFieldType FieldType { get; set; } = FormFieldType.Text;
        public double RelX { get; set; }
        public double RelY { get; set; }
        public double RelWidth { get; set; }
        public double RelHeight { get; set; }
        public bool IsMultiline { get; set; }
        public bool IsReadOnly { get; set; }
        public string DefaultValue { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new List<string>();

        public FormFieldDescriptor Clone()
        {
            return new FormFieldDescriptor
            {
                Name = this.Name,
                FieldType = this.FieldType,
                RelX = this.RelX,
                RelY = this.RelY,
                RelWidth = this.RelWidth,
                RelHeight = this.RelHeight,
                IsMultiline = this.IsMultiline,
                IsReadOnly = this.IsReadOnly,
                DefaultValue = this.DefaultValue,
                Options = new List<string>(this.Options)
            };
        }
    }

    /// <summary>
    /// Stores the user-entered value for an AcroForm field.
    /// </summary>
    public class FormFieldValue
    {
        public string FieldName { get; set; } = string.Empty;
        public FormFieldType FieldType { get; set; } = FormFieldType.Text;
        public double RelX { get; set; }
        public double RelY { get; set; }
        public double RelWidth { get; set; }
        public double RelHeight { get; set; }
        public string TextValue { get; set; } = string.Empty;
        public bool BoolValue { get; set; }
        public bool IsMultiline { get; set; }

        public FormFieldValue Clone()
        {
            return new FormFieldValue
            {
                FieldName = this.FieldName,
                FieldType = this.FieldType,
                RelX = this.RelX,
                RelY = this.RelY,
                RelWidth = this.RelWidth,
                RelHeight = this.RelHeight,
                TextValue = this.TextValue,
                BoolValue = this.BoolValue,
                IsMultiline = this.IsMultiline
            };
        }
    }

    /// <summary>
    /// Represents a vector text highlight spanning one or more line segments.
    /// Coordinates are normalized [0..1] unrotated page coordinates.
    /// </summary>
    public class TextHighlightItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public List<Rect> LineRects { get; set; } = new List<Rect>();
        public string ColorHex { get; set; } = "#FACC15"; // Yellow
        public double Opacity { get; set; } = 0.40;

        public TextHighlightItem Clone()
        {
            return new TextHighlightItem
            {
                Id = this.Id,
                LineRects = new List<Rect>(this.LineRects),
                ColorHex = this.ColorHex,
                Opacity = this.Opacity
            };
        }
    }

    /// <summary>
    /// Represents a freehand pen stroke or freehand highlight ribbon.
    /// Points are stored in normalized [0..1] unrotated page coordinates.
    /// </summary>
    public class DrawingStroke
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public StrokeType Type { get; set; } = StrokeType.Pen;
        public List<Point> Points { get; set; } = new List<Point>();
        public string ColorHex { get; set; } = "#000000";
        public double NormalizedThickness { get; set; } // Thickness relative to unrotated page width
        public double DisplayPixelThickness { get; set; } // Reference thickness at 100% zoom (e.g. 3px, 20px)
        public double Opacity { get; set; } = 1.0;

        public DrawingStroke Clone()
        {
            return new DrawingStroke
            {
                Id = this.Id,
                Type = this.Type,
                Points = new List<Point>(this.Points),
                ColorHex = this.ColorHex,
                NormalizedThickness = this.NormalizedThickness,
                DisplayPixelThickness = this.DisplayPixelThickness,
                Opacity = this.Opacity
            };
        }
    }

    /// <summary>
    /// Represents an interactive placed signature overlay on a page.
    /// Stored in normalized [0..1] unrotated page coordinates.
    /// </summary>
    public class PlacedSignatureItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public BitmapSource SignatureImage { get; set; } = null!;
        public double RelX { get; set; }
        public double RelY { get; set; }
        public double RelWidth { get; set; }
        public double RelHeight { get; set; }

        public PlacedSignatureItem Clone()
        {
            return new PlacedSignatureItem
            {
                Id = this.Id,
                SignatureImage = this.SignatureImage,
                RelX = this.RelX,
                RelY = this.RelY,
                RelWidth = this.RelWidth,
                RelHeight = this.RelHeight
            };
        }
    }

    /// <summary>
    /// Complete container for all edits on a PDF page.
    /// Supports deep cloning for session isolation and rollback on Cancel.
    /// </summary>
    public class PageEditorData
    {
        public FormTextSizeMode TextSizeMode { get; set; } = FormTextSizeMode.Auto;
        public Dictionary<string, FormFieldValue> FormValues { get; set; } = new Dictionary<string, FormFieldValue>(StringComparer.OrdinalIgnoreCase);
        public List<DrawingStroke> PenStrokes { get; set; } = new List<DrawingStroke>();
        public List<DrawingStroke> FreehandHighlights { get; set; } = new List<DrawingStroke>();
        public List<TextHighlightItem> TextHighlights { get; set; } = new List<TextHighlightItem>();
        public List<PlacedSignatureItem> Signatures { get; set; } = new List<PlacedSignatureItem>();

        public bool HasEdits =>
            FormValues.Count > 0 ||
            PenStrokes.Count > 0 ||
            FreehandHighlights.Count > 0 ||
            TextHighlights.Count > 0 ||
            Signatures.Count > 0;

        public PageEditorData Clone()
        {
            var clone = new PageEditorData
            {
                TextSizeMode = this.TextSizeMode,
                FormValues = new Dictionary<string, FormFieldValue>(StringComparer.OrdinalIgnoreCase)
            };

            foreach (var kvp in this.FormValues)
            {
                clone.FormValues[kvp.Key] = kvp.Value.Clone();
            }

            foreach (var stroke in this.PenStrokes)
            {
                clone.PenStrokes.Add(stroke.Clone());
            }

            foreach (var hl in this.FreehandHighlights)
            {
                clone.FreehandHighlights.Add(hl.Clone());
            }

            foreach (var th in this.TextHighlights)
            {
                clone.TextHighlights.Add(th.Clone());
            }

            foreach (var sig in this.Signatures)
            {
                clone.Signatures.Add(sig.Clone());
            }

            return clone;
        }

        public void ClearDrawings()
        {
            PenStrokes.Clear();
            FreehandHighlights.Clear();
            TextHighlights.Clear();
        }
    }
}
