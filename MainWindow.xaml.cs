using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using pdfMerge.Models;
using pdfMerge.Services;

namespace pdfMerge
{
    public partial class MainWindow : Window
    {
        private readonly PdfService _pdfService = new PdfService();

        private Point _dragStartPoint;
        private bool _isDraggingPages;

        private Point _marqueeStartPoint;
        private bool _isSelectingWithMarquee;

        // Snapshot of original pages state for Revert All feature
        private List<PdfPageItem> _originalPagesSnapshot = new List<PdfPageItem>();
        private List<PdfPageItem>? _currentDraggedGroup;

        public ObservableCollection<PdfFileItem> Files { get; } = new ObservableCollection<PdfFileItem>();
        public ObservableCollection<PdfPageItem> Pages { get; } = new ObservableCollection<PdfPageItem>();

        public MainWindow()
        {
            InitializeComponent();

            LstFiles.ItemsSource = Files;
            LstPages.ItemsSource = Pages;

            Pages.CollectionChanged += Pages_CollectionChanged;

            UpdateUIState();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();

            // Bypass Environment.Exit(0) DLL detaching deadlocks and forcefully kill the process.
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        private void Pages_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (PdfPageItem item in e.NewItems)
                {
                    item.PropertyChanged += PageItem_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (PdfPageItem item in e.OldItems)
                {
                    item.PropertyChanged -= PageItem_PropertyChanged;
                }
            }
            UpdateUIState();
        }

