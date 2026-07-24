using System.Collections.Generic;
using System.Linq;
using pdfMerge.Models;

namespace pdfMerge.Services
{
    /// <summary>
    /// Service managing page selection operations (Priority 6).
    /// </summary>
    public static class PageSelectionService
    {
        public static void SelectAll(IEnumerable<PdfPageItem> pages)
        {
            foreach (var page in pages)
            {
                page.IsSelected = true;
            }
        }

        public static void DeselectAll(IEnumerable<PdfPageItem> pages)
        {
            foreach (var page in pages)
            {
                page.IsSelected = false;
            }
        }

        public static List<PdfPageItem> GetSelectedOrAllPages(IEnumerable<PdfPageItem> pages)
        {
            var list = pages.ToList();
            var selected = list.Where(p => p.IsSelected).ToList();
            return selected.Count > 0 ? selected : list;
        }
    }
}
