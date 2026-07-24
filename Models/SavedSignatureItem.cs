using System.Windows.Media.Imaging;

namespace pdfMerge.Models
{
    public class SavedSignatureItem
    {
        public string FilePath { get; set; } = string.Empty;
        public BitmapImage Image { get; set; } = null!;
    }
}
