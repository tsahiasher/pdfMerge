using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using pdfMerge.Helpers;
using pdfMerge.Models;
using pdfMerge.Services;

namespace pdfMerge
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _loadCts;

        private Point _dragStartPoint;
        private bool _isDraggingPages;

        private Point _marqueeStartPoint;
        private bool _isSelectingWithMarquee;

        // Snapshot of original pages state for Revert All feature (Requirement 1)
        private List<PageSnapshotState> _originalPagesSnapshot = new List<PageSnapshotState>();
        private List<PdfPageItem>? _currentDraggedGroup;
        private readonly SemaphoreSlim _thumbnailRenderSemaphore = new SemaphoreSlim(3);
        private bool _hasSourceBookmarks = false;

        private DateTime _lastDialogClosedTimestamp = DateTime.MinValue;

        private bool IsDialogCooldownActive()
        {
            return (DateTime.UtcNow - _lastDialogClosedTimestamp).TotalMilliseconds < 500;
        }

        private async Task DrainPendingInputAsync()
        {
            _lastDialogClosedTimestamp = DateTime.UtcNow;
            Mouse.Capture(null);
            await Dispatcher.Yield(DispatcherPriority.Input);
        }

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
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;

            Pages.CollectionChanged -= Pages_CollectionChanged;
            foreach (var item in Pages)
            {
                item.PropertyChanged -= PageItem_PropertyChanged;
            }
            Pages.Clear();
            Files.Clear();

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
                Mouse.OverrideCursor = pdfMerge.Helpers.CursorUtility.ClosedHand;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            if (_isDraggingPages)
            {
                e.UseDefaultCursors = false;
                Mouse.OverrideCursor = pdfMerge.Helpers.CursorUtility.ClosedHand;
                e.Handled = true;
            }
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

            bool? result = dialog.ShowDialog(this);
            await DrainPendingInputAsync();

            if (result == true)
            {
                await AddFilesAsync(dialog.FileNames);
            }
        }

        private async Task AddFilesAsync(IEnumerable<string> filePaths)
        {
            var files = filePaths
                .Select(Path.GetFullPath)
                .Where(PdfService.IsSupportedFile)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0) return;

            // Cancel and dispose previous loading CTS
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            SetLoadingState(true, "Reading file information...");

            try
            {
                var newlyAddedPages = new List<PdfPageItem>();

                foreach (var filePath in files)
                {
                    token.ThrowIfCancellationRequested();

                    if (Files.Any(f => f.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    int pageCount = await PdfService.GetPageCountAsync(filePath, token);

                    var fileItem = new PdfFileItem
                    {
                        FilePath = filePath,
                        PageCount = pageCount,
                        Order = Files.Count + 1
                    };

                    Files.Add(fileItem);

                    for (int pageIdx = 0; pageIdx < pageCount; pageIdx++)
                    {
                        var pageItem = new PdfPageItem
                        {
                            SourceFilePath = filePath,
                            OriginalPageIndex = pageIdx,
                            DisplayPageNumber = Pages.Count + 1,
                            IsLoading = true,
                            OriginalThumbnail = null
                        };

                        ApplyZoomDimensionsToItem(pageItem, (int)SldZoom.Value);
                        Pages.Add(pageItem);
                        _originalPagesSnapshot.Add(new PageSnapshotState
                        {
                            SourceFilePath = filePath,
                            OriginalPageIndex = pageIdx,
                            OriginalDisplayPageNumber = pageItem.DisplayPageNumber,
                            InitialRotation = 0,
                            InitialEditorData = new PageEditorData()
                        });
                        newlyAddedPages.Add(pageItem);
                    }
                }

                PageReorderService.ReindexSequenceNumbers(Pages);
                UpdateDocumentColors();
                UpdateBookmarkAvailability();
                PageSelectionService.DeselectAll(Pages);
                UpdateUIState();
                SetLoadingState(false, $"Added {newlyAddedPages.Count} page(s). Rendering thumbnails...");

                // Asynchronously render thumbnails in background with bounded concurrency (Priority 5)
                _ = LoadThumbnailsInBackgroundAsync(newlyAddedPages, token);
            }
            catch (OperationCanceledException)
            {
                SetLoadingState(false, "Loading cancelled");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetLoadingState(false, "Failed to load files");
            }
        }

        private void UpdateBookmarkAvailability()
        {
            _hasSourceBookmarks = PdfService.HasBookmarks(Pages);
        }

        private static readonly string[] DocumentColorPalette = new[]
        {
            "#0EA5E9", // Vibrant Sky Blue / Cyan
            "#10B981", // Emerald Green
            "#F59E0B", // Amber Gold
            "#A855F7", // Purple / Violet
            "#EC4899", // Pink / Rose
            "#14B8A6", // Teal
            "#F97316", // Bright Orange
            "#6366F1", // Indigo
            "#84CC16", // Lime
            "#E11D48"  // Crimson / Rose Red
        };

        private void UpdateDocumentColors()
        {
            PageReorderService.ReindexFilesOrder(Files);

            for (int i = 0; i < Files.Count; i++)
            {
                var fileItem = Files[i];
                string color = DocumentColorPalette[i % DocumentColorPalette.Length];
                fileItem.DocumentColorHex = color;

                foreach (var pageItem in Pages.Where(p => p.SourceFilePath.Equals(fileItem.FilePath, StringComparison.OrdinalIgnoreCase)))
                {
                    pageItem.DocumentColorHex = color;
                }
            }
        }

        private async Task LoadThumbnailsInBackgroundAsync(List<PdfPageItem> pageItems, CancellationToken token)
        {
            var tasks = new List<Task>();

            foreach (var item in pageItems)
            {
                if (token.IsCancellationRequested) break;

                tasks.Add(Task.Run(async () =>
                {
                    await _thumbnailRenderSemaphore.WaitAsync(token);
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        BitmapSource? thumb = await PdfService.RenderPageThumbnailAsync(item.SourceFilePath, item.OriginalPageIndex, 350, token);
                        if (thumb != null && !token.IsCancellationRequested)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                item.OriginalThumbnail = thumb;
                                item.IsLoading = false;
                            });
                        }
                        else
                        {
                            Dispatcher.Invoke(() => item.IsLoading = false);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error background rendering thumbnail for {item.SourceFileName}: {ex.Message}");
                        Dispatcher.Invoke(() => item.IsLoading = false);
                    }
                    finally
                    {
                        try { _thumbnailRenderSemaphore.Release(); } catch { }
                    }
                }, token));
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch { }
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
                PageReorderService.ReindexFilesOrder(Files);
                PageReorderService.ReindexSequenceNumbers(Pages);
                UpdateDocumentColors();
                UpdateBookmarkAvailability();
                UpdateUIState();

                // GC.Collect/WaitForPendingFinalizers removed (Rec #10) — the runtime handles collection automatically

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
                    PageReorderService.ReindexFilesOrder(Files);
                    PageReorderService.RebuildPagesFromFilesOrder(Pages, Files);
                    UpdateDocumentColors();
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
                    PageReorderService.ReindexFilesOrder(Files);
                    PageReorderService.RebuildPagesFromFilesOrder(Pages, Files);
                    UpdateDocumentColors();
                }
            }
        }

        private bool ConfirmReorderFilesIfPagesCustomized()
        {
            if (PageReorderService.HasCustomPageOrder(Pages, Files))
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
                _loadCts?.Cancel();
                Files.Clear();
                Pages.Clear();
                _originalPagesSnapshot.Clear();
                _hasSourceBookmarks = false;
                UpdateUIState();

                TxtStatus.Text = "Cleared all files";
            }
        }

        private void BtnRevert_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0 && _originalPagesSnapshot.Count == 0) return;

            if (MessageBox.Show(this, "Revert all changes and reload original files?", "Revert All Changes", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Gather existing valid thumbnails from current pages in memory
                var existingThumbnails = new Dictionary<(string FilePath, int PageIndex), BitmapSource>();
                foreach (var page in Pages)
                {
                    if (page.OriginalThumbnail != null)
                    {
                        var key = (page.SourceFilePath.ToLowerInvariant(), page.OriginalPageIndex);
                        if (!existingThumbnails.ContainsKey(key))
                        {
                            existingThumbnails[key] = page.OriginalThumbnail;
                        }
                    }
                }

                Pages.Clear();
                var pagesNeedingThumbnails = new List<PdfPageItem>();

                foreach (var snap in _originalPagesSnapshot)
                {
                    var key = (snap.SourceFilePath.ToLowerInvariant(), snap.OriginalPageIndex);
                    existingThumbnails.TryGetValue(key, out var existingThumb);

                    var restored = new PdfPageItem
                    {
                        SourceFilePath = snap.SourceFilePath,
                        OriginalPageIndex = snap.OriginalPageIndex,
                        DisplayPageNumber = snap.OriginalDisplayPageNumber,
                        Rotation = snap.InitialRotation,
                        IsSelected = false,
                        IsBeingDragged = false,
                        OriginalThumbnail = existingThumb,
                        IsLoading = existingThumb == null,
                        EditorData = snap.InitialEditorData?.Clone() ?? new PageEditorData()
                    };

                    ApplyZoomDimensionsToItem(restored, (int)SldZoom.Value);
                    Pages.Add(restored);

                    if (existingThumb == null)
                    {
                        pagesNeedingThumbnails.Add(restored);
                    }
                }

                PageReorderService.ReindexSequenceNumbers(Pages);
                UpdateDocumentColors();
                UpdateBookmarkAvailability();
                UpdateUIState();

                // If any restored page needs a thumbnail, asynchronously load in background
                if (pagesNeedingThumbnails.Count > 0)
                {
                    _loadCts?.Cancel();
                    _loadCts?.Dispose();
                    _loadCts = new CancellationTokenSource();
                    _ = LoadThumbnailsInBackgroundAsync(pagesNeedingThumbnails, _loadCts.Token);
                }

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
            if (IsDialogCooldownActive())
            {
                e.Handled = true;
                return;
            }

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

        private void HeaderBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsDialogCooldownActive())
            {
                e.Handled = true;
                return;
            }

            if (sender is FrameworkElement elem && elem.DataContext is PdfPageItem pageItem)
            {
                // Check if user clicked directly on the CheckBox to avoid double-toggling
                bool isCheckBoxClick = e.OriginalSource is CheckBox ||
                    (e.OriginalSource is DependencyObject depObj && FindVisualParent<CheckBox>(depObj) != null);

                if (!isCheckBoxClick)
                {
                    pageItem.IsSelected = !pageItem.IsSelected;
                    e.Handled = true;
                }
            }
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T typedParent) return typedParent;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsDialogCooldownActive())
            {
                e.Handled = true;
                return;
            }

            _dragStartPoint = e.GetPosition(null);
        }

        private void ListBoxItem_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Mouse.OverrideCursor = pdfMerge.Helpers.CursorUtility.ClosedHand;
            e.Handled = true;
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
                        // Select grabbed page (adding to any existing selection)
                        clickedPage.IsSelected = true;

                        var draggedItems = Pages.Where(p => p.IsSelected).ToList();

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
                            Mouse.OverrideCursor = pdfMerge.Helpers.CursorUtility.ClosedHand;
                            try
                            {
                                DragDrop.DoDragDrop(item, dragData, DragDropEffects.Move);
                            }
                            finally
                            {
                                Mouse.OverrideCursor = null;
                            }

                            if (draggedItems.Count == 1)
                            {
                                draggedItems[0].IsSelected = false;
                            }

                            ResetDraggedItemsState();
                            _isDraggingPages = false;
                            GhostCard.Visibility = Visibility.Collapsed;
                            UpdateUIState();
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
                Mouse.OverrideCursor = pdfMerge.Helpers.CursorUtility.ClosedHand;

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
                    PdfPageItem targetPage = hitResult.Value.Page;
                    bool insertBefore = e.GetPosition(hitResult.Value.Container).X < (hitResult.Value.Container.ActualWidth / 2.0);

                    var orderedDragged = Pages.Where(p => draggedItems.Contains(p)).ToList();
                    int targetIdxInPages = Pages.IndexOf(targetPage);

                    if (targetIdxInPages >= 0 && !orderedDragged.Contains(targetPage))
                    {
                        int currentMinIdx = orderedDragged.Min(p => Pages.IndexOf(p));
                        int currentMaxIdx = orderedDragged.Max(p => Pages.IndexOf(p));
                        int newInsert = targetIdxInPages + (insertBefore ? 0 : 1);

                        // Only reorder collection if target position has actually moved across boundaries
                        if (newInsert < currentMinIdx || newInsert > currentMaxIdx + 1)
                        {
                            foreach (var page in orderedDragged)
                            {
                                Pages.Remove(page);
                            }

                            int targetPos = Math.Min(Pages.IndexOf(targetPage) + (insertBefore ? 0 : 1), Pages.Count);
                            for (int i = 0; i < orderedDragged.Count; i++)
                            {
                                Pages.Insert(Math.Min(targetPos + i, Pages.Count), orderedDragged[i]);
                            }
                            PageReorderService.ReindexSequenceNumbers(Pages);
                        }
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

                if (draggedItems.Count == 1)
                {
                    draggedItems[0].IsSelected = false;
                }

                PageReorderService.ReindexSequenceNumbers(Pages);
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

        private (ListBoxItem Container, PdfPageItem Page)? GetListBoxItemAtPosition(Point position)
        {
            HitTestResult result = VisualTreeHelper.HitTest(LstPages, position);
            if (result != null)
            {
                DependencyObject obj = result.VisualHit;
                while (obj != null && obj != LstPages)
                {
                    if (obj is ListBoxItem item && item.DataContext is PdfPageItem pageItem)
                    {
                        return (item, pageItem);
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
            PageSelectionService.SelectAll(Pages);
            UpdateUIState();
        }

        private void BtnDeselectAllPages_Click(object sender, RoutedEventArgs e)
        {
            PageSelectionService.DeselectAll(Pages);
            UpdateUIState();
        }

        private void BtnRotateSelectedCW_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = PageSelectionService.GetSelectedOrAllPages(Pages);
            foreach (var page in selectedPages)
            {
                page.RotateClockwise();
            }
            TxtStatus.Text = $"Rotated {selectedPages.Count} page(s) +90° (Lossless)";
        }

        private void BtnRotateSelectedCCW_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = PageSelectionService.GetSelectedOrAllPages(Pages);
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

            PageReorderService.ReindexSequenceNumbers(Pages);
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
                PageReorderService.ReindexSequenceNumbers(Pages);
                UpdateUIState();
                TxtStatus.Text = $"Deleted Page {pageItem.DisplayPageNumber}";
            }
        }

        private async void BtnSignPage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfPageItem pageItem)
            {
                var editorWindow = new pdfMerge.Views.PageEditorWindow(pageItem)
                {
                    Owner = this
                };

                bool? editorResult = editorWindow.ShowDialog();
                await DrainPendingInputAsync();

                if (editorResult == true)
                {
                    TxtStatus.Text = $"Updated edits for Page {pageItem.DisplayPageNumber}";
                }
            }
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

            bool? dialogResult = dialog.ShowDialog(this);
            await DrainPendingInputAsync();

            if (dialogResult == true)
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
                        BitmapSource? pageImage = await PageRenderService.RenderCompositePageAsync(pageItem, 2048);

                        if (pageImage != null)
                        {

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
                    MessageBox.Show(this, $"Successfully exported {count} page image(s) to:\n{folder}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to export page images: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetLoadingState(false, "Image export failed");
                }
            }
        }

        // RotateBitmap moved to Helpers/BitmapUtilities.cs (Rec #1)

        private async void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one page before printing.", "No Pages", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var previewWindow = new pdfMerge.Views.PrintPreviewWindow(Pages.ToList())
            {
                Owner = this
            };

            bool? previewResult = previewWindow.ShowDialog();
            await DrainPendingInputAsync();

            if (previewResult == true)
            {
                TxtStatus.Text = "Print job completed successfully";
            }
        }

        // ConvertToGrayscale moved to Helpers/BitmapUtilities.cs (Rec #1)

        private async void BtnSaveSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = Pages.Where(p => p.IsSelected).ToList();
            if (selectedPages.Count == 0)
            {
                MessageBox.Show(this, "Please select at least one page to save.", "No Pages Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool recreateBookmarks = ChkPreserveBookmarks.IsChecked == true;

            var dialog = new SaveFileDialog
            {
                Filter = "PDF File (*.pdf)|*.pdf",
                Title = "Save Selected PDF Pages As",
                FileName = "SelectedPages.pdf"
            };

            bool? saveSelResult = dialog.ShowDialog(this);
            await DrainPendingInputAsync();

            if (saveSelResult == true)
            {
                SetLoadingState(true, "Merging and saving selected PDF pages...");

                try
                {
                    await PdfService.MergeAndSavePdfAsync(selectedPages, dialog.FileName, recreateBookmarks);
                    SetLoadingState(false, "Selected PDF pages saved successfully!");
                    MessageBox.Show(this, $"Successfully created PDF with {selectedPages.Count} selected page(s):\n{dialog.FileName}", "Save Selected Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Failed to save selected PDF pages: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetLoadingState(false, "Save selected failed");
                }
            }
        }

        private async void BtnSplit_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one PDF or image file before splitting.", "No Pages Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var splitWindow = new pdfMerge.Views.SplitWindow(Pages.ToList())
            {
                Owner = this
            };

            splitWindow.ShowDialog();
            await DrainPendingInputAsync();
        }

        private async void BtnMergeSave_Click(object sender, RoutedEventArgs e)
        {
            if (Pages.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one PDF page before merging.", "No Pages", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var pagesToSave = Pages.ToList();
            bool recreateBookmarks = ChkPreserveBookmarks.IsChecked == true;

            var dialog = new SaveFileDialog
            {
                Filter = "PDF File (*.pdf)|*.pdf",
                Title = "Save Merged PDF As",
                FileName = "MergedDocument.pdf"
            };

            bool? mergeResult = dialog.ShowDialog(this);
            await DrainPendingInputAsync();

            if (mergeResult == true)
            {
                SetLoadingState(true, "Merging document pages with lossless metadata rotation...");

                try
                {
                    await PdfService.MergeAndSavePdfAsync(pagesToSave, dialog.FileName, recreateBookmarks);
                    SetLoadingState(false, "Merged PDF saved successfully!");
                    MessageBox.Show(this, $"Successfully created merged PDF:\n{dialog.FileName}", "Merge Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

        // ReindexSequenceNumbers, ReindexFilesOrder, RebuildPagesFromFilesOrder moved to Services/PageReorderService.cs (Priority 6)

        private void UpdateUIState()
        {
            TxtFileCount.Text = $"{Files.Count} file{(Files.Count == 1 ? "" : "s")}";
            TxtPageCountBadge.Text = $"{Pages.Count} Page{(Pages.Count == 1 ? "" : "s")}";

            bool hasPages = Pages.Count > 0;
            bool hasFiles = Files.Count > 0;
            int selectedCount = Pages.Count(p => p.IsSelected);
            bool hasSelection = selectedCount > 0;

            TxtSelectedCountBadge.Text = selectedCount > 0 ? $"({selectedCount} selected)" : "(0 selected)";

            PnlEmptyState.Visibility = hasPages ? Visibility.Collapsed : Visibility.Visible;
            GridGalleryContainer.Visibility = hasPages ? Visibility.Visible : Visibility.Collapsed;

            BtnClearAll.IsEnabled = hasFiles || hasPages;
            BtnSplit.IsEnabled = hasPages;
            PnlZoom.IsEnabled = hasPages;
            SldZoom.IsEnabled = hasPages;
            BtnSelectAll.IsEnabled = hasPages;
            BtnDeselectAll.IsEnabled = hasSelection;
            BtnMergeSave.IsEnabled = hasPages;
            ChkPreserveBookmarks.IsEnabled = _hasSourceBookmarks;
            ChkPreserveBookmarks.ToolTip = _hasSourceBookmarks
                ? "Recreate and include bookmarks in the saved PDF from source documents"
                : "No bookmarks found in loaded source files";

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

    // PdfDocumentPaginator moved to Models/PdfDocumentPaginator.cs (Rec #2)
}