using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using pdfMerge.Models;

namespace pdfMerge.Views
{
    public class SavedSignatureItem
    {
        public string FilePath { get; set; } = string.Empty;
        public BitmapImage Image { get; set; } = null!;
    }

    public partial class SignatureWindow : Window
    {
        private readonly PdfPageItem _targetPage;
        private Point _rectStartPoint;
        private bool _isDrawingRect;
        private Rect _relativePlacementRect = new Rect(0.1, 0.1, 0.3, 0.15);

        private int _activeTabIndex = 0; // 0: Draw, 1: Type, 2: Upload, 3: Symbol
        private BitmapSource? _loadedImageSignature;
        private readonly string _signaturesFolderPath;

        private FontFamily _selectedFontFamily = new FontFamily("Segoe Script");
        private string _selectedSymbol = "✔";

        public ObservableCollection<SavedSignatureItem> SavedSignatures { get; } = new ObservableCollection<SavedSignatureItem>();
        public AppliedSignature? ResultSignature { get; private set; }

        public SignatureWindow(PdfPageItem page)
        {
            InitializeComponent();
            _targetPage = page;

            ImgPagePreview.Source = _targetPage.Thumbnail;
            if (_targetPage.Rotation != 0)
            {
                ImgPagePreview.RenderTransformOrigin = new Point(0.5, 0.5);
                ImgPagePreview.RenderTransform = new RotateTransform(_targetPage.Rotation);
            }

            _signaturesFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pdfMerge", "Signatures");
            Directory.CreateDirectory(_signaturesFolderPath);

            LstSavedSignatures.ItemsSource = SavedSignatures;
            LoadSavedSignatures();

            // Set default stroke attributes for drawing (Black Ink only)
            InkSignCanvas.DefaultDrawingAttributes.Color = Colors.Black;
            InkSignCanvas.DefaultDrawingAttributes.Width = 3;
            InkSignCanvas.DefaultDrawingAttributes.Height = 3;
            InkSignCanvas.DefaultDrawingAttributes.FitToCurve = true;

            InitializeFontPills();
            SwitchTab(0);
        }

        #region Step 1: Drag Signature Placement Box

        private void Step1Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _rectStartPoint = e.GetPosition(GridStep1Canvas);
            _isDrawingRect = true;

            Canvas.SetLeft(RectPlacement, _rectStartPoint.X);
            Canvas.SetTop(RectPlacement, _rectStartPoint.Y);
            RectPlacement.Width = 0;
            RectPlacement.Height = 0;
            RectPlacement.Visibility = Visibility.Visible;

            BtnContinueStep1.IsEnabled = false;
        }

        private void Step1Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawingRect && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(GridStep1Canvas);

