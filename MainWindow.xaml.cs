using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        public ObservableCollection<PdfFileItem> Files { get; } = new ObservableCollection<PdfFileItem>();
        public ObservableCollection<PdfPageItem> Pages { get; } = new ObservableCollection<PdfPageItem>();

        public MainWindow()
        {
            InitializeComponent();

            LstFiles.ItemsSource = Files;
            LstPages.ItemsSource = Pages;

            UpdateUIState();
        }

        #region Drag & Drop Window PDF File Loading

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Any(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
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
                var pdfFiles = droppedFiles
                    .Where(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (pdfFiles.Any())
                {
                    await AddPdfFilesAsync(pdfFiles);
                }
            }
        }

        #endregion

        #region File Management

        private async void BtnOpenFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Multiselect = true,
                Title = "Select PDF Files to Merge"
            };

            if (dialog.ShowDialog(this) == true)
            {
                await AddPdfFilesAsync(dialog.FileNames);
            }
        }

        private async Task AddPdfFilesAsync(IEnumerable<string> filePaths)
        {
            SetLoadingState(true, "Processing PDF files...");

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
                    if (pageCount <= 0) continue;

                    var fileItem = new PdfFileItem
                    {
                        FilePath = filePath,
                        PageCount = pageCount,
                        Order = Files.Count + 1
                    };

                    Files.Add(fileItem);

                    for (int i = 0; i < pageCount; i++)
                    {
                        var pageItem = new PdfPageItem
                        {
                            SourceFilePath = filePath,
                            OriginalPageIndex = i,
                            Rotation = 0,
                            IsLoading = true
                        };
                        Pages.Add(pageItem);
                        newPagesList.Add(pageItem);
                    }
                }

                ReindexSequenceNumbers();
                UpdateUIState();

                SetLoadingState(true, $"Rendering page thumbnails (0/{newPagesList.Count})...");

                int completed = 0;
                foreach (var pageItem in newPagesList)
                {
                    var thumb = await _pdfService.RenderPageThumbnailAsync(pageItem.SourceFilePath, pageItem.OriginalPageIndex);
                    pageItem.Thumbnail = thumb;
                    pageItem.IsLoading = false;

                    completed++;
                    SetLoadingState(true, $"Rendering page thumbnails ({completed}/{newPagesList.Count})...");
                }

                SetLoadingState(false, "Ready");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error adding PDF file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetLoadingState(false, "Error loading files");
            }
        }

        private void BtnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfFileItem fileItem)
            {
                Files.Remove(fileItem);

                var pagesToRemove = Pages.Where(p => p.SourceFilePath.Equals(fileItem.FilePath, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var page in pagesToRemove)
                {
                    Pages.Remove(page);
                }

                ReindexFilesOrder();
                ReindexSequenceNumbers();
                UpdateUIState();
            }
        }

        private void BtnMoveFileUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is PdfFileItem fileItem)
            {
                int index = Files.IndexOf(fileItem);
                if (index > 0)
                {
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
                    Files.Move(index, index + 1);
                    ReindexFilesOrder();
                    RebuildPagesFromFilesOrder();
                }
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (Files.Count == 0 && Pages.Count == 0) return;

            if (MessageBox.Show(this, "Are you sure you want to clear all loaded PDF files and pages?", "Clear All", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Files.Clear();
                Pages.Clear();
                UpdateUIState();
                TxtStatus.Text = "Cleared all files";
            }
        }

        #endregion

        #region Marquee Selection (Rubber-Band Selection Box)

        private void LstPages_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point mousePos = e.GetPosition(LstPages);
            var hitResult = GetListBoxItemAtPosition(mousePos);

            if (hitResult == null)
            {
                // Clicked background space -> start marquee rubber-band selection
                _isSelectingWithMarquee = true;
                _marqueeStartPoint = mousePos;

                if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    LstPages.UnselectAll();
                }

                Canvas.SetLeft(SelectionRectangle, _marqueeStartPoint.X);
                Canvas.SetTop(SelectionRectangle, _marqueeStartPoint.Y);
                SelectionRectangle.Width = 0;
                SelectionRectangle.Height = 0;
                SelectionRectangle.Visibility = Visibility.Visible;

                GridGalleryContainer.CaptureMouse();
            }
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

                // Update item selection states based on marquee rectangle intersection
                foreach (var pageItem in Pages)
                {
                    var container = LstPages.ItemContainerGenerator.ContainerFromItem(pageItem) as ListBoxItem;
                    if (container != null)
                    {
                        Point itemPos = container.TranslatePoint(new Point(0, 0), LstPages);
                        Rect itemRect = new Rect(itemPos.X, itemPos.Y, container.ActualWidth, container.ActualHeight);

                        if (marqueeRect.IntersectsWith(itemRect))
                        {
                            if (!LstPages.SelectedItems.Contains(pageItem))
                            {
                                LstPages.SelectedItems.Add(pageItem);
                            }
                        }
                        else if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                        {
                            if (LstPages.SelectedItems.Contains(pageItem))
                            {
                                LstPages.SelectedItems.Remove(pageItem);
                            }
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
                GridGalleryContainer.ReleaseMouseCapture();
            }
        }

        #endregion

        #region Page Drag and Drop Reordering with Visual Marker & Auto-Scroll

        private void ListBoxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item && item.DataContext is PdfPageItem clickedPage)
            {
                _dragStartPoint = e.GetPosition(null);
                _isDraggingPages = false;

                if (LstPages.SelectedItems.Contains(clickedPage))
                {
                    e.Handled = false;
                }
            }
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
                        if (!LstPages.SelectedItems.Contains(clickedPage))
                        {
                            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                            {
                                LstPages.SelectedItems.Clear();
                            }
                            LstPages.SelectedItems.Add(clickedPage);
                        }

                        var draggedItems = Pages.Where(p => LstPages.SelectedItems.Contains(p)).ToList();

                        if (draggedItems.Count > 0)
                        {
                            _isDraggingPages = true;
                            DataObject dragData = new DataObject("PdfPageItems", draggedItems);
                            DragDrop.DoDragDrop(item, dragData, DragDropEffects.Move);
                            _isDraggingPages = false;
                            InsertionMarker.Visibility = Visibility.Collapsed;
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

                // Viewport Edge Auto-Scrolling
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

                // Position Visual Insertion Line Marker
                Point mousePos = e.GetPosition(LstPages);
                var hitResult = GetListBoxItemAtPosition(mousePos);

                if (hitResult != null)
                {
                    ListBoxItem item = hitResult.Item1;
                    Point itemRelPos = e.GetPosition(item);
                    bool isBefore = itemRelPos.X < (item.ActualWidth / 2.0);

                    Point canvasPos = item.TranslatePoint(new Point(isBefore ? -6 : item.ActualWidth + 2, 0), CnvInsertionMarker);
                    Canvas.SetLeft(InsertionMarker, Math.Max(0, canvasPos.X));
                    Canvas.SetTop(InsertionMarker, canvasPos.Y);
                    InsertionMarker.Height = item.ActualHeight > 0 ? item.ActualHeight : 305;
                    InsertionMarker.Visibility = Visibility.Visible;
                }
                else if (Pages.Count > 0)
                {
                    var lastContainer = LstPages.ItemContainerGenerator.ContainerFromIndex(Pages.Count - 1) as ListBoxItem;
                    if (lastContainer != null)
                    {
                        Point canvasPos = lastContainer.TranslatePoint(new Point(lastContainer.ActualWidth + 2, 0), CnvInsertionMarker);
                        Canvas.SetLeft(InsertionMarker, canvasPos.X);
                        Canvas.SetTop(InsertionMarker, canvasPos.Y);
                        InsertionMarker.Height = lastContainer.ActualHeight > 0 ? lastContainer.ActualHeight : 305;
                        InsertionMarker.Visibility = Visibility.Visible;
                    }
                }
                else
                {
                    InsertionMarker.Visibility = Visibility.Collapsed;
                }

                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                InsertionMarker.Visibility = Visibility.Collapsed;
            }
        }

        private void LstPages_DragLeave(object sender, DragEventArgs e)
        {
            InsertionMarker.Visibility = Visibility.Collapsed;
        }

        private void LstPages_Drop(object sender, DragEventArgs e)
        {
            InsertionMarker.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent("PdfPageItems"))
            {
                var draggedItems = e.Data.GetData("PdfPageItems") as List<PdfPageItem>;
                if (draggedItems == null || draggedItems.Count == 0) return;

                Point dropPosition = e.GetPosition(LstPages);
                var hitResult = GetListBoxItemAtPosition(dropPosition);

                PdfPageItem? targetPage = null;
                bool insertBefore = true;

                if (hitResult != null)
                {
                    targetPage = hitResult.Item2;
                    Point itemRelPos = e.GetPosition(hitResult.Item1);
                    insertBefore = itemRelPos.X < (hitResult.Item1.ActualWidth / 2.0);
                }

                var orderedDragged = Pages.Where(p => draggedItems.Contains(p)).ToList();

                foreach (var page in orderedDragged)
                {
                    Pages.Remove(page);
                }

                int insertIndex;
                if (targetPage != null && Pages.Contains(targetPage))
                {
                    int targetIdxInUpdatedList = Pages.IndexOf(targetPage);
                    insertIndex = insertBefore ? targetIdxInUpdatedList : targetIdxInUpdatedList + 1;
                }
                else
                {
                    insertIndex = Pages.Count;
                }

                for (int i = 0; i < orderedDragged.Count; i++)
                {
                    int currentInsert = Math.Min(insertIndex + i, Pages.Count);
                    Pages.Insert(currentInsert, orderedDragged[i]);
                }

                ReindexSequenceNumbers();
                UpdateUIState();

                LstPages.SelectedItems.Clear();
                foreach (var page in orderedDragged)
                {
                    LstPages.SelectedItems.Add(page);
                }

                TxtStatus.Text = $"Moved {orderedDragged.Count} page{(orderedDragged.Count == 1 ? "" : "s")}";
                e.Handled = true;
            }
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
            LstPages.SelectAll();
        }

        private void BtnDeselectAllPages_Click(object sender, RoutedEventArgs e)
        {
            LstPages.UnselectAll();
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
            var selectedPages = LstPages.SelectedItems.Cast<PdfPageItem>().ToList();
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

        private List<PdfPageItem> GetSelectedOrAllPages()
        {
            var selected = LstPages.SelectedItems.Cast<PdfPageItem>().ToList();
            return selected.Count > 0 ? selected : Pages.ToList();
        }

        #endregion

        #region Merge & Save Export

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

            int selectedCount = LstPages.SelectedItems.Count;
            TxtSelectedCountBadge.Text = selectedCount > 0 ? $"({selectedCount} selected)" : "(0 selected)";

            bool hasPages = Pages.Count > 0;
            PnlEmptyState.Visibility = hasPages ? Visibility.Collapsed : Visibility.Visible;
            GridGalleryContainer.Visibility = hasPages ? Visibility.Visible : Visibility.Collapsed;
            BtnMergeSave.IsEnabled = hasPages;

            bool hasSelection = selectedCount > 0;
            BtnRotateSelectedCW.IsEnabled = hasPages;
            BtnRotateSelectedCCW.IsEnabled = hasPages;
            BtnDeleteSelected.IsEnabled = hasSelection;
        }

        private void SetLoadingState(bool isLoading, string statusText)
        {
            TxtStatus.Text = statusText;
            ProgressStatus.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            ProgressStatus.IsIndeterminate = isLoading;
        }

        #endregion
    }
}