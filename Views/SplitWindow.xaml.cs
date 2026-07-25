using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using pdfMerge.Models;
using pdfMerge.Services;

namespace pdfMerge.Views
{
    public partial class SplitWindow : Window
    {
        private readonly List<PdfPageItem> _allPages;
        public ObservableCollection<SplitRangeItem> SplitRanges { get; } = new ObservableCollection<SplitRangeItem>();

        public SplitWindow(List<PdfPageItem> allPages)
        {
            InitializeComponent();

            _allPages = allPages ?? new List<PdfPageItem>();
            LstSplitRanges.ItemsSource = SplitRanges;

            SplitRanges.CollectionChanged += SplitRanges_CollectionChanged;

            if (TxtTotalPagesBadge != null)
            {
                TxtTotalPagesBadge.Text = $"{_allPages.Count} Page{(_allPages.Count == 1 ? "" : "s")} Available";
            }

            if (TxtToPage != null && _allPages.Count > 0)
            {
                TxtToPage.Text = _allPages.Count.ToString();
            }

            bool hasSelected = _allPages.Any(p => p.IsSelected);
            if (BtnSplitSelectedOnly != null)
            {
                BtnSplitSelectedOnly.IsEnabled = hasSelected;
            }

            UpdateUIState();
        }

        private void SplitRanges_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateUIState();
        }

