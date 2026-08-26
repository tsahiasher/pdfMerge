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
using pdfMerge.Services;

namespace pdfMerge.Views
{
    // SavedSignatureItem moved to Models/SavedSignatureItem.cs (Rec #3)

    public partial class SignatureWindow : Window
    {
        private readonly PdfPageItem _targetPage;
        private Rect _relativePlacementRect = new Rect(0.1, 0.1, 0.3, 0.15);

        private int _activeTabIndex = 0; // 0: Draw, 1: Type, 2: Upload, 3: Symbol
        private BitmapSource? _loadedImageSignature;
        private readonly string _signaturesFolderPath;

        private FontFamily _selectedFontFamily = new FontFamily("Segoe Script");
        private string _selectedSymbol = "✔";

        private bool _isUpdatingLibrarySelection = false;

        public ObservableCollection<SavedSignatureItem> SavedSignatures { get; } = new ObservableCollection<SavedSignatureItem>();
        public PlacedSignatureItem? ResultSignature { get; private set; }

        public SignatureWindow(PdfPageItem page, Rect? directPlacementRect = null)
        {
            InitializeComponent();
            _targetPage = page;
            _relativePlacementRect = directPlacementRect ?? new Rect(0.35, 0.40, 0.30, 0.15);

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

        #region Tab Navigation (Draw, Type, Upload, Symbol)

        private void UnselectLibrary()
        {
            if (_isUpdatingLibrarySelection || LstSavedSignatures == null) return;
            _isUpdatingLibrarySelection = true;
            LstSavedSignatures.UnselectAll();
            _isUpdatingLibrarySelection = false;
        }

        private void SwitchTab(int tabIndex)
        {
            _activeTabIndex = tabIndex;

            // Reset tab button visual styles
            SetTabButtonStyle(BtnTabDraw, tabIndex == 0);
            SetTabButtonStyle(BtnTabType, tabIndex == 1);
            SetTabButtonStyle(BtnTabUpload, tabIndex == 2);
            SetTabButtonStyle(BtnTabSymbol, tabIndex == 3);

            // Show active panel
            if (PnlTabDraw != null) PnlTabDraw.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (PnlTabType != null) PnlTabType.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (PnlTabUpload != null) PnlTabUpload.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (PnlTabSymbol != null) PnlTabSymbol.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

            if (!_isUpdatingLibrarySelection)
            {
                UnselectLibrary();
            }
        }

        private void SetTabButtonStyle(Button btn, bool isActive)
        {
            btn.Foreground = (SolidColorBrush)Application.Current.Resources[isActive ? "PrimaryBlueBrush" : "MutedFgBrush"];
            btn.BorderBrush = isActive ? (SolidColorBrush)Application.Current.Resources["PrimaryBlueBrush"] : Brushes.Transparent;
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
            UnselectLibrary();
        }

        private void BtnUndoInk_Click(object sender, RoutedEventArgs e)
        {
            if (InkSignCanvas.Strokes.Count > 0)
            {
                InkSignCanvas.Strokes.RemoveAt(InkSignCanvas.Strokes.Count - 1);
            }
            UnselectLibrary();
        }

        private void BtnClearInk_Click(object sender, RoutedEventArgs e)
        {
            InkSignCanvas.Strokes.Clear();
            _loadedImageSignature = null;
            UnselectLibrary();
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
            UnselectLibrary();
            if (sender is Button btn && btn.Tag is string fontName)
            {
                _selectedFontFamily = new FontFamily(fontName);

                foreach (UIElement child in PnlFontPills.Children)
                {
                    if (child is Button b)
                    {
                        bool isSelected = fontName.Equals(b.Tag as string);
                        b.Background = (SolidColorBrush)Application.Current.Resources[isSelected ? "PrimaryBlueBrush" : "SubtleBorderBrush"];
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
            UnselectLibrary();
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
                if (files.Length > 0 && PdfService.IsSupportedImageFile(files[0]))
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
                if (files.Length > 0 && PdfService.IsSupportedImageFile(files[0]))
                {
                    LoadImageFromFile(files[0]);
                }
            }
        }

        // IsSupportedImageFile removed — using PdfService.IsSupportedImageFile instead (Rec #12)

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
                UnselectLibrary();
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
            UnselectLibrary();
            if (sender is Button btn && btn.Tag is string symbol)
            {
                _selectedSymbol = symbol;
                TxtSymbolPreview.Text = symbol;

                // Symbols are Black Only per user request
                TxtSymbolPreview.Foreground = Brushes.Black;
                TxtSymbolPreview.FontSize = symbol == "APPROVED" ? 36 : 72;

                // Update button active state
                BtnSymbolCheck.Background = (SolidColorBrush)Application.Current.Resources[symbol == "✔" ? "PrimaryBlueBrush" : "SubtleBorderBrush"];
                BtnSymbolCross.Background = (SolidColorBrush)Application.Current.Resources[symbol == "✖" ? "PrimaryBlueBrush" : "SubtleBorderBrush"];
                BtnSymbolStar.Background = (SolidColorBrush)Application.Current.Resources[symbol == "★" ? "PrimaryBlueBrush" : "SubtleBorderBrush"];
                BtnSymbolApproved.Background = (SolidColorBrush)Application.Current.Resources[symbol == "APPROVED" ? "PrimaryBlueBrush" : "SubtleBorderBrush"];
            }
        }

        #endregion

        #region Saved Signatures Library & Persistence

        private void BtnSaveSignatureToLibrary_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BitmapSource? sigBitmap = GetCurrentSignatureBitmap();
                if (sigBitmap == null)
                {
                    MessageBox.Show(this, "Please provide a valid signature before saving to library.", "Cannot Save", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading saved signature '{file}': {ex.Message}");
                }
            }
        }

        private void LstSavedSignatures_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingLibrarySelection) return;