        private void PageItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PdfPageItem.IsSelected))
            {
                UpdateUIState();
            }
        }

        #region Drag & Drop Window PDF and Image File Loading

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Any(f => PdfService.IsSupportedFile(f)))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }

            if (e.Data.GetDataPresent("PdfPageItems"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] droppedFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
                var supportedFiles = droppedFiles
                    .Where(f => PdfService.IsSupportedFile(f))
                    .ToList();

                if (supportedFiles.Any())
                {
                    await AddFilesAsync(supportedFiles);
                }
            }
        }

        #endregion

        #region File Management

        private async void BtnOpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "All Supported Files (*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.webp)|*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.webp|PDF Files (*.pdf)|*.pdf|Image Files (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All Files (*.*)|*.*",
                Multiselect = true,
                Title = "Select PDF or Image Files to Add"
            };

            if (dialog.ShowDialog(this) == true)
            {
                await AddFilesAsync(dialog.FileNames);
            }
        }

        private async Task AddFilesAsync(IEnumerable<string> filePaths)
        {
            SetLoadingState(true, "Processing files...");

            try
            {
                var newPagesList = new List<PdfPageItem>();

                foreach (var filePath in filePaths)
                {
                    if (Files.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    int pageCount = await _pdfService.GetPageCountAsync(filePath);

                    var fileItem = new PdfFileItem
                    {
                        FilePath = filePath,
                        PageCount = pageCount,
                        Order = Files.Count + 1
                    };

                    Files.Add(fileItem);

                    for (int pageIdx = 0; pageIdx < pageCount; pageIdx++)
                    {
                        BitmapSource? thumb = await _pdfService.RenderPageThumbnailAsync(filePath, pageIdx, 350);

                        var pageItem = new PdfPageItem
                        {
                            SourceFilePath = filePath,
                            OriginalPageIndex = pageIdx,
                            DisplayPageNumber = Pages.Count + newPagesList.Count + 1,
                            Thumbnail = thumb
                        };

                        ApplyZoomDimensionsToItem(pageItem, (int)SldZoom.Value);
                        newPagesList.Add(pageItem);
                    }
                }

                foreach (var page in newPagesList)
                {
                    Pages.Add(page);
                    _originalPagesSnapshot.Add(page.CloneSnapshot());
                }

                ReindexSequenceNumbers();
                UpdateUIState();

                SetLoadingState(false, $"Loaded {newPagesList.Count} page(s) from {filePaths.Count()} file(s)");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetLoadingState(false, "Failed to load files");
            }
        }

        private void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfFileItem fileItem)
            {
                var pagesToRemove = Pages.Where(p => p.SourceFilePath.Equals(fileItem.FilePath, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var page in pagesToRemove)
                {
                    Pages.Remove(page);
                }

                _originalPagesSnapshot.RemoveAll(p => p.SourceFilePath.Equals(fileItem.FilePath, StringComparison.OrdinalIgnoreCase));

                Files.Remove(fileItem);
                ReindexFilesOrder();
                ReindexSequenceNumbers();
                UpdateUIState();

                GC.Collect();
                GC.WaitForPendingFinalizers();

                TxtStatus.Text = $"Removed file '{fileItem.FileName}'";
            }
        }

        private void BtnMoveFileUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfFileItem fileItem)
            {
                int index = Files.IndexOf(fileItem);
                if (index > 0)
                {
                    if (!ConfirmReorderFilesIfPagesCustomized()) return;

                    Files.Move(index, index - 1);
                    ReindexFilesOrder();
                    RebuildPagesFromFilesOrder();
                }
            }
        }

        private void BtnMoveFileDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfFileItem fileItem)
            {
                int index = Files.IndexOf(fileItem);
                if (index >= 0 && index < Files.Count - 1)
                {
                    if (!ConfirmReorderFilesIfPagesCustomized()) return;

                    Files.Move(index, index + 1);
                    ReindexFilesOrder();
                    RebuildPagesFromFilesOrder();
                }
            }
        }

        private bool HasCustomPageOrder()
        {
            if (Pages.Count <= 1) return false;

            var fileOrderDict = Files.Select((f, idx) => new { f.FilePath, idx })
                                     .ToDictionary(x => x.FilePath, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var expectedOrder = Pages.OrderBy(p => fileOrderDict.TryGetValue(p.SourceFilePath, out int order) ? order : int.MaxValue)
                                     .ThenBy(p => p.OriginalPageIndex)
                                     .ToList();

            for (int i = 0; i < Pages.Count; i++)
            {
                if (Pages[i] != expectedOrder[i])
                {
                    return true;
                }
            }

            return false;
        }

        private bool ConfirmReorderFilesIfPagesCustomized()
        {
            if (HasCustomPageOrder())
            {
                var result = MessageBox.Show(
                    this,
                    "Reordering source files will reset all pages back to their original file order, undoing any custom page reordering you have done.\n\nDo you want to proceed?",
                    "Reset Custom Page Order?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                return result == MessageBoxResult.Yes;
            }
            return true;
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (Files.Count == 0 && Pages.Count == 0) return;

            if (MessageBox.Show(this, "Are you sure you want to clear all loaded files and pages?", "Clear All", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Files.Clear();
                Pages.Clear();
                _originalPagesSnapshot.Clear();
                UpdateUIState();
                
                GC.Collect();
                GC.WaitForPendingFinalizers();

                TxtStatus.Text = "Cleared all files";
            }
        }

        private void BtnRevert_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0 && _originalPagesSnapshot.Count == 0) return;

            if (MessageBox.Show(this, "Revert all changes and reload original files?", "Revert All Changes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Pages.Clear();
                foreach (var snap in _originalPagesSnapshot)
                {
                    var restored = snap.CloneSnapshot();
                    ApplyZoomDimensionsToItem(restored, (int)SldZoom.Value);
                    Pages.Add(restored);
                }

                ReindexSequenceNumbers();
                UpdateUIState();
                TxtStatus.Text = "Reverted all changes to original files";
            }
        }

        #endregion

        #region Zoom Slider Handling

        private void SldZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int zoomLevel = (int)e.NewValue;
            foreach (var page in Pages)
            {
                ApplyZoomDimensionsToItem(page, zoomLevel);
            }
        }

        private void ApplyZoomDimensionsToItem(PdfPageItem page, int zoomLevel)
        {
            switch (zoomLevel)
            {
                case 1: // Small
                    page.CardWidth = 160;
                    page.CardHeight = 235;
                    page.ImageMaxHeight = 140;
                    page.ImageMaxWidth = 130;
                    break;
                case 3: // Large
                    page.CardWidth = 260;
                    page.CardHeight = 380;
                    page.ImageMaxHeight = 275;
                    page.ImageMaxWidth = 225;
                    break;
                case 4: // Extra Large
                    page.CardWidth = 320;
                    page.CardHeight = 460;
                    page.ImageMaxHeight = 340;
                    page.ImageMaxWidth = 280;
                    break;
                case 2: // Medium (Default)
                default:
                    page.CardWidth = 205;
                    page.CardHeight = 305;
                    page.ImageMaxHeight = 205;
                    page.ImageMaxWidth = 175;
                    break;
            }
        }

        #endregion

        #region Marquee Selection (Rubber-Band Box)

        private void LstPages_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(LstPages);

            // Bypass marquee selection if clicking on ScrollBar controls
            HitTestResult hitTest = VisualTreeHelper.HitTest(GridGalleryContainer, e.GetPosition(GridGalleryContainer));
            if (hitTest != null && IsClickOnScrollBar(hitTest.VisualHit))
            {
                return;
            }

            var hitResult = GetListBoxItemAtPosition(mousePos);
            if (hitResult == null)
            {
                _isSelectingWithMarquee = true;
                _marqueeStartPoint = mousePos;

                Canvas.SetLeft(SelectionRectangle, mousePos.X);
                Canvas.SetTop(SelectionRectangle, mousePos.Y);
                SelectionRectangle.Width = 0;
                SelectionRectangle.Height = 0;
                SelectionRectangle.Visibility = Visibility.Visible;

                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    foreach (var page in Pages)
                    {
                        page.IsSelected = false;
                    }
                }
            }
        }

        private bool IsClickOnScrollBar(DependencyObject visual)
        {
            DependencyObject obj = visual;
            while (obj != null && obj != GridGalleryContainer)
            {
                if (obj is System.Windows.Controls.Primitives.ScrollBar ||
                    obj is System.Windows.Controls.Primitives.Thumb ||
                    obj is System.Windows.Controls.Primitives.RepeatButton)
                {
                    return true;
                }
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        private void LstPages_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSelectingWithMarquee && e.LeftButton == MouseButtonState.Pressed)
            {
                Point currentPoint = e.GetPosition(LstPages);

                double x = Math.Min(_marqueeStartPoint.X, currentPoint.X);
                double y = Math.Min(_marqueeStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(_marqueeStartPoint.X - currentPoint.X);
                double height = Math.Abs(_marqueeStartPoint.Y - currentPoint.Y);

                Canvas.SetLeft(SelectionRectangle, x);
                Canvas.SetTop(SelectionRectangle, y);
                SelectionRectangle.Width = width;
                SelectionRectangle.Height = height;

                Rect marqueeRect = new Rect(x, y, width, height);

                foreach (var pageItem in Pages)
                {
                    var container = LstPages.ItemContainerGenerator.ContainerFromItem(pageItem) as ListBoxItem;
                    if (container != null)
                    {
                        Point itemPos = container.TranslatePoint(new Point(0, 0), LstPages);
                        Rect itemRect = new Rect(itemPos.X, itemPos.Y, container.ActualWidth, container.ActualHeight);

                        if (marqueeRect.IntersectsWith(itemRect))
                        {
                            pageItem.IsSelected = true;
                        }
                        else if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                        {
                            pageItem.IsSelected = false;
                        }
                    }
                }
            }
        }

        private void LstPages_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSelectingWithMarquee)
            {
                _isSelectingWithMarquee = false;
                SelectionRectangle.Visibility = Visibility.Collapsed;
            }
        }

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void ListBoxItem_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingPages && !_isSelectingWithMarquee)
            {
                Point currentPoint = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPoint;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is ListBoxItem item && item.DataContext is PdfPageItem clickedPage)
                    {
                        var draggedItems = Pages.Where(p => p.IsSelected).ToList();
                        if (draggedItems.Count == 0 || !draggedItems.Contains(clickedPage))
                        {
                            draggedItems = new List<PdfPageItem> { clickedPage };
                        }

                        if (draggedItems.Count > 0)
                        {
                            _isDraggingPages = true;
                            _currentDraggedGroup = draggedItems;

                            foreach (var p in draggedItems)
                            {
                                p.IsBeingDragged = true;
                            }

                            PdfPageItem primaryItem = draggedItems[0];
                            GhostImage.Source = primaryItem.Thumbnail;
                            GhostRotateTransform.Angle = primaryItem.Rotation;
                            TxtGhostBadge.Text = draggedItems.Count > 1 ? $"{draggedItems.Count} Pages" : $"Page {primaryItem.DisplayPageNumber}";

                            GhostCard.Width = primaryItem.CardWidth;
                            GhostCard.Height = primaryItem.CardHeight;
                            GhostCard.Visibility = Visibility.Visible;

                            DataObject dragData = new DataObject("PdfPageItems", draggedItems);
                            DragDrop.DoDragDrop(item, dragData, DragDropEffects.Move);

                            ResetDraggedItemsState();
                            _isDraggingPages = false;
                            GhostCard.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
        }

        private void LstPages_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PdfPageItems"))
            {
                e.Effects = DragDropEffects.Move;

                ScrollViewer? scrollViewer = GetScrollViewer(LstPages);
                if (scrollViewer != null)
                {
                    Point mousePosInScroll = e.GetPosition(scrollViewer);
                    double margin = 50.0;
                    double scrollSpeed = 16.0;

                    if (mousePosInScroll.Y >= 0 && mousePosInScroll.Y < margin)
                    {
                        scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset - scrollSpeed));
                    }
                    else if (mousePosInScroll.Y <= scrollViewer.ActualHeight && mousePosInScroll.Y > scrollViewer.ActualHeight - margin)
                    {
                        scrollViewer.ScrollToVerticalOffset(Math.Min(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset + scrollSpeed));
                    }
                }

                Point ghostPos = e.GetPosition(GridGalleryContainer);
                Canvas.SetLeft(GhostCard, Math.Max(0, ghostPos.X - (GhostCard.Width / 2.0)));
                Canvas.SetTop(GhostCard, Math.Max(0, ghostPos.Y - (GhostCard.Height / 2.0)));
                GhostCard.Visibility = Visibility.Visible;

                Point mousePos = e.GetPosition(LstPages);
                var hitResult = GetListBoxItemAtPosition(mousePos);
                var draggedItems = e.Data.GetData("PdfPageItems") as List<PdfPageItem>;

                if (draggedItems != null && draggedItems.Count > 0 && hitResult != null)
                {
                    PdfPageItem targetPage = hitResult.Item2;
                    bool insertBefore = e.GetPosition(hitResult.Item1).X < (hitResult.Item1.ActualWidth / 2.0);

                    var orderedDragged = Pages.Where(p => draggedItems.Contains(p)).ToList();
                    int targetIdxInPages = Pages.IndexOf(targetPage);

                    if (targetIdxInPages >= 0 && !orderedDragged.Contains(targetPage))
                    {
                        foreach (var page in orderedDragged)
                        {
                            Pages.Remove(page);
                        }

                        int newInsert = Math.Min(Pages.IndexOf(targetPage) + (insertBefore ? 0 : 1), Pages.Count);
                        for (int i = 0; i < orderedDragged.Count; i++)
                        {
                            Pages.Insert(Math.Min(newInsert + i, Pages.Count), orderedDragged[i]);
                        }
                        ReindexSequenceNumbers();
                    }
                }

                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                GhostCard.Visibility = Visibility.Collapsed;
            }
        }

        private void LstPages_DragLeave(object sender, DragEventArgs e)
        {
            GhostCard.Visibility = Visibility.Collapsed;
        }

        private void LstPages_Drop(object sender, DragEventArgs e)
        {
            GhostCard.Visibility = Visibility.Collapsed;
            ResetDraggedItemsState();

            if (e.Data.GetDataPresent("PdfPageItems"))
            {
                var draggedItems = e.Data.GetData("PdfPageItems") as List<PdfPageItem>;
                if (draggedItems == null || draggedItems.Count == 0) return;

                ReindexSequenceNumbers();
                UpdateUIState();

                TxtStatus.Text = $"Moved {draggedItems.Count} page{(draggedItems.Count == 1 ? "" : "s")}";
                e.Handled = true;
            }
        }

        private void ResetDraggedItemsState()
        {
            foreach (var page in Pages)
            {
                page.IsBeingDragged = false;
            }
            _currentDraggedGroup = null;
        }

        private Tuple<ListBoxItem, PdfPageItem>? GetListBoxItemAtPosition(Point position)
        {
            HitTestResult result = VisualTreeHelper.HitTest(LstPages, position);
            if (result != null)
            {
                DependencyObject obj = result.VisualHit;
                while (obj != null && obj != LstPages)
                {
                    if (obj is ListBoxItem item && item.DataContext is PdfPageItem pageItem)
                    {
                        return Tuple.Create(item, pageItem);
                    }
                    obj = VisualTreeHelper.GetParent(obj);
                }
            }
            return null;
        }

        private ScrollViewer? GetScrollViewer(DependencyObject depObj)
        {
            if (depObj is ScrollViewer viewer) return viewer;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                var result = GetScrollViewer(child);
                if (result != null) return result;
            }
            return null;
        }

        #endregion

        #region Multi-Selection & Page Actions

        private void LstPages_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUIState();
        }

        private void BtnSelectAllPages_Click(object sender, RoutedEventArgs e)
        {
            foreach (var page in Pages)
            {
                page.IsSelected = true;
            }
            UpdateUIState();
        }

        private void BtnDeselectAllPages_Click(object sender, RoutedEventArgs e)
        {
            foreach (var page in Pages)
            {
                page.IsSelected = false;
            }
            UpdateUIState();
        }

        private void BtnRotateSelectedCW_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = GetSelectedOrAllPages();
            foreach (var page in selectedPages)
            {
                page.RotateClockwise();
            }
            TxtStatus.Text = $"Rotated {selectedPages.Count} page(s) +90° (Lossless)";
        }

        private void BtnRotateSelectedCCW_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = GetSelectedOrAllPages();
            foreach (var page in selectedPages)
            {
                page.RotateCounterClockwise();
            }
            TxtStatus.Text = $"Rotated {selectedPages.Count} page(s) -90° (Lossless)";
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = Pages.Where(p => p.IsSelected).ToList();
            if (selectedPages.Count == 0) return;

            int count = selectedPages.Count;
            foreach (var page in selectedPages)
            {
                Pages.Remove(page);
            }

            ReindexSequenceNumbers();
            UpdateUIState();
            TxtStatus.Text = $"Deleted {count} page{(count == 1 ? "" : "s")}";
        }

        private void BtnRotateCW_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfPageItem pageItem)
            {
                pageItem.RotateClockwise();
                TxtStatus.Text = $"Rotated Page {pageItem.DisplayPageNumber} +90° (Lossless)";
            }
        }

        private void BtnRotateCCW_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfPageItem pageItem)
            {
                pageItem.RotateCounterClockwise();
                TxtStatus.Text = $"Rotated Page {pageItem.DisplayPageNumber} -90° (Lossless)";
            }
        }

        private void BtnDeletePage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfPageItem pageItem)
            {
                Pages.Remove(pageItem);
                ReindexSequenceNumbers();
                UpdateUIState();
                TxtStatus.Text = $"Deleted Page {pageItem.DisplayPageNumber}";
            }
        }

        private void BtnSignPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfPageItem pageItem)
            {
                var sigWindow = new pdfMerge.Views.SignatureWindow(pageItem)
                {
                    Owner = this
                };

                if (sigWindow.ShowDialog() == true && sigWindow.ResultSignature != null)
                {
                    pageItem.PageSignature = sigWindow.ResultSignature;

                    if (pageItem.Thumbnail != null)
                    {
                        pageItem.Thumbnail = RenderSignatureOverlayOnThumbnail(pageItem.Thumbnail, sigWindow.ResultSignature);
                    }

                    TxtStatus.Text = $"Applied signature to Page {pageItem.DisplayPageNumber}";
                }
            }
        }

        private BitmapSource RenderSignatureOverlayOnThumbnail(BitmapSource baseThumb, AppliedSignature sig)
        {
            int width = baseThumb.PixelWidth;
            int height = baseThumb.PixelHeight;

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(baseThumb, new Rect(0, 0, width, height));

                double sigX = width * sig.RelX;
                double sigY = height * sig.RelY;
                double sigW = width * sig.RelWidth;
                double sigH = height * sig.RelHeight;

                dc.DrawImage(sig.SignatureImage, new Rect(sigX, sigY, sigW, sigH));
            }

            var rtb = new RenderTargetBitmap(width, height, baseThumb.DpiX, baseThumb.DpiY, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }

        private List<PdfPageItem> GetSelectedOrAllPages()
        {
            var selected = Pages.Where(p => p.IsSelected).ToList();
            return selected.Count > 0 ? selected : Pages.ToList();
        }

        #endregion

        #region Save Selected, Export Images, Print & Merge Export

        private async void BtnExportSelectedImage_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = Pages.Where(p => p.IsSelected).ToList();
            if (selectedPages.Count == 0)
            {
                MessageBox.Show(this, "Please select at least one page to export as an image.", "No Pages Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
                Title = "Export Selected Pages As Images",
                FileName = selectedPages.Count == 1 ? $"Page_{selectedPages[0].DisplayPageNumber}.png" : "ExportedPage.png"
            };

            if (dialog.ShowDialog(this) == true)
            {
                bool isJpeg = Path.GetExtension(dialog.FileName).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                              Path.GetExtension(dialog.FileName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

                SetLoadingState(true, $"Exporting {selectedPages.Count} selected page(s) to image...");

                try
                {
                    string folder = Path.GetDirectoryName(dialog.FileName) ?? Environment.CurrentDirectory;
                    string baseFileName = Path.GetFileNameWithoutExtension(dialog.FileName);
                    string ext = isJpeg ? ".jpg" : ".png";

                    int count = 0;
                    foreach (var pageItem in selectedPages)
                    {
                        BitmapSource? pageImage = await _pdfService.RenderPageThumbnailAsync(pageItem.SourceFilePath, pageItem.OriginalPageIndex, 2048);
                        if (pageImage == null && pageItem.Thumbnail != null)
                        {
                            pageImage = pageItem.Thumbnail;
                        }

                        if (pageImage != null)
                        {
                            if (pageItem.Rotation != 0)
                            {
                                pageImage = RotateBitmap(pageImage, pageItem.Rotation);
                            }

                            if (pageItem.PageSignature != null)
                            {
                                pageImage = RenderSignatureOverlayOnThumbnail(pageImage, pageItem.PageSignature);
                            }

                            string targetPath = selectedPages.Count == 1
                                ? dialog.FileName
                                : Path.Combine(folder, $"{baseFileName}_Page_{pageItem.DisplayPageNumber:D3}{ext}");

                            using (var stream = new FileStream(targetPath, FileMode.Create))
                            {
                                BitmapEncoder encoder = isJpeg ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
                                encoder.Frames.Add(BitmapFrame.Create(pageImage));
                                encoder.Save(stream);
                            }

                            count++;
                        }
                    }

                    SetLoadingState(false, $"Exported {count} page image(s) successfully!");
                    var result = MessageBox.Show(this, $"Successfully exported {count} page image(s) to:\n{folder}\n\nWould you like to open the output folder?", "Export Complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folder);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to export page images: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetLoadingState(false, "Image export failed");
                }
            }
        }

        private BitmapSource RotateBitmap(BitmapSource source, int angle)
        {
            var transformed = new TransformedBitmap(source, new RotateTransform(angle));
            transformed.Freeze();
            return transformed;
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one page before printing.", "No Pages", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var previewWindow = new pdfMerge.Views.PrintPreviewWindow(Pages.ToList(), _pdfService)
            {
                Owner = this
            };

            if (previewWindow.ShowDialog() == true)
            {
                TxtStatus.Text = "Print job completed successfully";
            }
        }

        private BitmapSource ConvertToGrayscale(BitmapSource source)
        {
            var grayBitmap = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            grayBitmap.Freeze();
            return grayBitmap;
        }

        private async void BtnSaveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = Pages.Where(p => p.IsSelected).ToList();
            if (selectedPages.Count == 0)
            {
                MessageBox.Show(this, "Please select at least one page to save.", "No Pages Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PDF File (*.pdf)|*.pdf",
                Title = "Save Selected PDF Pages As",
                FileName = "SelectedPages.pdf"
            };

            if (dialog.ShowDialog(this) == true)
            {
                SetLoadingState(true, "Merging and saving selected PDF pages...");

                try
                {
                    await _pdfService.MergeAndSavePdfAsync(selectedPages, dialog.FileName);
                    SetLoadingState(false, "Selected PDF pages saved successfully!");

                    var result = MessageBox.Show(this, $"Successfully created PDF with {selectedPages.Count} selected page(s):\n{dialog.FileName}\n\nWould you like to open the output folder?", "Save Selected Complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        string? folder = Path.GetDirectoryName(dialog.FileName);
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", folder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save selected PDF pages: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetLoadingState(false, "Save selected failed");
                }
            }
        }

        private async void BtnMergeSave_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one PDF page before merging.", "No Pages", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PDF File (*.pdf)|*.pdf",
                Title = "Save Merged PDF As",
                FileName = "MergedDocument.pdf"
            };

            if (dialog.ShowDialog(this) == true)
            {
                SetLoadingState(true, "Merging document pages with lossless metadata rotation...");

                try
                {
                    await _pdfService.MergeAndSavePdfAsync(Pages.ToList(), dialog.FileName);
                    SetLoadingState(false, "Merged PDF saved successfully!");

                    var result = MessageBox.Show(this, $"Successfully created merged PDF:\n{dialog.FileName}\n\nWould you like to open the output folder?", "Merge Complete", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result == MessageBoxResult.Yes)
                    {
                        string? folder = Path.GetDirectoryName(dialog.FileName);
                        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                        {
                            System.Diagnostics.Process.Start("explorer.exe", folder);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to merge PDF: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetLoadingState(false, "Merge failed");
                }
            }
        }

        #endregion

        #region Helpers

        private void ReindexFilesOrder()
        {
            for (int i = 0; i < Files.Count; i++)
            {
                Files[i].Order = i + 1;
            }
        }

        private void ReindexSequenceNumbers()
        {
            for (int i = 0; i < Pages.Count; i++)
            {
                Pages[i].DisplayPageNumber = i + 1;
            }
        }

        private void RebuildPagesFromFilesOrder()
        {
            var fileOrderDict = Files.Select((f, index) => new { f.FilePath, index })
                                     .ToDictionary(x => x.FilePath, x => x.index, StringComparer.OrdinalIgnoreCase);

            var sortedPages = Pages.OrderBy(p => fileOrderDict.TryGetValue(p.SourceFilePath, out int order) ? order : int.MaxValue)
                                   .ThenBy(p => p.OriginalPageIndex)
                                   .ToList();

            Pages.Clear();
            foreach (var page in sortedPages)
            {
                Pages.Add(page);
            }

            ReindexSequenceNumbers();
        }

        private void UpdateUIState()
        {
            TxtFileCount.Text = $"{Files.Count} file{(Files.Count == 1 ? "" : "s")}";
            TxtPageCountBadge.Text = $"{Pages.Count} Page{(Pages.Count == 1 ? "" : "s")}";

            int selectedCount = Pages.Count(p => p.IsSelected);
            TxtSelectedCountBadge.Text = selectedCount > 0 ? $"({selectedCount} selected)" : "(0 selected)";

            bool hasPages = Pages.Count > 0;
            PnlEmptyState.Visibility = hasPages ? Visibility.Collapsed : Visibility.Visible;
            GridGalleryContainer.Visibility = hasPages ? Visibility.Visible : Visibility.Collapsed;
            BtnMergeSave.IsEnabled = hasPages;

            bool hasSelection = selectedCount > 0;
            BtnRotateSelectedCW.IsEnabled = hasPages;
            BtnRotateSelectedCCW.IsEnabled = hasPages;
            BtnDeleteSelected.IsEnabled = hasSelection;
            BtnSaveSelected.IsEnabled = hasSelection;
            BtnExportSelectedImage.IsEnabled = hasSelection;
            BtnPrint.IsEnabled = hasPages;
            BtnRevert.IsEnabled = hasPages || _originalPagesSnapshot.Count > 0;
        }

        private void SetLoadingState(bool isLoading, string statusText)
        {
            TxtStatus.Text = statusText;
            ProgressStatus.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            ProgressStatus.IsIndeterminate = isLoading;
        }

        #endregion
    }

    /// <summary>
    /// DocumentPaginator for printing PDF document pages with native PrintDialog options.
    /// </summary>
    public class PdfDocumentPaginator : DocumentPaginator
    {
        private readonly List<BitmapSource> _pageBitmaps;
        private Size _pageSize;

        public PdfDocumentPaginator(List<BitmapSource> pageBitmaps, Size pageSize)
        {
            _pageBitmaps = pageBitmaps;
            _pageSize = pageSize;
        }

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0 || pageNumber >= _pageBitmaps.Count)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));

            var bitmap = _pageBitmaps[pageNumber];

            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double scale = Math.Min(_pageSize.Width / bitmap.PixelWidth, _pageSize.Height / bitmap.PixelHeight);
                double renderWidth = bitmap.PixelWidth * scale;
                double renderHeight = bitmap.PixelHeight * scale;

                double offsetX = (_pageSize.Width - renderWidth) / 2.0;
                double offsetY = (_pageSize.Height - renderHeight) / 2.0;

                dc.DrawImage(bitmap, new Rect(offsetX, offsetY, renderWidth, renderHeight));
            }

            return new DocumentPage(visual, _pageSize, new Rect(0, 0, _pageSize.Width, _pageSize.Height), new Rect(0, 0, _pageSize.Width, _pageSize.Height));
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => _pageBitmaps.Count;
        public override Size PageSize { get => _pageSize; set => _pageSize = value; }
        public override IDocumentPaginatorSource? Source => null;
    }
}