        private void UpdateUIState()
        {
            int count = SplitRanges.Count;
            if (TxtPartCountBadge != null)
            {
                TxtPartCountBadge.Text = $"{count} Part{(count == 1 ? "" : "s")} Configured";
            }

            if (BdrEmptyState != null)
            {
                BdrEmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            if (BtnSplitSave != null)
            {
                BtnSplitSave.IsEnabled = count > 0;
            }
        }

        #region Add Ranges Logic

        private void BtnAddRange_Click(object sender, RoutedEventArgs e)
        {
            List<PdfPageItem> targetPages = new();

            // Try range text box first if non-empty
            string rangeText = TxtRangeInput.Text.Trim();
            if (!string.IsNullOrWhiteSpace(rangeText))
            {
                targetPages = ParseCustomPageRange(rangeText, _allPages);
            }
            else if (int.TryParse(TxtFromPage.Text, out int start) && int.TryParse(TxtToPage.Text, out int end))
            {
                for (int i = Math.Max(1, start); i <= Math.Min(_allPages.Count, end); i++)
                {
                    var item = _allPages.FirstOrDefault(p => p.DisplayPageNumber == i);
                    if (item != null && !targetPages.Contains(item))
                    {
                        targetPages.Add(item);
                    }
                }
                rangeText = $"{start}-{end}";
            }

            if (targetPages.Count == 0)
            {
                MessageBox.Show(this, "Please enter a valid page number or range of pages to split.", "Invalid Range", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string partName = TxtPartName.Text.Trim();
            if (string.IsNullOrWhiteSpace(partName))
            {
                partName = $"Part {SplitRanges.Count + 1}";
            }

            var rangeItem = new SplitRangeItem
            {
                Name = partName,
                RangeText = rangeText,
                Pages = targetPages
            };

            SplitRanges.Add(rangeItem);

            // Auto-advance inputs for quick adding of next part
            TxtPartName.Text = string.Empty;
            TxtRangeInput.Text = string.Empty;
            int maxAddedPage = targetPages.Max(p => p.DisplayPageNumber);
            if (maxAddedPage < _allPages.Count)
            {
                TxtFromPage.Text = (maxAddedPage + 1).ToString();
                TxtToPage.Text = _allPages.Count.ToString();
            }
        }

        private List<PdfPageItem> ParseCustomPageRange(string rangeText, List<PdfPageItem> allPages)
        {
            var result = new List<PdfPageItem>();
            var lookup = allPages.ToDictionary(p => p.DisplayPageNumber, p => p);
            var seen = new HashSet<PdfPageItem>();

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
                System.Diagnostics.Debug.WriteLine($"Error parsing page range '{rangeText}': {ex.Message}");
            }

            return result;
        }

        #endregion

        #region Quick Presets

        private void BtnSplitSinglePages_Click(object sender, RoutedEventArgs e)
        {
            if (_allPages.Count == 0) return;

            SplitRanges.Clear();
            for (int i = 0; i < _allPages.Count; i++)
            {
                var page = _allPages[i];
                SplitRanges.Add(new SplitRangeItem
                {
                    Name = $"Page {page.DisplayPageNumber}",
                    RangeText = page.DisplayPageNumber.ToString(),
                    Pages = new List<PdfPageItem> { page }
                });
            }
        }

        private void BtnSplitEveryN_Click(object sender, RoutedEventArgs e)
        {
            if (_allPages.Count == 0) return;

            if (!int.TryParse(TxtChunkSize.Text, out int chunkSize) || chunkSize <= 0)
            {
                chunkSize = 2;
                TxtChunkSize.Text = "2";
            }

            SplitRanges.Clear();
            int partIndex = 1;
            for (int i = 0; i < _allPages.Count; i += chunkSize)
            {
                var chunkPages = _allPages.Skip(i).Take(chunkSize).ToList();
                int startPage = chunkPages.First().DisplayPageNumber;
                int endPage = chunkPages.Last().DisplayPageNumber;

                SplitRanges.Add(new SplitRangeItem
                {
                    Name = $"Part {partIndex++} (P. {startPage}-{endPage})",
                    RangeText = startPage == endPage ? $"{startPage}" : $"{startPage}-{endPage}",
                    Pages = chunkPages
                });
            }
        }

        private void BtnSplitSelectedOnly_Click(object sender, RoutedEventArgs e)
        {
            var selectedPages = _allPages.Where(p => p.IsSelected).ToList();
            if (selectedPages.Count == 0)
            {
                MessageBox.Show(this, "No pages are currently selected in the gallery.", "No Pages Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SplitRanges.Add(new SplitRangeItem
            {
                Name = $"Selected Pages ({selectedPages.Count})",
                RangeText = string.Join(",", selectedPages.Select(p => p.DisplayPageNumber)),
                Pages = selectedPages
            });
        }

        #endregion

        #region Range Management & Saving

        private void BtnRemoveRange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SplitRangeItem item)
            {
                SplitRanges.Remove(item);
            }
        }

        private void BtnClearRanges_Click(object sender, RoutedEventArgs e)
        {
            if (SplitRanges.Count > 0)
            {
                SplitRanges.Clear();
            }
        }

        private async void BtnSplitSave_Click(object sender, RoutedEventArgs e)
        {
            if (SplitRanges.Count == 0)
            {
                MessageBox.Show(this, "Please add at least one split range before saving.", "No Ranges Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PDF Document (*.pdf)|*.pdf",
                Title = "Choose Base Name / Directory to Save Split PDF Parts",
                FileName = "Split_Document.pdf"
            };

            if (dialog.ShowDialog(this) == true)
            {
                try
                {
                    string folder = Path.GetDirectoryName(dialog.FileName) ?? Environment.CurrentDirectory;
                    string baseName = Path.GetFileNameWithoutExtension(dialog.FileName);

                    int count = 0;
                    foreach (var part in SplitRanges)
                    {
                        if (part.Pages.Count == 0) continue;

                        string sanitizedLabel = string.Concat(part.Name.Split(Path.GetInvalidFileNameChars())).Trim();
                        if (string.IsNullOrWhiteSpace(sanitizedLabel)) sanitizedLabel = $"Part_{count + 1}";

                        string targetPath = Path.Combine(folder, $"{baseName}_{sanitizedLabel}.pdf");

                        await PdfService.MergeAndSavePdfAsync(part.Pages, targetPath);
                        count++;
                    }

                    MessageBox.Show(this, $"Successfully split PDF into {count} part(s) in:\n{folder}", "Split Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Error saving split PDF files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion
    }
}
