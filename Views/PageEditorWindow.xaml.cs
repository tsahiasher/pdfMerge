using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using pdfMerge.Helpers;
using pdfMerge.Models;
using pdfMerge.Services;

namespace pdfMerge.Views
{
    public enum EditorTool
    {
        FormText,
        Pen,
        Highlighter,
        Signature,
        Eraser
    }

    public partial class PageEditorWindow : Window
    {
        private readonly PdfPageItem _targetPage;
        private PageEditorData _workingData;
        private readonly Stack<PageEditorData> _undoStack = new Stack<PageEditorData>();

        private List<FormFieldDescriptor> _formFields = new List<FormFieldDescriptor>();
        private List<ExtractedTextLine> _textLines = new List<ExtractedTextLine>();

        private EditorTool _currentTool = EditorTool.FormText;
        private double _zoomFactor = 1.0;
        private double _basePageWidth = 600;
        private double _basePageHeight = 800;

        private double DisplayedWidth => _targetPage.Rotation == 90 || _targetPage.Rotation == 270 ? _basePageHeight : _basePageWidth;
        private double DisplayedHeight => _targetPage.Rotation == 90 || _targetPage.Rotation == 270 ? _basePageWidth : _basePageHeight;

        // Drawing / Highlighting State
        private bool _isInteracting = false;
        private readonly List<Point> _activeStrokePoints = new List<Point>();
        private bool _isTextHighlightLocked = false;
        private ExtractedTextLine? _textHighlightStartLine;
        private Point _textHighlightStartPoint;
        private bool _isRectangleHighlightMode = false;
        private bool _isDrawingRectHighlight = false;
        private Point _rectHighlightStartMouse;

        // Tool Settings
        private string _activePenColor = "#000000";
        private double _activePenWidth = 3.0;
        private double _activeHlWidth = 20.0;
        private double _activeEraserSize = 10.0;
        private FormTextSizeMode _textSizeMode = FormTextSizeMode.Auto;

        // Signature Placement & Manipulation State
        private PlacedSignatureItem? _selectedSignature;
        private bool _isDraggingSignature = false;
        private bool _isResizingSignature = false;
        private bool _isDrawingSignaturePlacement = false;
        private Point _sigPlacementStartMouse;
        private Point _sigDragStartMouse;
        private Rect _sigDragStartRect;
        private bool _isLoaded = false;

        public PageEditorWindow(PdfPageItem page)
        {
            _targetPage = page;
            _workingData = _targetPage.EditorData?.Clone() ?? new PageEditorData();

            // Import any legacy page signatures into working data if not already present
            foreach (var sig in _targetPage.PageSignatures)
            {
                if (!_workingData.Signatures.Any(s => s.RelX == sig.RelX && s.RelY == sig.RelY))
                {
                    _workingData.Signatures.Add(new PlacedSignatureItem
                    {
                        SignatureImage = sig.SignatureImage,
                        RelX = sig.RelX,
                        RelY = sig.RelY,
                        RelWidth = sig.RelWidth,
                        RelHeight = sig.RelHeight
                    });
                }
            }

            InitializeComponent();

            // Always default to Form & Text on every window open (Requirement 1)
            _currentTool = EditorTool.FormText;
            if (TabToolFormText != null) TabToolFormText.IsChecked = true;

            if (TxtPageBadge != null) TxtPageBadge.Text = $"Page {_targetPage.DisplayPageNumber} (Orig. {_targetPage.OriginalPageNumber})";
            if (TxtSourceFileName != null) TxtSourceFileName.Text = _targetPage.SourceFileName;

            if (_targetPage.Rotation != 0 && BadgeRotation != null && TxtRotationBadge != null)
            {
                BadgeRotation.Visibility = Visibility.Visible;
                TxtRotationBadge.Text = $"{_targetPage.Rotation}°";
            }

            Loaded += PageEditorWindow_Loaded;
        }

        private async void PageEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TxtStatus.Text = "Loading high-resolution page render and form fields...";

            // 1. Render clean raw high-resolution base page without previous editor annotations
            BitmapSource? baseBmp = await PdfService.RenderPageThumbnailAsync(_targetPage.SourceFilePath, _targetPage.OriginalPageIndex, 2048);
            if (baseBmp == null && _targetPage.OriginalThumbnail != null)
            {
                baseBmp = _targetPage.OriginalThumbnail;
            }

            if (baseBmp != null)
            {
                if (_targetPage.Rotation != 0)
                {
                    baseBmp = BitmapUtilities.RotateBitmap(baseBmp, _targetPage.Rotation);
                }

                // Measure intrinsic aspect ratio
                if (_targetPage.Rotation == 90 || _targetPage.Rotation == 270)
                {
                    _basePageWidth = 800.0 * ((double)baseBmp.PixelHeight / Math.Max(1, baseBmp.PixelWidth));
                    _basePageHeight = 800.0;
                }
                else
                {
                    _basePageWidth = 650.0;
                    _basePageHeight = 650.0 * ((double)baseBmp.PixelHeight / Math.Max(1, baseBmp.PixelWidth));
                }

                ImgBasePage.Source = baseBmp;
            }

            // 2. Extract PDF AcroForm fields and text geometry
            try
            {
                _formFields = await PdfFormService.ExtractFormFieldsAsync(_targetPage.SourceFilePath, _targetPage.OriginalPageIndex);
                _textLines = await PdfFormService.ExtractTextLinesAsync(_targetPage.SourceFilePath, _targetPage.OriginalPageIndex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading PDF fields: {ex.Message}");
            }

            // 3. Mark window as loaded and ready for layer rendering
            _isLoaded = true;

            // 4. Apply initial zoom to fit comfortably in view
            FitToPage();

            // 5. Render all initial layers
            RenderAllLayers();

            TxtStatus.Text = "Ready";
        }

        #region Undo History Management