            if (LstSavedSignatures.SelectedItem is SavedSignatureItem selected)
            {
                _isUpdatingLibrarySelection = true;
                _loadedImageSignature = selected.Image;
                ImgUploadedPreview.Source = selected.Image;
                ImgUploadedPreview.Visibility = Visibility.Visible;
                PnlUploadInstructions.Visibility = Visibility.Collapsed;

                SwitchTab(2); // Switch to Upload tab so picked library signature is displayed in the main preview
                _isUpdatingLibrarySelection = false;
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
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error deleting saved signature: {ex.Message}");
                }

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

        private BitmapSource? GetCurrentSignatureBitmap()
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
                    return _loadedImageSignature;

                case 3: // Symbol
                    return RenderVisualToBitmap(GridSymbolPreview, 600, 150);

                case 0: // Draw
                default:
                    if (InkSignCanvas.Strokes.Count == 0 && _loadedImageSignature != null)
                    {
                        return _loadedImageSignature;
                    }
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
            if (LstSavedSignatures.SelectedItem == null)
            {
                if (_activeTabIndex == 0 && InkSignCanvas.Strokes.Count == 0 && _loadedImageSignature == null)
                {
                    MessageBox.Show(this, "Please draw a signature on the canvas.", "Empty Signature", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                else if (_activeTabIndex == 1 && (string.IsNullOrWhiteSpace(TxtTypedSignature.Text) || TxtTypedSignature.Text == "Signature Preview"))
                {
                    MessageBox.Show(this, "Please type your signature.", "Empty Signature", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                else if (_activeTabIndex == 2 && _loadedImageSignature == null)
                {
                    MessageBox.Show(this, "Please choose or drop an image file for your signature.", "No Image Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                else if (_activeTabIndex == 3 && string.IsNullOrEmpty(_selectedSymbol))
                {
                    MessageBox.Show(this, "Please select a symbol stamp.", "No Symbol Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            BitmapSource? finalSigBitmap = GetCurrentSignatureBitmap();
            if (finalSigBitmap == null)
            {
                MessageBox.Show(this, "Could not create signature bitmap. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Transform signature image to unrotated 0° base page orientation if page is currently rotated
            int rot = ((_targetPage.Rotation % 360) + 360) % 360;
            if (rot != 0)
            {
                finalSigBitmap = pdfMerge.Helpers.BitmapUtilities.RotateBitmap(finalSigBitmap, (360 - rot) % 360);
            }

            ResultSignature = new PlacedSignatureItem
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