                double x = Math.Min(_rectStartPoint.X, currentPoint.X);
                double y = Math.Min(_rectStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(_rectStartPoint.X - currentPoint.X);
                double height = Math.Abs(_rectStartPoint.Y - currentPoint.Y);

                Canvas.SetLeft(RectPlacement, x);
                Canvas.SetTop(RectPlacement, y);
                RectPlacement.Width = width;
                RectPlacement.Height = height;

                if (width > 15 && height > 15)
                {
                    BtnContinueStep1.IsEnabled = true;
                }
            }
        }

        private void Step1Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawingRect)
            {
                _isDrawingRect = false;

                if (RectPlacement.Width > 15 && RectPlacement.Height > 15)
                {
                    CalculateNormalizedPlacement();
                    BtnContinueStep1.IsEnabled = true;
                }
                else
                {
                    RectPlacement.Visibility = Visibility.Collapsed;
                    BtnContinueStep1.IsEnabled = false;
                }
            }
        }

        private void CalculateNormalizedPlacement()
        {
            try
            {
                double rectX = Canvas.GetLeft(RectPlacement);
                double rectY = Canvas.GetTop(RectPlacement);

                if (double.IsNaN(rectX)) rectX = 0;
                if (double.IsNaN(rectY)) rectY = 0;

                double rectW = RectPlacement.Width;
                double rectH = RectPlacement.Height;

                if (double.IsNaN(rectW) || rectW <= 0) rectW = 100;
                if (double.IsNaN(rectH) || rectH <= 0) rectH = 50;

                Point imgOffset = new Point(0, 0);
                try
                {
                    if (ImgPagePreview.IsVisible && GridStep1Canvas.IsAncestorOf(ImgPagePreview))
                    {
                        imgOffset = ImgPagePreview.TranslatePoint(new Point(0, 0), GridStep1Canvas);
                    }
                }
                catch { }

                double imgWidth = ImgPagePreview.ActualWidth;
                double imgHeight = ImgPagePreview.ActualHeight;

                if (imgWidth <= 0) imgWidth = Math.Max(1, GridStep1Canvas.ActualWidth);
                if (imgHeight <= 0) imgHeight = Math.Max(1, GridStep1Canvas.ActualHeight);

                double relX = Math.Max(0, (rectX - imgOffset.X) / imgWidth);
                double relY = Math.Max(0, (rectY - imgOffset.Y) / imgHeight);
                double relW = Math.Min(1.0 - relX, rectW / imgWidth);
                double relH = Math.Min(1.0 - relY, rectH / imgHeight);

                if (double.IsNaN(relX) || double.IsInfinity(relX)) relX = 0.1;
                if (double.IsNaN(relY) || double.IsInfinity(relY)) relY = 0.1;
                if (double.IsNaN(relW) || double.IsInfinity(relW) || relW <= 0) relW = 0.3;
                if (double.IsNaN(relH) || double.IsInfinity(relH) || relH <= 0) relH = 0.15;

                _relativePlacementRect = new Rect(
                    Math.Clamp(relX, 0, 0.95),
                    Math.Clamp(relY, 0, 0.95),
                    Math.Clamp(relW, 0.01, 1.0),
                    Math.Clamp(relH, 0.01, 1.0)
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calculating placement: {ex.Message}");
                _relativePlacementRect = new Rect(0.1, 0.1, 0.3, 0.15);
            }
        }

        private void BtnContinueStep1_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                CalculateNormalizedPlacement();

                PnlStep1.Visibility = Visibility.Collapsed;
                PnlStep2.Visibility = Visibility.Visible;

                BtnContinueStep1.Visibility = Visibility.Collapsed;
                BtnFinishStep2.Visibility = Visibility.Visible;
                BtnBackStep.Visibility = Visibility.Visible;

                TxtWizardStepTitle.Text = "Step 2 of 2: Create or Choose Signature";
                TxtWizardStepSubtitle.Text = "Draw, type, upload an image, or choose a symbol / saved signature.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error proceeding to Step 2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnBackStep_Click(object sender, RoutedEventArgs e)
        {
            PnlStep2.Visibility = Visibility.Collapsed;
            PnlStep1.Visibility = Visibility.Visible;

            BtnFinishStep2.Visibility = Visibility.Collapsed;
            BtnContinueStep1.Visibility = Visibility.Visible;
            BtnBackStep.Visibility = Visibility.Collapsed;

            TxtWizardStepTitle.Text = "Step 1 of 2: Draw Signature Placement Box";
            TxtWizardStepSubtitle.Text = "Click and drag your mouse on the page preview to specify where to place the signature.";
        }

        #endregion

        #region Step 2: Tab Navigation (Draw, Type, Upload, Symbol)

        private void SwitchTab(int tabIndex)
        {
            _activeTabIndex = tabIndex;

            // Reset tab button visual styles
            SetTabButtonStyle(BtnTabDraw, tabIndex == 0);
            SetTabButtonStyle(BtnTabType, tabIndex == 1);
            SetTabButtonStyle(BtnTabUpload, tabIndex == 2);
            SetTabButtonStyle(BtnTabSymbol, tabIndex == 3);

            // Show active panel
            PnlTabDraw.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            PnlTabType.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            PnlTabUpload.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            PnlTabSymbol.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetTabButtonStyle(Button btn, bool isActive)
        {
            btn.Foreground = new SolidColorBrush(isActive ? Color.FromRgb(14, 165, 233) : Color.FromRgb(148, 163, 184));
            btn.BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(14, 165, 233) : Colors.Transparent);
            btn.BorderThickness = new Thickness(0, 0, 0, isActive ? 3 : 0);
            btn.FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold;
        }

        private void BtnTabDraw_Click(object sender, RoutedEventArgs e) => SwitchTab(0);
        private void BtnTabType_Click(object sender, RoutedEventArgs e) => SwitchTab(1);
        private void BtnTabUpload_Click(object sender, RoutedEventArgs e) => SwitchTab(2);
        private void BtnTabSymbol_Click(object sender, RoutedEventArgs e) => SwitchTab(3);

        #endregion

        #region Tab 1: Draw (Ink Canvas & Thickness Bar)

        private void SldThickness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (InkSignCanvas != null && TxtThicknessValue != null)
            {
                double val = Math.Round(e.NewValue);
                TxtThicknessValue.Text = $"{val}px";

                InkSignCanvas.DefaultDrawingAttributes.Width = val;
                InkSignCanvas.DefaultDrawingAttributes.Height = val;
            }
        }

        private void InkSignCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            _loadedImageSignature = null;
            LstSavedSignatures.UnselectAll();
        }

        private void BtnUndoInk_Click(object sender, RoutedEventArgs e)
        {
            if (InkSignCanvas.Strokes.Count > 0)
            {
                InkSignCanvas.Strokes.RemoveAt(InkSignCanvas.Strokes.Count - 1);
            }
        }

        private void BtnClearInk_Click(object sender, RoutedEventArgs e)
        {
            InkSignCanvas.Strokes.Clear();
            _loadedImageSignature = null;
            LstSavedSignatures.UnselectAll();
        }

        #endregion

        #region Tab 2: Type (Handwriting Fonts for EN & HE & GotFocus Clear)

        private void InitializeFontPills()
        {
            string[] fontOptions = new[]
            {
                "Segoe Script",
                "Segoe Print",
                "Comic Sans MS",
                "Guttman Yad",
                "Lucida Handwriting",
                "Brush Script MT",
                "Arial"
            };

            PnlFontPills.Children.Clear();

            foreach (var fontName in fontOptions)
            {
                var btn = new Button
                {
                    Content = fontName,
                    FontFamily = new FontFamily(fontName),
                    FontSize = 13,
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 8, 6),
                    Background = new SolidColorBrush(fontName.Equals(_selectedFontFamily.Source) ? Color.FromRgb(14, 165, 233) : Color.FromRgb(51, 65, 85)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    Tag = fontName
                };

                btn.Click += FontPill_Click;
                PnlFontPills.Children.Add(btn);
            }

            UpdateTypedPreviewFont();
        }

        private void FontPill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fontName)
            {
                _selectedFontFamily = new FontFamily(fontName);

                foreach (UIElement child in PnlFontPills.Children)
                {
                    if (child is Button b)
                    {
                        bool isSelected = fontName.Equals(b.Tag as string);
                        b.Background = new SolidColorBrush(isSelected ? Color.FromRgb(14, 165, 233) : Color.FromRgb(51, 65, 85));
                        b.FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal;
                    }
                }

                UpdateTypedPreviewFont();
            }
        }

        private void TxtTypedSignature_GotFocus(object sender, RoutedEventArgs e)
        {
            if (TxtTypedSignature.Text == "Signature Preview")
            {
                TxtTypedSignature.Text = string.Empty;
            }
        }

        private void TxtTypedSignature_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtTypedSignature.Text))
            {
                TxtTypedSignature.Text = "Signature Preview";
            }
        }

        private void TxtTypedSignature_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtTypedPreview != null)
            {
                TxtTypedPreview.Text = string.IsNullOrWhiteSpace(TxtTypedSignature.Text) ? "Signature Preview" : TxtTypedSignature.Text;
            }
        }

        private void UpdateTypedPreviewFont()
        {
            if (TxtTypedPreview != null)
            {
                TxtTypedPreview.FontFamily = _selectedFontFamily;
            }
        }

        #endregion

        #region Tab 3: Upload Image Signature & Drag-Drop Zone

        private void BdrUploadDropZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            BtnLoadImageSignature_Click(sender, e);
        }

        private void BdrUploadDropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && IsSupportedImageFile(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void BdrUploadDropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && IsSupportedImageFile(files[0]))
                {
                    LoadImageFromFile(files[0]);
                }
            }
        }

        private bool IsSupportedImageFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
        }

        private void BtnLoadImageSignature_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                Title = "Select Signature Image"
            };

            if (dialog.ShowDialog(this) == true)
            {
                LoadImageFromFile(dialog.FileName);
            }
        }

        private void LoadImageFromFile(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                _loadedImageSignature = bitmap;
                ImgUploadedPreview.Source = bitmap;
                ImgUploadedPreview.Visibility = Visibility.Visible;
                PnlUploadInstructions.Visibility = Visibility.Collapsed;
                LstSavedSignatures.UnselectAll();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to load signature image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Tab 4: Symbol (Black Only)

        private void BtnSymbol_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string symbol)
            {
                _selectedSymbol = symbol;
                TxtSymbolPreview.Text = symbol;

                // Symbols are Black Only per user request
                TxtSymbolPreview.Foreground = Brushes.Black;
                TxtSymbolPreview.FontSize = symbol == "APPROVED" ? 36 : 72;

                // Update button active state
                BtnSymbolCheck.Background = new SolidColorBrush(symbol == "✔" ? Color.FromRgb(14, 165, 233) : Color.FromRgb(51, 65, 85));
                BtnSymbolCross.Background = new SolidColorBrush(symbol == "✖" ? Color.FromRgb(14, 165, 233) : Color.FromRgb(51, 65, 85));
                BtnSymbolStar.Background = new SolidColorBrush(symbol == "★" ? Color.FromRgb(14, 165, 233) : Color.FromRgb(51, 65, 85));
                BtnSymbolApproved.Background = new SolidColorBrush(symbol == "APPROVED" ? Color.FromRgb(14, 165, 233) : Color.FromRgb(51, 65, 85));
            }
        }

        #endregion

        #region Saved Signatures Library & Persistence

        private void BtnSaveSignatureToLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BitmapSource sigBitmap = GetCurrentSignatureBitmap();
                string filePath = Path.Combine(_signaturesFolderPath, $"Signature_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(sigBitmap));
                    encoder.Save(stream);
                }

                LoadSavedSignatures();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save signature: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSavedSignatures()
        {
            SavedSignatures.Clear();
            if (!Directory.Exists(_signaturesFolderPath)) return;

            var files = Directory.GetFiles(_signaturesFolderPath, "*.png").OrderByDescending(File.GetCreationTime);
            foreach (var file in files)
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    SavedSignatures.Add(new SavedSignatureItem
                    {
                        FilePath = file,
                        Image = bitmap
                    });
                }
                catch { }
            }
        }

        private void LstSavedSignatures_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstSavedSignatures.SelectedItem is SavedSignatureItem selected)
            {
                _loadedImageSignature = selected.Image;
            }
        }

        private void BtnDeleteSavedSignature_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SavedSignatureItem item)
            {
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                    }
                }
                catch { }

                if (_loadedImageSignature == item.Image)
                {
                    _loadedImageSignature = null;
                }

                SavedSignatures.Remove(item);
                e.Handled = true;
            }
        }

        #endregion

        #region Output Bitmap Rendering

        private BitmapSource GetCurrentSignatureBitmap()
        {
            if (LstSavedSignatures.SelectedItem is SavedSignatureItem selected)
            {
                return selected.Image;
            }

            switch (_activeTabIndex)
            {
                case 1: // Type
                    return RenderVisualToBitmap(GridTypedPreview, 600, 130);

                case 2: // Upload
                    if (_loadedImageSignature != null) return _loadedImageSignature;
                    return RenderVisualToBitmap(PnlUploadInstructions, 600, 180);

                case 3: // Symbol
                    return RenderVisualToBitmap(GridSymbolPreview, 600, 150);

                case 0: // Draw
                default:
                    if (_loadedImageSignature != null) return _loadedImageSignature;
                    return RenderVisualToBitmap(GridDrawCanvasArea, 600, 220);
            }
        }

        private BitmapSource RenderVisualToBitmap(Visual visual, int width, int height)
        {
            if (width <= 0) width = 600;
            if (height <= 0) height = 200;

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();

            using (DrawingContext dc = dv.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(visual) { Stretch = Stretch.Uniform };
                dc.DrawRectangle(vb, null, new Rect(0, 0, width, height));
            }

            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private void BtnFinishStep2_Click(object sender, RoutedEventArgs e)
        {
            if (_activeTabIndex == 0 && InkSignCanvas.Strokes.Count == 0 && _loadedImageSignature == null && LstSavedSignatures.SelectedItem == null)
            {
                MessageBox.Show(this, "Please draw a signature, type a signature, upload an image, or select a saved signature.", "Empty Signature", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BitmapSource finalSigBitmap = GetCurrentSignatureBitmap();

            ResultSignature = new AppliedSignature
            {
                SignatureImage = finalSigBitmap,
                RelX = _relativePlacementRect.X,
                RelY = _relativePlacementRect.Y,
                RelWidth = _relativePlacementRect.Width,
                RelHeight = _relativePlacementRect.Height
            };

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion
    }
}
