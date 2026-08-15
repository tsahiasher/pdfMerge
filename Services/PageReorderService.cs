using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using pdfMerge.Models;

namespace pdfMerge.Services
{
    /// <summary>
    /// Service managing page sequence numbering and file reordering (Priority 6).
    /// </summary>
    public static class PageReorderService
    {
        public static void ReindexSequenceNumbers(IList<PdfPageItem> pages)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].DisplayPageNumber = i + 1;
            }
        }

        public static void ReindexFilesOrder(IList<PdfFileItem> files)
        {
            for (int i = 0; i < files.Count; i++)
            {
                files[i].Order = i + 1;
                files[i].CanMoveUp = (i > 0);
                files[i].CanMoveDown = (i < files.Count - 1);
            }
        }

        public static bool HasCustomPageOrder(IList<PdfPageItem> pages, IList<PdfFileItem> files)
        {
            if (pages.Count <= 1) return false;

            var fileOrderDict = files.Select((f, idx) => new { f.FilePath, idx })
                                     .ToDictionary(x => x.FilePath, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var expectedOrder = pages.OrderBy(p => fileOrderDict.TryGetValue(p.SourceFilePath, out int order) ? order : int.MaxValue)
                                     .ThenBy(p => p.OriginalPageIndex)
                                     .ToList();

            for (int i = 0; i < pages.Count; i++)
            {
                if (pages[i] != expectedOrder[i])
                {
                    return true;
                }
            }

            return false;
        }

        public static void RebuildPagesFromFilesOrder(ObservableCollection<PdfPageItem> pages, IList<PdfFileItem> files)
        {
            var fileOrderDict = files.Select((f, idx) => new { f.FilePath, idx })
                                     .ToDictionary(x => x.FilePath, x => x.idx, StringComparer.OrdinalIgnoreCase);

            var ordered = pages.OrderBy(p => fileOrderDict.TryGetValue(p.SourceFilePath, out int order) ? order : int.MaxValue)
                               .ThenBy(p => p.OriginalPageIndex)
                               .ToList();

            pages.Clear();
            foreach (var page in ordered)
            {
                pages.Add(page);
            }
            ReindexSequenceNumbers(pages);
        }
    }
}
