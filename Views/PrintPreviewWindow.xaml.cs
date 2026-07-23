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
using pdfMerge.Models;
using pdfMerge.Services;

namespace pdfMerge.Views
{
    public class PrintQueueItem
    {
        public string Name { get; set; } = string.Empty;
        public PrintQueue Queue { get; set; } = null!;
    }

    public partial class PrintPreviewWindow : Window
    {
        private readonly List<PdfPageItem> _allPages;
        private readonly PdfService _pdfService;

        private List<PdfPageItem> _pagesToPrint = new List<PdfPageItem>();
        private List<BitmapSource> _renderedPageBitmaps = new List<BitmapSource>();
        private int _currentPreviewIndex = 0;
        private CancellationTokenSource? _renderCts;

        public PrintPreviewWindow(List<PdfPageItem> allPages, PdfService pdfService)
        {
            InitializeComponent();

            _allPages = allPages ?? new List<PdfPageItem>();
            _pdfService = pdfService;

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

            EvaluatePrintRange();
        }

        private void PopulatePrinters()
        {
            try
            {
                var printServer = new LocalPrintServer();
                var queues = printServer.GetPrintQueues(new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections });

                var queueItems = new List<PrintQueueItem>();
                string defaultQueueName = string.Empty;

                try
                {
                    defaultQueueName = printServer.DefaultPrintQueue.Name;
                }
                catch { }

                PrintQueueItem? defaultItem = null;

                foreach (var q in queues)
                {
                    var item = new PrintQueueItem { Name = q.FullName, Queue = q };
                    queueItems.Add(item);

                    if (!string.IsNullOrEmpty(defaultQueueName) && q.Name.Equals(defaultQueueName, StringComparison.OrdinalIgnoreCase))
                    {
                        defaultItem = item;
                    }
                }

                if (CmbPrinters != null)
                {
                    CmbPrinters.ItemsSource = queueItems;
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
                EvaluatePrintRange();
            }
        }

