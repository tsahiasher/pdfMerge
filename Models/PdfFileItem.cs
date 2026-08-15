using System.IO;
using pdfMerge.Helpers;

namespace pdfMerge.Models
{
    public class PdfFileItem : ObservableObject
    {
        private string _filePath = string.Empty;
        private string _fileName = string.Empty;
        private int _pageCount;
        private long _fileSizeBytes;
        private int _order;
        private string _documentColorHex = "#0EA5E9";

        public string DocumentColorHex
        {
            get => _documentColorHex;
            set => SetProperty(ref _documentColorHex, value);
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    FileName = Path.GetFileName(value);
                    if (File.Exists(value))
                    {
                        var info = new FileInfo(value);
                        FileSizeBytes = info.Length;
                    }
                }
            }
        }

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        public int PageCount
        {
            get => _pageCount;
            set => SetProperty(ref _pageCount, value);
        }

        public long FileSizeBytes
        {
            get => _fileSizeBytes;
            set
            {
                if (SetProperty(ref _fileSizeBytes, value))
                {
                    OnPropertyChanged(nameof(FileSizeFormatted));
                }
            }
        }

        public int Order
        {
            get => _order;
            set => SetProperty(ref _order, value);
        }

        public string FileSizeFormatted
        {
            get
            {
                double kb = FileSizeBytes / 1024.0;
                if (kb < 1024)
                    return $"{kb:F1} KB";
                double mb = kb / 1024.0;
                return $"{mb:F1} MB";
            }
        }
    }
}
