using System.Collections.Generic;
using System.Linq;
using pdfMerge.Helpers;

namespace pdfMerge.Models
{
    public class SplitRangeItem : ObservableObject
    {
        private string _name = string.Empty;
        private string _rangeText = string.Empty;
        private List<PdfPageItem> _pages = new();

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string RangeText
        {
            get => _rangeText;
            set => SetProperty(ref _rangeText, value);
        }

        public List<PdfPageItem> Pages
        {
            get => _pages;
            set
            {
                if (SetProperty(ref _pages, value))
                {
                    OnPropertyChanged(nameof(PageCount));
                    OnPropertyChanged(nameof(PageSummaryText));
                }
            }
        }

        public int PageCount => Pages.Count;

        public string PageSummaryText => Pages.Count > 0 
            ? $"Pages: {string.Join(", ", Pages.Select(p => p.DisplayPageNumber))}" 
            : "No pages selected";
    }
}