        private void TxtCustomRange_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (RdoRangeCustom != null && RdoRangeCustom.IsChecked == true && _allPages != null)
            {
                EvaluatePrintRange();
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

        private async void EvaluatePrintRange()
        {
            if (_allPages == null || _allPages.Count == 0) return;

            List<PdfPageItem> targetPages;

            if (RdoRangeSelected != null && RdoRangeSelected.IsChecked == true)
            {
                targetPages = _allPages.Where(p => p.IsSelected).ToList();
                if (targetPages.Count == 0) targetPages = _allPages.ToList();
            }
            else if (RdoRangeCustom != null && RdoRangeCustom.IsChecked == true && TxtCustomRange != null)
            {
                targetPages = ParseCustomPageRange(TxtCustomRange.Text, _allPages);
            }
            else
            {
                targetPages = _allPages.ToList();
            }

            _pagesToPrint = targetPages.ToList();

            if (TxtPreviewRangeInfo != null)
            {
                TxtPreviewRangeInfo.Text = $"{_pagesToPrint.Count} Page{(_pagesToPrint.Count == 1 ? "" : "s")} Selected";
            }

            _currentPreviewIndex = 0;
            await RenderAllPreviewPagesAsync(_pagesToPrint.ToList());
        }

        private List<PdfPageItem> ParseCustomPageRange(string rangeText, List<PdfPageItem> allPages)
        {
            var result = new List<PdfPageItem>();
            if (string.IsNullOrWhiteSpace(rangeText)) return allPages.ToList();

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
                                var item = allPages.FirstOrDefault(p => p.DisplayPageNumber == i);
                                if (item != null && !result.Contains(item))
                                {
                                    result.Add(item);
                                }
                            }
                        }
                    }
                    else if (int.TryParse(trimmed, out int pageNum))
                    {
                        var item = allPages.FirstOrDefault(p => p.DisplayPageNumber == pageNum);
                        if (item != null && !result.Contains(item))
                        {
                            result.Add(item);
                        }
                    }
                }
            }
            catch { }

            return result.Count > 0 ? result : allPages.ToList();
        }

        private async Task RenderAllPreviewPagesAsync(List<PdfPageItem> pagesToRender)
        {
            _renderCts?.Cancel();
            _renderCts = new CancellationTokenSource();
            var token = _renderCts.Token;

            if (PnlPreviewLoading != null) PnlPreviewLoading.Visibility = Visibility.Visible;

            var newBitmaps = new List<BitmapSource>();

            foreach (var pageItem in pagesToRender)
            {
                if (token.IsCancellationRequested) return;

                BitmapSource? pageImage = await _pdfService.RenderPageThumbnailAsync(pageItem.SourceFilePath, pageItem.OriginalPageIndex, 1600);
                if (pageImage == null && pageItem.Thumbnail != null)
                {
                    pageImage = pageItem.Thumbnail;
                }

                if (token.IsCancellationRequested) return;

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

                    newBitmaps.Add(pageImage);
                }
            }

            if (token.IsCancellationRequested) return;

            _renderedPageBitmaps = newBitmaps;

            if (PnlPreviewLoading != null) PnlPreviewLoading.Visibility = Visibility.Collapsed;

            if (_currentPreviewIndex >= _renderedPageBitmaps.Count)
            {
                _currentPreviewIndex = Math.Max(0, _renderedPageBitmaps.Count - 1);
            }

            UpdateLivePreviewDisplay();
        }

        private void UpdateLivePreviewDisplay()
        {
            if (ImgLivePagePreview == null || TxtPageNavigation == null || BtnPrevPage == null || BtnNextPage == null)
            {
                return;
            }

            if (_renderedPageBitmaps == null || _renderedPageBitmaps.Count == 0)
            {
                ImgLivePagePreview.Source = null;
                TxtPageNavigation.Text = "Page 0 of 0";
                BtnPrevPage.IsEnabled = false;
                BtnNextPage.IsEnabled = false;
                return;
            }

            if (_currentPreviewIndex < 0 || _currentPreviewIndex >= _renderedPageBitmaps.Count)
            {
                _currentPreviewIndex = 0;
            }

            TxtPageNavigation.Text = $"Page {_currentPreviewIndex + 1} of {_renderedPageBitmaps.Count}";
            BtnPrevPage.IsEnabled = _currentPreviewIndex > 0;
            BtnNextPage.IsEnabled = _currentPreviewIndex < _renderedPageBitmaps.Count - 1;

            BitmapSource currentBitmap = _renderedPageBitmaps[_currentPreviewIndex];

            if (RdoMonochrome != null && RdoMonochrome.IsChecked == true)
            {
                currentBitmap = ConvertToGrayscale(currentBitmap);
            }

            ImgLivePagePreview.Source = currentBitmap;
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreviewIndex > 0)
            {
                _currentPreviewIndex--;
                UpdateLivePreviewDisplay();
            }
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreviewIndex < _renderedPageBitmaps.Count - 1)
            {
                _currentPreviewIndex++;
                UpdateLivePreviewDisplay();
            }
        }

        private BitmapSource RotateBitmap(BitmapSource source, int angle)
        {
            var transformed = new TransformedBitmap(source, new RotateTransform(angle));
            transformed.Freeze();
            return transformed;
        }

        private BitmapSource ConvertToGrayscale(BitmapSource source)
        {
            var grayBitmap = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            grayBitmap.Freeze();
            return grayBitmap;
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

        private void BtnPrintNow_Click(object sender, RoutedEventArgs e)
        {
            if (_renderedPageBitmaps.Count == 0)
            {
                MessageBox.Show(this, "No valid pages available to print.", "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var printDialog = new PrintDialog();

                if (CmbPrinters != null && CmbPrinters.SelectedItem is PrintQueueItem selectedItem && selectedItem.Queue != null)
                {
                    printDialog.PrintQueue = selectedItem.Queue;
                }

                PrintTicket ticket = printDialog.PrintTicket ?? new PrintTicket();

                if (TxtCopies != null && int.TryParse(TxtCopies.Text, out int copyCount) && copyCount > 0)
                {
                    ticket.CopyCount = copyCount;
                }

                if (RdoMonochrome != null)
                {
                    ticket.OutputColor = RdoMonochrome.IsChecked == true ? OutputColor.Grayscale : OutputColor.Color;
                }

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

                var finalBitmaps = _renderedPageBitmaps.Select(b => (RdoMonochrome != null && RdoMonochrome.IsChecked == true) ? ConvertToGrayscale(b) : b).ToList();
                Size printArea = new Size(printDialog.PrintableAreaWidth > 0 ? printDialog.PrintableAreaWidth : 792, printDialog.PrintableAreaHeight > 0 ? printDialog.PrintableAreaHeight : 1122);

                var paginator = new PdfDocumentPaginator(finalBitmaps, printArea);

                printDialog.PrintDocument(paginator, "PDF Merge Print Job");

                MessageBox.Show(this, $"Document sent to printer '{printDialog.PrintQueue.Name}' successfully!", "Print Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to print document: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
