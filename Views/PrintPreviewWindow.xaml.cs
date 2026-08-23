using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using pdfMerge.Helpers;
using pdfMerge.Models;
using pdfMerge.Services;

namespace pdfMerge.Views
{
    public partial class PrintPreviewWindow : Window
    {
        private readonly List<PdfPageItem> _allPages;

        private List<PdfPageItem> _pagesToPrint = new List<PdfPageItem>();
        private readonly Dictionary<int, BitmapSource> _previewCache = new Dictionary<int, BitmapSource>();
        private const int MaxCacheSize = 5;

        private int _currentPreviewIndex = 0;
        private CancellationTokenSource? _renderCts;
        private int _renderVersion = 0; // #7: Prevent stale preview results

        public PrintPreviewWindow(List<PdfPageItem> allPages)
        {
            InitializeComponent();

            _allPages = allPages ?? new List<PdfPageItem>();

            PopulatePrinters();

            bool hasSelected = _allPages.Any(p => p.IsSelected);
            if (RdoRangeSelected != null)
            {
                RdoRangeSelected.IsEnabled = hasSelected;
                if (hasSelected)
                {
                    RdoRangeSelected.IsChecked = true;
                }
            }

            // #6: Move async work from constructor to Loaded event
            Loaded += async (_, _) => await EvaluatePrintRangeAsync();
        }

        // #5: Dispose CTS and cancel renders on window close
        protected override void OnClosed(EventArgs e)
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = null;
            _previewCache.Clear();
            base.OnClosed(e);
        }

        // #3: Dispose LocalPrintServer after enumeration, store only printer names
        private void PopulatePrinters()
        {
            try
            {
                using var printServer = new LocalPrintServer();
                using var queues = printServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });

                var queueItems = new List<PrintQueueItem>();
                string defaultQueueName = string.Empty;

                try
                {
                    using var defaultQueue = printServer.DefaultPrintQueue;
                    if (defaultQueue != null)
                    {
                        defaultQueueName = defaultQueue.Name;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting default printer: {ex.Message}");
                }

                PrintQueueItem? defaultItem = null;

                foreach (var q in queues)
                {
                    var item = new PrintQueueItem { Name = q.Name, FullName = q.FullName };
                    queueItems.Add(item);

                    if (!string.IsNullOrEmpty(defaultQueueName) && q.Name.Equals(defaultQueueName, StringComparison.OrdinalIgnoreCase))
                    {
                        defaultItem = item;
                    }

                    q.Dispose();
                }

                if (CmbPrinters != null)
                {
                    CmbPrinters.ItemsSource = queueItems;
                    CmbPrinters.DisplayMemberPath = "FullName";
                    if (defaultItem != null)
                    {
                        CmbPrinters.SelectedItem = defaultItem;
                    }
                    else if (queueItems.Count > 0)
                    {
                        CmbPrinters.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enumerating printers: {ex.Message}");
            }
        }

        private void CmbPrinters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Printer selection changed
        }

        private void BtnCopyUp_Click(object sender, RoutedEventArgs e)
        {
            if (TxtCopies != null)
            {
                if (int.TryParse(TxtCopies.Text, out int c))
                {
                    TxtCopies.Text = (c + 1).ToString();
                }
                else
                {
                    TxtCopies.Text = "1";
                }
            }
        }

        private void BtnCopyDown_Click(object sender, RoutedEventArgs e)
        {
            if (TxtCopies != null)
            {
                if (int.TryParse(TxtCopies.Text, out int c) && c > 1)
                {
                    TxtCopies.Text = (c - 1).ToString();
                }
                else
                {
                    TxtCopies.Text = "1";
                }
            }
        }

