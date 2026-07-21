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

        private BitmapSource? _loadedImageSignature;
        private readonly string _signaturesFolderPath;

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

            // Set default stroke attributes for drawing
            InkSignCanvas.DefaultDrawingAttributes.Color = Colors.Black;
            InkSignCanvas.DefaultDrawingAttributes.Width = 3;
            InkSignCanvas.DefaultDrawingAttributes.Height = 3;
            InkSignCanvas.DefaultDrawingAttributes.FitToCurve = true;
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
                TxtWizardStepSubtitle.Text = "Draw a signature with your mouse, import an image file, or choose a saved signature from your library.";
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

        #region Step 2: Ink Drawing, Image Loading & Saved Signatures

        private void InkSignCanvas_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
        {
            _loadedImageSignature = null;
            ImgSignaturePreview.Source = null;
            ImgSignaturePreview.Visibility = Visibility.Collapsed;
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
            ImgSignaturePreview.Source = null;
            ImgSignaturePreview.Visibility = Visibility.Collapsed;
            LstSavedSignatures.UnselectAll();
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
                try
                {
                    using var stream = File.OpenRead(dialog.FileName);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    _loadedImageSignature = bitmap;
                    ImgSignaturePreview.Source = bitmap;
                    ImgSignaturePreview.Visibility = Visibility.Visible;
                    InkSignCanvas.Strokes.Clear();
                    LstSavedSignatures.UnselectAll();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to load signature image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnSaveSignatureToLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (InkSignCanvas.Strokes.Count == 0 && _loadedImageSignature == null)
            {
                MessageBox.Show(this, "Please draw a signature or load an image first.", "Empty Signature", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                ImgSignaturePreview.Source = selected.Image;
                ImgSignaturePreview.Visibility = Visibility.Visible;
                InkSignCanvas.Strokes.Clear();
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
                    ImgSignaturePreview.Source = null;
                    ImgSignaturePreview.Visibility = Visibility.Collapsed;
                }

                SavedSignatures.Remove(item);
                e.Handled = true;
            }
        }

        private BitmapSource GetCurrentSignatureBitmap()
        {
            if (_loadedImageSignature != null)
            {
                return _loadedImageSignature;
            }

            int width = (int)InkSignCanvas.ActualWidth;
            int height = (int)InkSignCanvas.ActualHeight;

            if (width <= 0) width = 600;
            if (height <= 0) height = 260;

            var renderTarget = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                VisualBrush vb = new VisualBrush(InkSignCanvas);
                dc.DrawRectangle(vb, null, new Rect(0, 0, width, height));
            }

            renderTarget.Render(visual);
            return renderTarget;
        }

        private void BtnFinishStep2_Click(object sender, RoutedEventArgs e)
        {
            if (InkSignCanvas.Strokes.Count == 0 && _loadedImageSignature == null)
            {
                MessageBox.Show(this, "Please draw a signature, load an image, or select a saved signature.", "No Signature", MessageBoxButton.OK, MessageBoxImage.Information);
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
