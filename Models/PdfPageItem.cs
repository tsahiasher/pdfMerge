using System;
using System.IO;
using System.Windows.Media.Imaging;
using pdfMerge.Helpers;

namespace pdfMerge.Models
{
    public class PdfPageItem : ObservableObject
    {
        private Guid _id = Guid.NewGuid();
        private string _sourceFilePath = string.Empty;
        private string _sourceFileName = string.Empty;
        private int _originalPageIndex;
        private int _displayPageNumber;
        private int _rotation;
        private BitmapSource? _thumbnail;
        private bool _isSelected;
        private bool _isLoading = true;

        public Guid Id => _id;

        public string SourceFilePath
        {
            get => _sourceFilePath;
            set
            {
                if (SetProperty(ref _sourceFilePath, value))
                {
                    SourceFileName = Path.GetFileName(value);
                }
            }
        }

        public string SourceFileName
        {
            get => _sourceFileName;
            set => SetProperty(ref _sourceFileName, value);
        }

        public int OriginalPageIndex
        {
            get => _originalPageIndex;
            set => SetProperty(ref _originalPageIndex, value);
        }

        public int DisplayPageNumber
        {
            get => _displayPageNumber;
            set => SetProperty(ref _displayPageNumber, value);
        }

        public int Rotation
        {
            get => _rotation;
            set
            {
                // Normalize rotation to 0, 90, 180, 270
                int normalized = ((value % 360) + 360) % 360;
                if (SetProperty(ref _rotation, normalized))
                {
                    OnPropertyChanged(nameof(RotationText));
                }
            }
        }

        public string RotationText => Rotation == 0 ? "0°" : $"{Rotation}°";

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public void RotateClockwise()
        {
            Rotation = (Rotation + 90) % 360;
        }

        public void RotateCounterClockwise()
        {
            Rotation = (Rotation - 90 + 360) % 360;
        }
    }
}