        private void PrintRange_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (_allPages != null)
            {
                _ = EvaluatePrintRangeAsync();
            }
        }

        private void TxtCustomRange_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RdoRangeCustom != null && RdoRangeCustom.IsChecked == true && _allPages != null)
            {
                _ = EvaluatePrintRangeAsync();
            }
        }

        private void RdoColor_Checked(object sender, RoutedEventArgs e)
        {
            UpdateLivePreviewDisplay();
        }

        private void RdoMonochrome_Checked(object sender, RoutedEventArgs e)
        {
            UpdateLivePreviewDisplay();
        }

        private async Task EvaluatePrintRangeAsync()
        {
            if (_allPages == null || _allPages.Count == 0) return;

            try
            {
                List<PdfPageItem> targetPages;

                if (RdoRangeSelected != null && RdoRangeSelected.IsChecked == true)
                {
                    targetPages = _allPages.Where(p => p.IsSelected).ToList();
                    if (targetPages.Count == 0) targetPages = _allPages.ToList();
                }
                else if (RdoRangeCustom != null && RdoRangeCustom.IsChecked == true && TxtCustomRange != null)
                {
                    targetPages = ParseCustomPageRange(TxtCustomRange.Text, _allPages);

                    // #15: If non-empty input produced zero valid pages, show validation error
                    if (targetPages.Count == 0 && !string.IsNullOrWhiteSpace(TxtCustomRange.Text))
                    {
                        _pagesToPrint = new List<PdfPageItem>();
                        _previewCache.Clear();
                        if (TxtPreviewRangeInfo != null)
                        {
                            TxtPreviewRangeInfo.Text = "Invalid range";
                        }
                        if (BtnPrintNow != null) BtnPrintNow.IsEnabled = false;
                        UpdateLivePreviewDisplay();
                        return;
                    }
                }
                else
                {
                    targetPages = _allPages.ToList();
                }

                _pagesToPrint = targetPages.ToList();
                _previewCache.Clear();

                if (TxtPreviewRangeInfo != null)
                {
                    TxtPreviewRangeInfo.Text = $"{_pagesToPrint.Count} Page{(_pagesToPrint.Count == 1 ? "" : "s")} Selected";
                }
                if (BtnPrintNow != null) BtnPrintNow.IsEnabled = _pagesToPrint.Count > 0;

                _currentPreviewIndex = 0;
                await RenderCurrentPreviewPageAsync();
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error evaluating print range: {ex.Message}");
            }
        }

        // #15: Improved page-range parsing with O(1) lookup and HashSet dedup
        private List<PdfPageItem> ParseCustomPageRange(string rangeText, List<PdfPageItem> allPages)
        {
            if (string.IsNullOrWhiteSpace(rangeText)) return allPages.ToList();

            var lookup = allPages.ToDictionary(p => p.DisplayPageNumber, p => p);
            var seen = new HashSet<PdfPageItem>();
            var result = new List<PdfPageItem>();

            try
            {
                string[] parts = rangeText.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    string trimmed = part.Trim();
                    if (trimmed.Contains('-'))
                    {
                        string[] rangeParts = trimmed.Split('-');
                        if (rangeParts.Length == 2 && int.TryParse(rangeParts[0], out int start) && int.TryParse(rangeParts[1], out int end))
                        {
                            for (int i = Math.Max(1, start); i <= Math.Min(allPages.Count, end); i++)
                            {
                                if (lookup.TryGetValue(i, out var item) && seen.Add(item))
                                {
                                    result.Add(item);
                                }
                            }
                        }
                    }
                    else if (int.TryParse(trimmed, out int pageNum))
                    {
                        if (lookup.TryGetValue(pageNum, out var item) && seen.Add(item))
                        {
                            result.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing custom page range '{rangeText}': {ex.Message}");
            }

            return result;
        }

        private async Task RenderCurrentPreviewPageAsync()
        {
            if (_pagesToPrint.Count == 0)
            {
                UpdateLivePreviewDisplay();
                return;
            }

            if (_currentPreviewIndex < 0 || _currentPreviewIndex >= _pagesToPrint.Count)
            {
                _currentPreviewIndex = 0;
            }

            // Check bounded preview cache first
            if (_previewCache.TryGetValue(_currentPreviewIndex, out _))
            {
                UpdateLivePreviewDisplay();
                PrefetchAdjacentPages(_currentPreviewIndex);
                return;
            }

            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            var token = _renderCts.Token;

            int myVersion = ++_renderVersion;

            if (PnlPreviewLoading != null) PnlPreviewLoading.Visibility = Visibility.Visible;

            try
            {
                var pageItem = _pagesToPrint[_currentPreviewIndex];
                BitmapSource? pageImage = await PageRenderService.RenderCompositePageAsync(pageItem, 1600, isMonochrome: false, token: token);

                if (pageImage != null && myVersion == _renderVersion && !token.IsCancellationRequested)
                {
                    _previewCache[_currentPreviewIndex] = pageImage;
                    TrimCache(_currentPreviewIndex);
                    UpdateLivePreviewDisplay();
                    PrefetchAdjacentPages(_currentPreviewIndex);
                }
            }
            catch (OperationCanceledException) { /* expected on fast navigate */ }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error rendering preview page: {ex.Message}");
            }
            finally
            {
                if (myVersion == _renderVersion && PnlPreviewLoading != null)
                {
                    PnlPreviewLoading.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async void PrefetchAdjacentPages(int currentIndex)
        {
            int[] targets = new[] { currentIndex + 1, currentIndex - 1 };
            foreach (int idx in targets)
            {
                if (idx >= 0 && idx < _pagesToPrint.Count && !_previewCache.ContainsKey(idx))
                {
                    try
                    {
                        var item = _pagesToPrint[idx];
                        var bmp = await PageRenderService.RenderCompositePageAsync(item, 1600, isMonochrome: false);

                        if (bmp != null)
                        {
                            _previewCache[idx] = bmp;
                            TrimCache(currentIndex);
                        }
                    }
                    catch { }
                }
            }
        }

        private void TrimCache(int currentIndex)
        {
            while (_previewCache.Count > MaxCacheSize)
            {
                int farthestKey = _previewCache.Keys.OrderByDescending(k => Math.Abs(k - currentIndex)).First();
                _previewCache.Remove(farthestKey);
            }
        }

        private void UpdateLivePreviewDisplay()
        {
            if (ImgLivePagePreview == null || TxtPageNavigation == null || BtnPrevPage == null || BtnNextPage == null)
            {
                return;
            }

            if (_pagesToPrint == null || _pagesToPrint.Count == 0)
            {
                ImgLivePagePreview.Source = null;
                TxtPageNavigation.Text = "Page 0 of 0";
                BtnPrevPage.IsEnabled = false;
                BtnNextPage.IsEnabled = false;
                return;
            }

            if (_currentPreviewIndex < 0 || _currentPreviewIndex >= _pagesToPrint.Count)
            {
                _currentPreviewIndex = 0;
            }

            TxtPageNavigation.Text = $"Page {_currentPreviewIndex + 1} of {_pagesToPrint.Count}";
            BtnPrevPage.IsEnabled = _currentPreviewIndex > 0;
            BtnNextPage.IsEnabled = _currentPreviewIndex < _pagesToPrint.Count - 1;

            if (_previewCache.TryGetValue(_currentPreviewIndex, out BitmapSource? currentBitmap) && currentBitmap != null)
            {
                if (RdoMonochrome != null && RdoMonochrome.IsChecked == true)
                {
                    currentBitmap = BitmapUtilities.ConvertToGrayscale(currentBitmap);
                }

                ImgLivePagePreview.Source = currentBitmap;
            }
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreviewIndex > 0)
            {
                _currentPreviewIndex--;
                _ = RenderCurrentPreviewPageAsync();
            }
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreviewIndex < _pagesToPrint.Count - 1)
            {
                _currentPreviewIndex++;
                _ = RenderCurrentPreviewPageAsync();
            }
        }

        private void BtnPrintNow_Click(object sender, RoutedEventArgs e)
        {
            if (_pagesToPrint.Count == 0)
            {
                MessageBox.Show(this, "No valid pages available to print.", "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LocalPrintServer? printServer = null;
            try
            {
                var printDialog = new PrintDialog();

                if (CmbPrinters != null && CmbPrinters.SelectedItem is PrintQueueItem selectedItem && !string.IsNullOrEmpty(selectedItem.FullName))
                {
                    printServer = new LocalPrintServer();
                    printDialog.PrintQueue = printServer.GetPrintQueue(selectedItem.FullName);
                }

                PrintTicket ticket = printDialog.PrintTicket ?? new PrintTicket();

                if (TxtCopies != null && int.TryParse(TxtCopies.Text, out int copyCount) && copyCount > 0)
                {
                    ticket.CopyCount = copyCount;
                }

                bool isMonochrome = RdoMonochrome != null && RdoMonochrome.IsChecked == true;
                ticket.OutputColor = isMonochrome ? OutputColor.Grayscale : OutputColor.Color;

                if (CmbDuplex != null)
                {
                    if (CmbDuplex.SelectedIndex == 1)
                    {
                        ticket.Duplexing = Duplexing.TwoSidedLongEdge;
                    }
                    else if (CmbDuplex.SelectedIndex == 2)
                    {
                        ticket.Duplexing = Duplexing.TwoSidedShortEdge;
                    }
                    else
                    {
                        ticket.Duplexing = Duplexing.OneSided;
                    }
                }

                printDialog.PrintTicket = ticket;

                Size printArea = new Size(printDialog.PrintableAreaWidth > 0 ? printDialog.PrintableAreaWidth : 792, printDialog.PrintableAreaHeight > 0 ? printDialog.PrintableAreaHeight : 1122);

                var paginator = new PdfDocumentPaginator(_pagesToPrint, printArea, isMonochrome);

                printDialog.PrintDocument(paginator, "PDF Merge Print Job");

                MessageBox.Show(this, $"Document sent to printer successfully!", "Print Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to print document: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                printServer?.Dispose();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _renderCts?.Cancel();
            DialogResult = false;
            Close();
        }
    }
}
