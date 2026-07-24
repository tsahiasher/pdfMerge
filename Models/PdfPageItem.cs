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
        private bool _isBeingDragged;
        private AppliedSignature? _pageSignature;

        // Dynamic Card Zoom Dimensions
        private double _cardWidth = 205;
        private double _cardHeight = 305;
        private double _imageMaxHeight = 205;
        private double _imageMaxWidth = 175;

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

        public int OriginalPageNumber => OriginalPageIndex + 1;

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

        public bool IsBeingDragged
        {
            get => _isBeingDragged;
            set => SetProperty(ref _isBeingDragged, value);
        }

        public AppliedSignature? PageSignature
        {
            get => _pageSignature;
            set => SetProperty(ref _pageSignature, value);
        }

        public double CardWidth
        {
            get => _cardWidth;
            set => SetProperty(ref _cardWidth, value);
        }

        public double CardHeight
        {
            get => _cardHeight;
            set => SetProperty(ref _cardHeight, value);
        }

        public double ImageMaxHeight
        {
            get => _imageMaxHeight;
            set => SetProperty(ref _imageMaxHeight, value);
        }

        public double ImageMaxWidth
        {
            get => _imageMaxWidth;
            set => SetProperty(ref _imageMaxWidth, value);
        }

        public void RotateClockwise()
        {
            Rotation = (Rotation + 90) % 360;
        }

        public void RotateCounterClockwise()
        {
            Rotation = (Rotation - 90 + 360) % 360;
        }

        public PdfPageItem CloneSnapshot()
        {
            return new PdfPageItem
            {
                _id = this._id,
                SourceFilePath = this.SourceFilePath,
                OriginalPageIndex = this.OriginalPageIndex,
                DisplayPageNumber = this.DisplayPageNumber,
                Rotation = this.Rotation,
                Thumbnail = this.Thumbnail,
                IsSelected = false,
                IsLoading = false,
                IsBeingDragged = false,
                PageSignature = this.PageSignature?.Clone(),
                CardWidth = this.CardWidth,
                CardHeight = this.CardHeight,
                ImageMaxHeight = this.ImageMaxHeight,
                ImageMaxWidth = this.ImageMaxWidth
            };
        }
    }
}