        private void PushUndoSnapshot()
        {
            _undoStack.Push(_workingData.Clone());
            if (_undoStack.Count > 40)
            {
                // Cap undo stack depth to prevent excessive memory usage
                var items = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = items.Length - 2; i >= 0; i--)
                {
                    _undoStack.Push(items[i]);
                }
            }
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count > 0)
            {
                _workingData = _undoStack.Pop();
                RenderCommittedDrawings();
                RenderCommittedHighlights();
                RenderSignatures();
                TxtStatus.Text = "Undid last action";
            }
            else
            {
                TxtStatus.Text = "Nothing to undo";
            }
        }

        #endregion

        #region Tool Selection & Option Bar Switching

        private void ToolTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || PnlOptionsFormText == null || WorkspaceContainer == null) return;

            PnlOptionsFormText.Visibility = Visibility.Collapsed;
            PnlOptionsPen.Visibility = Visibility.Collapsed;
            PnlOptionsHighlighter.Visibility = Visibility.Collapsed;
            PnlOptionsSignature.Visibility = Visibility.Collapsed;
            PnlOptionsEraser.Visibility = Visibility.Collapsed;

            DeselectSignature();

            if (TabToolFormText.IsChecked == true)
            {
                _currentTool = EditorTool.FormText;
                PnlOptionsFormText.Visibility = Visibility.Visible;
                WorkspaceContainer.Cursor = Cursors.IBeam;
                EraserFollowerCircle.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "Form & Text";
            }
            else if (TabToolPen.IsChecked == true)
            {
                _currentTool = EditorTool.Pen;
                PnlOptionsPen.Visibility = Visibility.Visible;
                WorkspaceContainer.Cursor = pdfMerge.Helpers.CursorUtility.RotatedPen;
                EraserFollowerCircle.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "Draw Pen";
            }
            else if (TabToolHighlighter.IsChecked == true)
            {
                _currentTool = EditorTool.Highlighter;
                PnlOptionsHighlighter.Visibility = Visibility.Visible;
                WorkspaceContainer.Cursor = _isRectangleHighlightMode ? Cursors.Cross : pdfMerge.Helpers.CursorUtility.HighlighterPen;
                EraserFollowerCircle.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "Highlighter";
            }
            else if (TabToolSignature.IsChecked == true)
            {
                _currentTool = EditorTool.Signature;
                PnlOptionsSignature.Visibility = Visibility.Visible;
                WorkspaceContainer.Cursor = Cursors.Cross;
                EraserFollowerCircle.Visibility = Visibility.Collapsed;
                TxtStatus.Text = "Signature";
            }
            else if (TabToolEraser.IsChecked == true)
            {
                _currentTool = EditorTool.Eraser;
                PnlOptionsEraser.Visibility = Visibility.Visible;
                WorkspaceContainer.Cursor = Cursors.None; // Hide cursor over workspace for circular follower (Requirement 8)
                EraserFollowerCircle.Visibility = Visibility.Visible;
                UpdateEraserFollowerSize();
                TxtStatus.Text = "Eraser";
            }

            RenderAcroFormControls();
        }

        private void TextSize_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _workingData == null || WorkspaceContainer == null) return;

            if (RdoSizeSmall?.IsChecked == true) _textSizeMode = FormTextSizeMode.Small;
            else if (RdoSizeMedium?.IsChecked == true) _textSizeMode = FormTextSizeMode.Medium;
            else if (RdoSizeLarge?.IsChecked == true) _textSizeMode = FormTextSizeMode.Large;
            else _textSizeMode = FormTextSizeMode.Auto;

            _workingData.TextSizeMode = _textSizeMode;
            RenderAcroFormControls();
        }

        private void HlMode_Checked(object sender, RoutedEventArgs e)
        {
            _isRectangleHighlightMode = (RdoHlRectangle?.IsChecked == true);

            if (_currentTool == EditorTool.Highlighter && WorkspaceContainer != null)
            {
                WorkspaceContainer.Cursor = _isRectangleHighlightMode ? Cursors.Cross : pdfMerge.Helpers.CursorUtility.HighlighterPen;
            }

            if (TxtHlWidthLabel != null) TxtHlWidthLabel.Visibility = _isRectangleHighlightMode ? Visibility.Collapsed : Visibility.Visible;
            if (SldHlWidth != null) SldHlWidth.Visibility = _isRectangleHighlightMode ? Visibility.Collapsed : Visibility.Visible;
            if (TxtHlWidthVal != null) TxtHlWidthVal.Visibility = _isRectangleHighlightMode ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnPenColor_Click(object sender, RoutedEventArgs e)
        {
            _activePenColor = "#000000";
        }

        private void SldPenWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _activePenWidth = Math.Round(e.NewValue);
            if (TxtPenWidthVal != null) TxtPenWidthVal.Text = $"{_activePenWidth}px";
        }

        private void SldHlWidth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _activeHlWidth = Math.Round(e.NewValue);
            if (TxtHlWidthVal != null) TxtHlWidthVal.Text = $"{_activeHlWidth}px";
        }

        private void SldEraserSize_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _activeEraserSize = Math.Round(e.NewValue);
            if (TxtEraserSizeVal != null) TxtEraserSizeVal.Text = $"{_activeEraserSize}px";
            UpdateEraserFollowerSize();
        }

        private void UpdateEraserFollowerSize()
        {
            if (EraserFollowerCircle != null)
            {
                double d = _activeEraserSize * _zoomFactor;
                EraserFollowerCircle.Width = d;
                EraserFollowerCircle.Height = d;
            }
        }

        private void BtnClearAllDrawings_Click(object sender, RoutedEventArgs e)
        {
            if (_workingData.PenStrokes.Count > 0 || _workingData.FreehandHighlights.Count > 0 || _workingData.TextHighlights.Count > 0)
            {
                PushUndoSnapshot();
                _workingData.ClearDrawings();
                RenderCommittedDrawings();
                RenderCommittedHighlights();
                TxtStatus.Text = "Cleared all drawings and highlights";
            }
        }

        #endregion

        #region Zoom and Viewport Traversal

        private void SetZoom(double zoom)
        {
            _zoomFactor = Math.Clamp(zoom, 0.40, 2.50);
            TxtZoomPercentage.Text = $"{Math.Round(_zoomFactor * 100)}%";

            double w = DisplayedWidth * _zoomFactor;
            double h = DisplayedHeight * _zoomFactor;

            WorkspaceContainer.Width = w;
            WorkspaceContainer.Height = h;

            UpdateEraserFollowerSize();
            RenderAllLayers();
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor + 0.15);
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor - 0.15);

        private void BtnFitWidth_Click(object sender, RoutedEventArgs e)
        {
            double availableWidth = Math.Max(200, EditorScrollViewer.ActualWidth - 60);
            double targetZoom = availableWidth / DisplayedWidth;
            SetZoom(targetZoom);
        }

        private void BtnFitPage_Click(object sender, RoutedEventArgs e) => FitToPage();

        private void FitToPage()
        {
            double availW = Math.Max(200, EditorScrollViewer.ActualWidth - 60);
            double availH = Math.Max(200, EditorScrollViewer.ActualHeight - 60);
            double zW = availW / DisplayedWidth;
            double zH = availH / DisplayedHeight;
            SetZoom(Math.Min(zW, zH));
        }

        #endregion

        #region Rendering All Layers

        private void RenderAllLayers()
        {
            if (!_isLoaded || WorkspaceContainer == null || _workingData == null) return;
            RenderCommittedHighlights();
            RenderCommittedDrawings();
            RenderAcroFormControls();
            RenderSignatures();
        }

        private void RenderCommittedHighlights()
        {
            if (!_isLoaded || CnvHighlights == null || WorkspaceContainer == null || _workingData == null) return;
            CnvHighlights.Children.Clear();
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;

            // 1. Text Highlights (yellow rectangles with ~0.40 opacity)
            var hlBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FACC15")) { Opacity = 0.40 };
            hlBrush.Freeze();

            foreach (var th in _workingData.TextHighlights)
            {
                foreach (var lr in th.LineRects)
                {
                    Rect screenRect = EditorCoordinateService.BaseToScreenRect(lr, curW, curH, _targetPage.Rotation);
                    var rectShape = new Rectangle
                    {
                        Width = Math.Max(2, screenRect.Width),
                        Height = Math.Max(2, screenRect.Height),
                        Fill = hlBrush,
                        RadiusX = 2,
                        RadiusY = 2
                    };
                    Canvas.SetLeft(rectShape, screenRect.X);
                    Canvas.SetTop(rectShape, screenRect.Y);
                    CnvHighlights.Children.Add(rectShape);
                }
            }

            // 2. Freehand Highlights (smooth ribbons with ~0.38 opacity)
            foreach (var fh in _workingData.FreehandHighlights)
            {
                if (fh.Points.Count < 2) continue;

                Color col = (Color)ColorConverter.ConvertFromString("#FACC15");
                col.A = (byte)(255 * 0.38);

                double strokePx = Math.Max(4, fh.DisplayPixelThickness * _zoomFactor);
                var penBrush = new SolidColorBrush(col);
                penBrush.Freeze();

                var pathGeom = CreateSmoothedPathGeometry(fh.Points, curW, curH, _targetPage.Rotation);
                var pathShape = new Path
                {
                    Data = pathGeom,
                    Stroke = penBrush,
                    StrokeThickness = strokePx,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                CnvHighlights.Children.Add(pathShape);
            }
        }

        private void RenderCommittedDrawings()
        {
            if (!_isLoaded || CnvDrawings == null || WorkspaceContainer == null || _workingData == null) return;
            CnvDrawings.Children.Clear();
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;

            foreach (var stroke in _workingData.PenStrokes)
            {
                if (stroke.Points.Count < 2) continue;

                Color col;
                try { col = (Color)ColorConverter.ConvertFromString(stroke.ColorHex); }
                catch { col = Colors.Black; }

                double strokePx = Math.Max(1, stroke.DisplayPixelThickness * _zoomFactor);
                var brush = new SolidColorBrush(col);
                brush.Freeze();

                var pathGeom = CreateSmoothedPathGeometry(stroke.Points, curW, curH, _targetPage.Rotation);
                var pathShape = new Path
                {
                    Data = pathGeom,
                    Stroke = brush,
                    StrokeThickness = strokePx,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round
                };
                CnvDrawings.Children.Add(pathShape);
            }
        }

        private PathGeometry CreateSmoothedPathGeometry(List<Point> basePoints, double curW, double curH, int rotation)
        {
            var geom = new PathGeometry();
            if (basePoints.Count < 2) return geom;

            var screenPoints = basePoints.Select(p => EditorCoordinateService.BaseToScreenPoint(p, curW, curH, rotation)).ToList();

            var figure = new PathFigure
            {
                StartPoint = screenPoints[0],
                IsFilled = false,
                IsClosed = false
            };

            for (int i = 1; i < screenPoints.Count; i++)
            {
                Point prev = screenPoints[i - 1];
                Point curr = screenPoints[i];
                Point mid = new Point((prev.X + curr.X) / 2.0, (prev.Y + curr.Y) / 2.0);

                figure.Segments.Add(new QuadraticBezierSegment(prev, mid, true));
            }

            figure.Segments.Add(new LineSegment(screenPoints[^1], true));
            geom.Figures.Add(figure);
            return geom;
        }

        #endregion

        #region AcroForm Interactive Controls Layer

        private void RenderAcroFormControls()
        {
            if (!_isLoaded || CnvAcroFormControls == null || WorkspaceContainer == null || _workingData == null) return;
            CnvAcroFormControls.Children.Clear();
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;

            bool isFormToolActive = _currentTool == EditorTool.FormText;
            CnvAcroFormControls.IsHitTestVisible = isFormToolActive;

            foreach (var field in _formFields)
            {
                Rect screenRect = EditorCoordinateService.BaseToScreenRect(new Rect(field.RelX, field.RelY, field.RelWidth, field.RelHeight), curW, curH, _targetPage.Rotation);

                // Get stored user value or default
                _workingData.FormValues.TryGetValue(field.Name, out var storedVal);
                string textVal = storedVal?.TextValue ?? field.DefaultValue;
                bool boolVal = storedVal?.BoolValue ?? PdfFormService.NormalizeBoolValue(field.DefaultValue);
                bool isMultiline = field.IsMultiline
                    || field.RelHeight >= 0.025
                    || (storedVal?.IsMultiline ?? false)
                    || (!string.IsNullOrEmpty(textVal) && (textVal.Contains('\n') || textVal.Contains('\r')));

                // Calculate font size (Auto / Small / Medium / Large)
                double fontSize = CalculateFontSize(screenRect.Height, _textSizeMode);

                FrameworkElement control;

                if (field.FieldType == FormFieldType.CheckBox || field.FieldType == FormFieldType.RadioButton)
                {
                    double chkSize = Math.Max(12, Math.Min(26, Math.Min(screenRect.Width, screenRect.Height)));
                    var chkBorder = new Border
                    {
                        Width = chkSize,
                        Height = chkSize,
                        CornerRadius = new CornerRadius(Math.Max(2, chkSize * 0.18)),
                        BorderThickness = new Thickness(1.5),
                        Cursor = Cursors.Hand,
                        ToolTip = field.Name
                    };

                    var checkMark = new TextBlock
                    {
                        Text = "✓",
                        FontFamily = new FontFamily("Segoe UI"),
                        FontWeight = FontWeights.ExtraBold,
                        FontSize = Math.Max(8, chkSize * 0.75),
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, -1, 0, 0)
                    };

                    void UpdateCheckVisual(bool isChecked)
                    {
                        if (isChecked)
                        {
                            chkBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                            chkBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0369A1"));
                            chkBorder.Child = checkMark;
                        }
                        else
                        {
                            chkBorder.Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
                            chkBorder.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7"));
                            chkBorder.Child = null;
                        }
                    }

                    UpdateCheckVisual(boolVal);

                    chkBorder.MouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        _workingData.FormValues.TryGetValue(field.Name, out var curr);
                        bool nextVal = !(curr?.BoolValue ?? boolVal);
                        UpdateFormFieldBool(field, nextVal);
                        UpdateCheckVisual(nextVal);
                    };

                    control = chkBorder;
                }
                else if (field.FieldType == FormFieldType.Choice && field.Options.Count > 0)
                {
                    var cmb = new ComboBox
                    {
                        ItemsSource = field.Options,
                        SelectedItem = field.Options.FirstOrDefault(o => o.Equals(textVal, StringComparison.OrdinalIgnoreCase)) ?? field.Options.FirstOrDefault(),
                        Width = Math.Max(15, screenRect.Width),
                        Height = Math.Max(8, screenRect.Height),
                        FontSize = fontSize,
                        Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                        Foreground = Brushes.Black,
                        ToolTip = field.Name
                    };

                    cmb.SelectionChanged += (s, e) =>
                    {
                        if (cmb.SelectedItem is string sel)
                        {
                            UpdateFormFieldText(field, sel, isMultiline);
                        }
                    };

                    control = cmb;
                }
                else
                {
                    // Text Field (Universal Multiline Support by Box Height >=2.5% or Newlines)
                    double padY = Math.Max(0, Math.Min(2, (screenRect.Height - fontSize) / 3.0));
                    var txt = new TextBox
                    {
                        Text = textVal,
                        Width = Math.Max(10, screenRect.Width),
                        Height = Math.Max(6, screenRect.Height),
                        FontSize = fontSize,
                        FontFamily = new FontFamily("Segoe UI"),
                        Foreground = Brushes.Black,
                        Background = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                        BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0284C7")),
                        BorderThickness = new Thickness(1),
                        AcceptsReturn = isMultiline,
                        TextWrapping = isMultiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                        Padding = new Thickness(2, padY, 2, padY),
                        ToolTip = field.Name
                    };

                    txt.TextChanged += (s, e) =>
                    {
                        UpdateFormFieldText(field, txt.Text, txt.AcceptsReturn);
                    };

                    // Handle Enter key for instantaneous conversion from single-line to multiline
                    txt.PreviewKeyDown += (s, e) =>
                    {
                        if (e.Key == Key.Enter && !txt.AcceptsReturn)
                        {
                            e.Handled = true;
                            int caretIdx = txt.CaretIndex;
                            txt.AcceptsReturn = true;
                            txt.TextWrapping = TextWrapping.Wrap;
                            txt.Text = txt.Text.Insert(caretIdx, Environment.NewLine);
                            txt.CaretIndex = caretIdx + Environment.NewLine.Length;
                            UpdateFormFieldText(field, txt.Text, true);
                        }
                    };

                    control = txt;
                }

                Canvas.SetLeft(control, screenRect.X);
                Canvas.SetTop(control, screenRect.Y);
                CnvAcroFormControls.Children.Add(control);
            }
        }

        private double CalculateFontSize(double fieldHeightPx, FormTextSizeMode sizeMode)
        {
            double medium = Math.Max(5, Math.Min(22, Math.Round(fieldHeightPx * 0.65)));
            return sizeMode switch
            {
                FormTextSizeMode.Small => Math.Max(4, Math.Round(medium * 0.70)),
                FormTextSizeMode.Medium => medium,
                FormTextSizeMode.Large => Math.Max(7, Math.Round(medium * 1.30)),
                _ => medium // Auto (Requirement 4)
            };
        }

        private void UpdateFormFieldText(FormFieldDescriptor field, string text, bool isMultiline)
        {
            if (!_workingData.FormValues.TryGetValue(field.Name, out var val))
            {
                val = new FormFieldValue { FieldName = field.Name };
                _workingData.FormValues[field.Name] = val;
            }
            val.FieldType = field.FieldType;
            val.RelX = field.RelX;
            val.RelY = field.RelY;
            val.RelWidth = field.RelWidth;
            val.RelHeight = field.RelHeight;
            val.TextValue = text;
            val.IsMultiline = isMultiline;
        }

        private void UpdateFormFieldBool(FormFieldDescriptor field, bool isChecked)
        {
            if (!_workingData.FormValues.TryGetValue(field.Name, out var val))
            {
                val = new FormFieldValue { FieldName = field.Name };
                _workingData.FormValues[field.Name] = val;
            }
            val.FieldType = field.FieldType;
            val.RelX = field.RelX;
            val.RelY = field.RelY;
            val.RelWidth = field.RelWidth;
            val.RelHeight = field.RelHeight;
            val.BoolValue = isChecked;
            val.TextValue = isChecked ? "Yes" : "Off";
        }

        #endregion

        #region Signatures Layer & In-Place Placement / Resizing

        private void RenderSignatures()
        {
            if (!_isLoaded || CnvSignatures == null || WorkspaceContainer == null || _workingData == null) return;
            CnvSignatures.Children.Clear();
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;

            foreach (var sig in _workingData.Signatures)
            {
                Rect screenRect = EditorCoordinateService.BaseToScreenRect(new Rect(sig.RelX, sig.RelY, sig.RelWidth, sig.RelHeight), curW, curH, _targetPage.Rotation);

                bool isSelected = _selectedSignature == sig;

                var sigGrid = new Grid
                {
                    Width = Math.Max(20, screenRect.Width),
                    Height = Math.Max(15, screenRect.Height),
                    Tag = sig,
                    Background = Brushes.Transparent, // Crucial: enables full hit-testing over transparent PNGs/ink signatures!
                    Cursor = Cursors.SizeAll
                };

                // 1. Signature image content
                var img = new Image
                {
                    Source = sig.SignatureImage,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false
                };
                sigGrid.Children.Add(img);

                // Subtle hover border (visible when mouse is over signature)
                var hoverBorder = new Border
                {
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(15, 14, 165, 233)),
                    CornerRadius = new CornerRadius(2),
                    Visibility = Visibility.Collapsed,
                    IsHitTestVisible = false
                };
                sigGrid.Children.Add(hoverBorder);

                sigGrid.MouseEnter += (s, e) =>
                {
                    if (_selectedSignature != sig)
                    {
                        hoverBorder.Visibility = Visibility.Visible;
                    }
                };
                sigGrid.MouseLeave += (s, e) =>
                {
                    hoverBorder.Visibility = Visibility.Collapsed;
                };

                // 2. Selection Border & Handles
                if (isSelected)
                {
                    hoverBorder.Visibility = Visibility.Collapsed;

                    var selBorder = new Border
                    {
                        BorderBrush = (SolidColorBrush)Application.Current.Resources["PrimaryBlueBrush"],
                        BorderThickness = new Thickness(1.5),
                        Background = new SolidColorBrush(Color.FromArgb(20, 14, 165, 233)),
                        CornerRadius = new CornerRadius(2),
                        IsHitTestVisible = false
                    };
                    sigGrid.Children.Add(selBorder);

                    // Resize Handle (Bottom-Right)
                    var handle = new Rectangle
                    {
                        Width = 10,
                        Height = 10,
                        Fill = (SolidColorBrush)Application.Current.Resources["PrimaryBlueBrush"],
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Cursor = Cursors.SizeNWSE,
                        Margin = new Thickness(0, 0, -5, -5)
                    };
                    handle.MouseLeftButtonDown += (s, e) =>
                    {
                        e.Handled = true;
                        _isResizingSignature = true;
                        _sigDragStartMouse = e.GetPosition(WorkspaceContainer);
                        _sigDragStartRect = screenRect;
                        WorkspaceContainer.CaptureMouse();
                    };
                    sigGrid.Children.Add(handle);

                    // Delete Button ('✕')
                    var delBtn = new Button
                    {
                        Content = "✕",
                        Width = 18,
                        Height = 18,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Background = (SolidColorBrush)Application.Current.Resources["DangerRedBrush"],
                        BorderThickness = new Thickness(0),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -9, -9, 0),
                        Padding = new Thickness(0),
                        Cursor = Cursors.Hand,
                        ToolTip = "Delete signature (Del)"
                    };
                    delBtn.Click += (s, e) =>
                    {
                        e.Handled = true;
                        DeleteSignature(sig);
                    };
                    sigGrid.Children.Add(delBtn);
                }

                // Drag Start Handler & Selection
                sigGrid.MouseLeftButtonDown += (s, e) =>
                {
                    e.Handled = true;
                    SelectSignature(sig);
                    _isDraggingSignature = true;
                    _sigDragStartMouse = e.GetPosition(WorkspaceContainer);
                    _sigDragStartRect = screenRect;
                    WorkspaceContainer.CaptureMouse();
                };

                Canvas.SetLeft(sigGrid, screenRect.X);
                Canvas.SetTop(sigGrid, screenRect.Y);
                CnvSignatures.Children.Add(sigGrid);
            }
        }

        private void SelectSignature(PlacedSignatureItem sig)
        {
            _selectedSignature = sig;
            if (BtnDeleteSelectedSig != null) BtnDeleteSelectedSig.IsEnabled = true;
            TxtStatus.Text = "Signature selected";
            RenderSignatures();
        }

        private void DeselectSignature()
        {
            if (_selectedSignature != null)
            {
                _selectedSignature = null;
                if (BtnDeleteSelectedSig != null) BtnDeleteSelectedSig.IsEnabled = false;
                RenderSignatures();
            }
        }

        private void DeleteSignature(PlacedSignatureItem sig)
        {
            PushUndoSnapshot();
            _workingData.Signatures.Remove(sig);
            DeselectSignature();
            TxtStatus.Text = "Deleted signature";
        }

        private void BtnDeleteSelectedSig_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedSignature != null)
            {
                DeleteSignature(_selectedSignature);
            }
        }

        private void PageEditorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                if (FocusManager.GetFocusedElement(this) is TextBox || Keyboard.FocusedElement is TextBox)
                {
                    return;
                }

                if (_selectedSignature != null)
                {
                    DeleteSignature(_selectedSignature);
                    e.Handled = true;
                }
            }
        }

        private void BtnAddSignature_Click(object sender, RoutedEventArgs e)
        {
            // Open Signature Creation Window (Draw, Type, Upload, Symbol, Saved Library)
            var sigWin = new SignatureWindow(_targetPage) { Owner = this };
            bool? res = sigWin.ShowDialog();

            if (res == true && sigWin.ResultSignature != null)
            {
                PushUndoSnapshot();

                // Create and place signature in center of page by default
                var newSig = new PlacedSignatureItem
                {
                    SignatureImage = sigWin.ResultSignature.SignatureImage,
                    RelX = 0.35,
                    RelY = 0.40,
                    RelWidth = Math.Clamp(sigWin.ResultSignature.RelWidth, 0.15, 0.45),
                    RelHeight = Math.Clamp(sigWin.ResultSignature.RelHeight, 0.08, 0.25)
                };

                _workingData.Signatures.Add(newSig);
                SelectSignature(newSig);
                TxtStatus.Text = "Placed signature — drag or resize directly on page";
            }
        }

        #endregion

        #region Workspace Pointer Events (Drawing, Highlighting, Erasing, Signature Manipulation)

        private void Workspace_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_currentTool == EditorTool.Eraser)
            {
                EraserFollowerCircle.Visibility = Visibility.Visible;
            }
        }

        private void Workspace_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_currentTool == EditorTool.Eraser)
            {
                EraserFollowerCircle.Visibility = Visibility.Collapsed;
            }
        }

        private void Workspace_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point mouseP = e.GetPosition(WorkspaceContainer);
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;

            // Convert mouse screen position to unrotated base [0..1]
            Point baseP = EditorCoordinateService.ScreenToBasePoint(mouseP, curW, curH, _targetPage.Rotation);

            if (_currentTool == EditorTool.Pen)
            {
                _isInteracting = true;
                _activeStrokePoints.Clear();
                _activeStrokePoints.Add(baseP);
                WorkspaceContainer.CaptureMouse();
            }
            else if (_currentTool == EditorTool.Highlighter)
            {
                _isInteracting = true;
                if (_isRectangleHighlightMode)
                {
                    _isDrawingRectHighlight = true;
                    _rectHighlightStartMouse = mouseP;
                }
                else
                {
                    _activeStrokePoints.Clear();
                    _activeStrokePoints.Add(baseP);
                    _textHighlightStartPoint = mouseP;

                    // Dual-Mode Detection: Check if click started over any extracted PDF text line
                    var hitLine = _textLines.FirstOrDefault(tl =>
                    {
                        var exp = tl.NormalizedBounds;
                        exp.Inflate(0.005, 0.005);
                        return exp.Contains(baseP);
                    });
                    _textHighlightStartLine = hitLine;
                    _isTextHighlightLocked = hitLine != null;
                }

                WorkspaceContainer.CaptureMouse();
            }
            else if (_currentTool == EditorTool.Eraser)
            {
                PushUndoSnapshot();
                _isInteracting = true;
                WorkspaceContainer.CaptureMouse();
                EraseAtPoint(baseP, curW, curH);
            }
            else if (_currentTool == EditorTool.Signature)
            {
                DeselectSignature();
                _isInteracting = true;
                _isDrawingSignaturePlacement = true;
                _sigPlacementStartMouse = mouseP;
                WorkspaceContainer.CaptureMouse();
            }
            else if (_currentTool == EditorTool.FormText)
            {
                DeselectSignature();
            }
        }

        private void Workspace_MouseMove(object sender, MouseEventArgs e)
        {
            Point mouseP = e.GetPosition(WorkspaceContainer);
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;

            // Update Eraser Follower Cursor
            if (_currentTool == EditorTool.Eraser)
            {
                double r = (_activeEraserSize * _zoomFactor) / 2.0;
                Canvas.SetLeft(EraserFollowerCircle, mouseP.X - r);
                Canvas.SetTop(EraserFollowerCircle, mouseP.Y - r);
            }

            if (!_isInteracting && !_isDraggingSignature && !_isResizingSignature)
                return;

            Point baseP = EditorCoordinateService.ScreenToBasePoint(mouseP, curW, curH, _targetPage.Rotation);

            // Handle Signature Dragging
            if (_isDraggingSignature && _selectedSignature != null)
            {
                double dx = mouseP.X - _sigDragStartMouse.X;
                double dy = mouseP.Y - _sigDragStartMouse.Y;

                Rect newScreenRect = new Rect(
                    _sigDragStartRect.X + dx,
                    _sigDragStartRect.Y + dy,
                    _sigDragStartRect.Width,
                    _sigDragStartRect.Height
                );

                Rect newBaseRect = EditorCoordinateService.ScreenToBaseRect(newScreenRect, curW, curH, _targetPage.Rotation);
                _selectedSignature.RelX = Math.Clamp(newBaseRect.X, 0.0, 0.95);
                _selectedSignature.RelY = Math.Clamp(newBaseRect.Y, 0.0, 0.95);

                RenderSignatures();
                return;
            }

            // Handle Signature Resizing (Preserving Aspect Ratio - Requirement 10)
            if (_isResizingSignature && _selectedSignature != null)
            {
                double dx = mouseP.X - _sigDragStartMouse.X;
                double newW = Math.Max(30, _sigDragStartRect.Width + dx);
                double aspectRatio = _sigDragStartRect.Width / Math.Max(1, _sigDragStartRect.Height);
                double newH = newW / aspectRatio;

                Rect newScreenRect = new Rect(_sigDragStartRect.X, _sigDragStartRect.Y, newW, newH);
                Rect newBaseRect = EditorCoordinateService.ScreenToBaseRect(newScreenRect, curW, curH, _targetPage.Rotation);

                _selectedSignature.RelWidth = Math.Clamp(newBaseRect.Width, 0.05, 0.95);
                _selectedSignature.RelHeight = Math.Clamp(newBaseRect.Height, 0.02, 0.95);

                RenderSignatures();
                return;
            }

            // Handle Signature Placement Box Drawing
            if (_isDrawingSignaturePlacement && _currentTool == EditorTool.Signature)
            {
                double x = Math.Min(_sigPlacementStartMouse.X, mouseP.X);
                double y = Math.Min(_sigPlacementStartMouse.Y, mouseP.Y);
                double w = Math.Abs(mouseP.X - _sigPlacementStartMouse.X);
                double h = Math.Abs(mouseP.Y - _sigPlacementStartMouse.Y);

                CnvDraftOverlay.Children.Clear();
                var rect = new Rectangle
                {
                    Width = Math.Max(2, w),
                    Height = Math.Max(2, h),
                    Fill = new SolidColorBrush(Color.FromArgb(40, 14, 165, 233)),
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0EA5E9")),
                    StrokeThickness = 2.0,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                CnvDraftOverlay.Children.Add(rect);
                return;
            }

            // Handle Rectangle Highlight Mode Drawing
            if (_isDrawingRectHighlight && _currentTool == EditorTool.Highlighter)
            {
                double x = Math.Min(_rectHighlightStartMouse.X, mouseP.X);
                double y = Math.Min(_rectHighlightStartMouse.Y, mouseP.Y);
                double w = Math.Abs(mouseP.X - _rectHighlightStartMouse.X);
                double h = Math.Abs(mouseP.Y - _rectHighlightStartMouse.Y);

                CnvDraftOverlay.Children.Clear();
                var rect = new Rectangle
                {
                    Width = Math.Max(2, w),
                    Height = Math.Max(2, h),
                    Fill = new SolidColorBrush(Color.FromArgb(100, 250, 204, 21)),
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAB308")),
                    StrokeThickness = 1.0,
                    StrokeDashArray = new DoubleCollection { 4, 2 },
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                CnvDraftOverlay.Children.Add(rect);
                return;
            }

            // Handle Pen Drawing
            if (_currentTool == EditorTool.Pen)
            {
                _activeStrokePoints.Add(baseP);
                RenderDraftPenStroke(curW, curH);
            }
            // Handle Highlighter (Dual-Mode)
            else if (_currentTool == EditorTool.Highlighter)
            {
                if (_isTextHighlightLocked)
                {
                    // Mode A: Text Selection Mode — Show selection overlay ONLY over text lines
                    RenderDraftTextSelectionOverlay(_textHighlightStartPoint, mouseP, curW, curH);
                }
                else
                {
                    // Mode B: Freehand Highlight Ribbon
                    _activeStrokePoints.Add(baseP);
                    RenderDraftFreehandHighlight(curW, curH);
                }
            }
            // Handle Eraser
            else if (_currentTool == EditorTool.Eraser)
            {
                EraseAtPoint(baseP, curW, curH);
            }
        }

        private void Workspace_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingSignature || _isResizingSignature)
            {
                _isDraggingSignature = false;
                _isResizingSignature = false;
                WorkspaceContainer.ReleaseMouseCapture();
                return;
            }

            if (!_isInteracting) return;
            _isInteracting = false;
            WorkspaceContainer.ReleaseMouseCapture();

            Point mouseP = e.GetPosition(WorkspaceContainer);
            double curW = WorkspaceContainer.Width;
            double curH = WorkspaceContainer.Height;
            Point baseP = EditorCoordinateService.ScreenToBasePoint(mouseP, curW, curH, _targetPage.Rotation);

            CnvDraftOverlay.Children.Clear();

            // Handle Signature Placement Box Drawn -> Open Signature Dialog directly
            if (_isDrawingSignaturePlacement && _currentTool == EditorTool.Signature)
            {
                _isDrawingSignaturePlacement = false;

                double sx = Math.Min(_sigPlacementStartMouse.X, mouseP.X);
                double sy = Math.Min(_sigPlacementStartMouse.Y, mouseP.Y);
                double sw = Math.Abs(mouseP.X - _sigPlacementStartMouse.X);
                double sh = Math.Abs(mouseP.Y - _sigPlacementStartMouse.Y);

                if (sw < 15 && sh < 15)
                {
                    sw = Math.Max(80, 180 * _zoomFactor);
                    sh = Math.Max(30, 60 * _zoomFactor);
                    sx = Math.Max(0, sx - sw / 2.0);
                    sy = Math.Max(0, sy - sh / 2.0);
                }

                Rect screenRect = new Rect(sx, sy, sw, sh);
                Rect baseRect = EditorCoordinateService.ScreenToBaseRect(screenRect, curW, curH, _targetPage.Rotation);

                double safeX = Math.Clamp(baseRect.X, 0.0, 0.95);
                double safeY = Math.Clamp(baseRect.Y, 0.0, 0.95);
                double safeW = Math.Clamp(baseRect.Width, 0.03, 1.0 - safeX);
                double safeH = Math.Clamp(baseRect.Height, 0.015, 1.0 - safeY);
                Rect safeBaseRect = new Rect(safeX, safeY, safeW, safeH);

                var sigWin = new SignatureWindow(_targetPage, safeBaseRect) { Owner = this };
                if (sigWin.ShowDialog() == true && sigWin.ResultSignature?.SignatureImage != null)
                {
                    PushUndoSnapshot();
                    var newSig = new PlacedSignatureItem
                    {
                        SignatureImage = sigWin.ResultSignature.SignatureImage,
                        RelX = safeBaseRect.X,
                        RelY = safeBaseRect.Y,
                        RelWidth = safeBaseRect.Width,
                        RelHeight = safeBaseRect.Height
                    };
                    _workingData.Signatures.Add(newSig);
                    SelectSignature(newSig);
                    TxtStatus.Text = "Signature placed";
                }
                return;
            }

            // Commit Rectangle Highlight
            if (_isDrawingRectHighlight && _currentTool == EditorTool.Highlighter)
            {
                _isDrawingRectHighlight = false;

                double x = Math.Min(_rectHighlightStartMouse.X, mouseP.X);
                double y = Math.Min(_rectHighlightStartMouse.Y, mouseP.Y);
                double w = Math.Abs(mouseP.X - _rectHighlightStartMouse.X);
                double h = Math.Abs(mouseP.Y - _rectHighlightStartMouse.Y);

                if (w > 3 && h > 3)
                {
                    Rect screenRect = new Rect(x, y, w, h);
                    Rect baseRect = EditorCoordinateService.ScreenToBaseRect(screenRect, curW, curH, _targetPage.Rotation);

                    double safeX = Math.Clamp(baseRect.X, 0.0, 0.999);
                    double safeY = Math.Clamp(baseRect.Y, 0.0, 0.999);
                    double safeW = Math.Clamp(baseRect.Width, 0.001, 1.0 - safeX);
                    double safeH = Math.Clamp(baseRect.Height, 0.001, 1.0 - safeY);

                    PushUndoSnapshot();
                    _workingData.TextHighlights.Add(new TextHighlightItem
                    {
                        LineRects = new List<Rect> { new Rect(safeX, safeY, safeW, safeH) },
                        ColorHex = "#FACC15",
                        Opacity = 0.40
                    });
                    RenderCommittedHighlights();
                }
                return;
            }

            // Commit Pen Stroke
            if (_currentTool == EditorTool.Pen && _activeStrokePoints.Count >= 2)
            {
                PushUndoSnapshot();
                _workingData.PenStrokes.Add(new DrawingStroke
                {
                    Type = StrokeType.Pen,
                    Points = new List<Point>(_activeStrokePoints),
                    ColorHex = _activePenColor,
                    DisplayPixelThickness = _activePenWidth,
                    NormalizedThickness = _activePenWidth / Math.Max(1, DisplayedWidth),
                    Opacity = 1.0
                });
                RenderCommittedDrawings();
            }
            // Commit Highlighter
            else if (_currentTool == EditorTool.Highlighter)
            {
                if (_isTextHighlightLocked)
                {
                    Point startBase = EditorCoordinateService.ScreenToBasePoint(_textHighlightStartPoint, curW, curH, _targetPage.Rotation);
                    Point endBase = baseP;

                    var matchingLines = GetSelectedTextLineRects(startBase, endBase, _textHighlightStartLine);

                    if (matchingLines.Count > 0)
                    {
                        PushUndoSnapshot();
                        _workingData.TextHighlights.Add(new TextHighlightItem
                        {
                            LineRects = matchingLines,
                            ColorHex = "#FACC15",
                            Opacity = 0.40
                        });
                        RenderCommittedHighlights();
                    }
                }
                else if (_activeStrokePoints.Count >= 2)
                {
                    // Commit Freehand Ribbon
                    PushUndoSnapshot();
                    _workingData.FreehandHighlights.Add(new DrawingStroke
                    {
                        Type = StrokeType.FreehandHighlight,
                        Points = new List<Point>(_activeStrokePoints),
                        ColorHex = "#FACC15",
                        DisplayPixelThickness = _activeHlWidth,
                        NormalizedThickness = _activeHlWidth / Math.Max(1, DisplayedWidth),
                        Opacity = 0.38
                    });
                    RenderCommittedHighlights();
                }
            }

            _activeStrokePoints.Clear();
            _isTextHighlightLocked = false;
            _textHighlightStartLine = null;
            _isDrawingRectHighlight = false;
        }

        private void RenderDraftPenStroke(double curW, double curH)
        {
            CnvDraftOverlay.Children.Clear();
            if (_activeStrokePoints.Count < 2) return;

            Color col;
            try { col = (Color)ColorConverter.ConvertFromString(_activePenColor); }
            catch { col = Colors.Black; }

            double strokePx = Math.Max(1, _activePenWidth * _zoomFactor);
            var brush = new SolidColorBrush(col);
            brush.Freeze();

            var geom = CreateSmoothedPathGeometry(_activeStrokePoints, curW, curH, _targetPage.Rotation);
            var path = new Path
            {
                Data = geom,
                Stroke = brush,
                StrokeThickness = strokePx,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            CnvDraftOverlay.Children.Add(path);
        }

        private void RenderDraftFreehandHighlight(double curW, double curH)
        {
            CnvDraftOverlay.Children.Clear();
            if (_activeStrokePoints.Count < 2) return;

            Color col = (Color)ColorConverter.ConvertFromString("#FACC15");
            col.A = (byte)(255 * 0.38);

            double strokePx = Math.Max(4, _activeHlWidth * _zoomFactor);
            var brush = new SolidColorBrush(col);
            brush.Freeze();

            var geom = CreateSmoothedPathGeometry(_activeStrokePoints, curW, curH, _targetPage.Rotation);
            var path = new Path
            {
                Data = geom,
                Stroke = brush,
                StrokeThickness = strokePx,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
            CnvDraftOverlay.Children.Add(path);
        }

        private List<Rect> GetSelectedTextLineRects(Point startBase, Point currBase, ExtractedTextLine? startLine)
        {
            var result = new List<Rect>();
            if (_textLines.Count == 0) return result;

            if (startLine == null)
            {
                startLine = _textLines.OrderBy(tl =>
                {
                    double dx = Math.Max(0, Math.Max(tl.NormalizedBounds.Left - startBase.X, startBase.X - tl.NormalizedBounds.Right));
                    double dy = Math.Max(0, Math.Max(tl.NormalizedBounds.Top - startBase.Y, startBase.Y - tl.NormalizedBounds.Bottom));
                    return dx * dx + dy * dy;
                }).FirstOrDefault();
                if (startLine == null) return result;
            }

            // Determine which line the pointer is currently on
            ExtractedTextLine? endLine = null;

            // 1. Check if pointer is within vertical bounds of startLine (with a small hysteresis buffer)
            double lineH = startLine.NormalizedBounds.Height;
            if (currBase.Y >= startLine.NormalizedBounds.Top - lineH * 0.25 &&
                currBase.Y <= startLine.NormalizedBounds.Bottom + lineH * 0.25)
            {
                endLine = startLine;
            }
            else
            {
                // 2. Find if pointer is directly inside any other text line
                endLine = _textLines.FirstOrDefault(tl =>
                    currBase.Y >= tl.NormalizedBounds.Top && currBase.Y <= tl.NormalizedBounds.Bottom);

                // 3. If in vertical gap between lines, snap to closest line ONLY if past the gap midpoint
                if (endLine == null)
                {
                    if (currBase.Y > startLine.NormalizedBounds.Bottom)
                    {
                        // Dragging downwards
                        var nextLine = _textLines
                            .Where(tl => tl.NormalizedBounds.Top >= startLine.NormalizedBounds.Bottom)
                            .OrderBy(tl => tl.NormalizedBounds.Top)
                            .FirstOrDefault();

                        if (nextLine != null)
                        {
                            double midGap = (startLine.NormalizedBounds.Bottom + nextLine.NormalizedBounds.Top) / 2.0;
                            endLine = currBase.Y >= midGap ? nextLine : startLine;
                        }
                        else
                        {
                            endLine = startLine;
                        }
                    }
                    else
                    {
                        // Dragging upwards
                        var prevLine = _textLines
                            .Where(tl => tl.NormalizedBounds.Bottom <= startLine.NormalizedBounds.Top)
                            .OrderByDescending(tl => tl.NormalizedBounds.Bottom)
                            .FirstOrDefault();

                        if (prevLine != null)
                        {
                            double midGap = (prevLine.NormalizedBounds.Bottom + startLine.NormalizedBounds.Top) / 2.0;
                            endLine = currBase.Y <= midGap ? prevLine : startLine;
                        }
                        else
                        {
                            endLine = startLine;
                        }
                    }
                }
            }

            if (endLine == null) endLine = startLine;

            // Single-Line Selection (Horizontal drag on the same row)
            if (endLine == startLine)
            {
                double sX = Math.Min(startBase.X, currBase.X);
                double eX = Math.Max(startBase.X, currBase.X);
                sX = Math.Clamp(sX, startLine.NormalizedBounds.Left, startLine.NormalizedBounds.Right);
                eX = Math.Clamp(eX, startLine.NormalizedBounds.Left, startLine.NormalizedBounds.Right);

                if (eX > sX + 0.0005)
                {
                    result.Add(new Rect(sX, startLine.NormalizedBounds.Top, eX - sX, startLine.NormalizedBounds.Height));
                }
                else
                {
                    result.Add(new Rect(Math.Clamp(startBase.X, startLine.NormalizedBounds.Left, startLine.NormalizedBounds.Right),
                                        startLine.NormalizedBounds.Top,
                                        Math.Min(0.005, startLine.NormalizedBounds.Width),
                                        startLine.NormalizedBounds.Height));
                }
                return result;
            }

            // Multi-Line Selection
            double startMidY = startLine.NormalizedBounds.Top + startLine.NormalizedBounds.Height / 2.0;
            double endMidY = endLine.NormalizedBounds.Top + endLine.NormalizedBounds.Height / 2.0;
            bool draggingDownwards = endMidY >= startMidY;

            double topBound = Math.Min(startLine.NormalizedBounds.Top, endLine.NormalizedBounds.Top);
            double bottomBound = Math.Max(startLine.NormalizedBounds.Bottom, endLine.NormalizedBounds.Bottom);

            var selectedLines = _textLines.Where(tl =>
            {
                double midY = tl.NormalizedBounds.Top + tl.NormalizedBounds.Height / 2.0;
                return midY >= topBound && midY <= bottomBound;
            }).OrderBy(tl => tl.NormalizedBounds.Top).ToList();

            if (!selectedLines.Contains(startLine)) selectedLines.Add(startLine);
            if (!selectedLines.Contains(endLine)) selectedLines.Add(endLine);
            selectedLines = selectedLines.OrderBy(tl => tl.NormalizedBounds.Top).ToList();

            for (int i = 0; i < selectedLines.Count; i++)
            {
                var tl = selectedLines[i];
                if (i == 0)
                {
                    if (draggingDownwards)
                    {
                        double sX = Math.Clamp(startBase.X, tl.NormalizedBounds.Left, tl.NormalizedBounds.Right);
                        double eX = tl.NormalizedBounds.Right;
                        if (eX > sX) result.Add(new Rect(sX, tl.NormalizedBounds.Top, eX - sX, tl.NormalizedBounds.Height));
                    }
                    else
                    {
                        double sX = Math.Clamp(currBase.X, tl.NormalizedBounds.Left, tl.NormalizedBounds.Right);
                        double eX = tl.NormalizedBounds.Right;
                        if (eX > sX) result.Add(new Rect(sX, tl.NormalizedBounds.Top, eX - sX, tl.NormalizedBounds.Height));
                    }
                }
                else if (i == selectedLines.Count - 1)
                {
                    if (draggingDownwards)
                    {
                        double sX = tl.NormalizedBounds.Left;
                        double eX = Math.Clamp(currBase.X, tl.NormalizedBounds.Left, tl.NormalizedBounds.Right);
                        if (eX > sX) result.Add(new Rect(sX, tl.NormalizedBounds.Top, eX - sX, tl.NormalizedBounds.Height));
                    }
                    else
                    {
                        double sX = tl.NormalizedBounds.Left;
                        double eX = Math.Clamp(startBase.X, tl.NormalizedBounds.Left, tl.NormalizedBounds.Right);
                        if (eX > sX) result.Add(new Rect(sX, tl.NormalizedBounds.Top, eX - sX, tl.NormalizedBounds.Height));
                    }
                }
                else
                {
                    result.Add(tl.NormalizedBounds);
                }
            }

            return result;
        }

        private void RenderDraftTextSelectionOverlay(Point start, Point curr, double curW, double curH)
        {
            CnvDraftOverlay.Children.Clear();

            Point startBase = EditorCoordinateService.ScreenToBasePoint(start, curW, curH, _targetPage.Rotation);
            Point currBase = EditorCoordinateService.ScreenToBasePoint(curr, curW, curH, _targetPage.Rotation);

            var lineRects = GetSelectedTextLineRects(startBase, currBase, _textHighlightStartLine);

            var hlFill = new SolidColorBrush(Color.FromArgb(120, 250, 204, 21)); // Translucent yellow
            hlFill.Freeze();
            var hlStroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EAB308"));
            hlStroke.Freeze();

            foreach (var rectBase in lineRects)
            {
                Rect screenR = EditorCoordinateService.BaseToScreenRect(rectBase, curW, curH, _targetPage.Rotation);

                var rectShape = new Rectangle
                {
                    Width = Math.Max(2, screenR.Width),
                    Height = Math.Max(2, screenR.Height),
                    Fill = hlFill,
                    Stroke = hlStroke,
                    StrokeThickness = 1,
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetLeft(rectShape, screenR.X);
                Canvas.SetTop(rectShape, screenR.Y);
                CnvDraftOverlay.Children.Add(rectShape);
            }
        }

        private static List<DrawingStroke> EraseStrokeSegment(DrawingStroke stroke, Point baseCenter, double normalizedRadius)
        {
            var result = new List<DrawingStroke>();
            if (stroke.Points.Count == 0) return result;

            double maxStep = normalizedRadius * 0.4;
            var densePoints = new List<Point> { stroke.Points[0] };

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                Point p1 = stroke.Points[i - 1];
                Point p2 = stroke.Points[i];
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist > maxStep && maxStep > 0.0001)
                {
                    int steps = (int)Math.Ceiling(dist / maxStep);
                    for (int s = 1; s < steps; s++)
                    {
                        double t = (double)s / steps;
                        densePoints.Add(new Point(p1.X + t * dx, p1.Y + t * dy));
                    }
                }
                densePoints.Add(p2);
            }

            double rSq = normalizedRadius * normalizedRadius;
            var currentSegment = new List<Point>();

            foreach (var pt in densePoints)
            {
                double dSq = (pt.X - baseCenter.X) * (pt.X - baseCenter.X) + (pt.Y - baseCenter.Y) * (pt.Y - baseCenter.Y);
                if (dSq > rSq)
                {
                    currentSegment.Add(pt);
                }
                else
                {
                    if (currentSegment.Count >= 2)
                    {
                        var sub = stroke.Clone();
                        sub.Points = new List<Point>(currentSegment);
                        result.Add(sub);
                    }
                    currentSegment.Clear();
                }
            }

            if (currentSegment.Count >= 2)
            {
                var sub = stroke.Clone();
                sub.Points = new List<Point>(currentSegment);
                result.Add(sub);
            }

            return result;
        }

        private void EraseAtPoint(Point baseCenter, double curW, double curH)
        {
            double normalizedRadius = (_activeEraserSize / 2.0) / Math.Max(1, DisplayedWidth);
            bool modified = false;

            // Erase pen strokes intersecting eraser circle (segment-level erasing)
            var newPenStrokes = new List<DrawingStroke>();
            foreach (var stroke in _workingData.PenStrokes)
            {
                bool hits = stroke.Points.Any(p => Math.Pow(p.X - baseCenter.X, 2) + Math.Pow(p.Y - baseCenter.Y, 2) <= Math.Pow(normalizedRadius, 2));
                if (hits)
                {
                    var segments = EraseStrokeSegment(stroke, baseCenter, normalizedRadius);
                    newPenStrokes.AddRange(segments);
                    modified = true;
                }
                else
                {
                    newPenStrokes.Add(stroke);
                }
            }
            _workingData.PenStrokes = newPenStrokes;

            // Erase freehand highlights intersecting eraser circle (segment-level erasing)
            var newFhHighlights = new List<DrawingStroke>();
            foreach (var hl in _workingData.FreehandHighlights)
            {
                bool hits = hl.Points.Any(p => Math.Pow(p.X - baseCenter.X, 2) + Math.Pow(p.Y - baseCenter.Y, 2) <= Math.Pow(normalizedRadius, 2));
                if (hits)
                {
                    var segments = EraseStrokeSegment(hl, baseCenter, normalizedRadius);
                    newFhHighlights.AddRange(segments);
                    modified = true;
                }
                else
                {
                    newFhHighlights.Add(hl);
                }
            }
            _workingData.FreehandHighlights = newFhHighlights;

            // Erase text highlights intersecting eraser circle
            for (int i = _workingData.TextHighlights.Count - 1; i >= 0; i--)
            {
                var th = _workingData.TextHighlights[i];
                if (th.LineRects.Any(r =>
                {
                    var exp = r;
                    exp.Inflate(normalizedRadius, normalizedRadius);
                    return exp.Contains(baseCenter);
                }))
                {
                    _workingData.TextHighlights.RemoveAt(i);
                    modified = true;
                }
            }

            if (modified)
            {
                RenderCommittedDrawings();
                RenderCommittedHighlights();
            }
        }

        #endregion

        #region Save & Done / Cancel

        private void BtnSaveDone_Click(object sender, RoutedEventArgs e)
        {
            // 1. Commit working data clone to target page
            _targetPage.EditorData = _workingData.Clone();

            // 2. Synchronize signatures into target page's PageSignatures collection
            _targetPage.PageSignatures.Clear();
            foreach (var sig in _workingData.Signatures)
            {
                _targetPage.PageSignatures.Add(sig.ToAppliedSignature());
            }

            // 3. Immediately invalidate thumbnail cache to update page card in gallery (Requirement 13)
            _targetPage.InvalidateThumbnailCache();

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Discard session changes without mutating committed page state (Requirement 1)
            DialogResult = false;
            Close();
        }

        #endregion
    }
}
