using System.Windows.Media.Imaging;

namespace pdfMerge.Models
{
    public class AppliedSignature
    {
        public BitmapSource SignatureImage { get; set; } = null!;
        public double RelX { get; set; }
        public double RelY { get; set; }
        public double RelWidth { get; set; }
        public double RelHeight { get; set; }

        public AppliedSignature Clone()
        {
            return new AppliedSignature
            {
                SignatureImage = this.SignatureImage,
                RelX = this.RelX,
                RelY = this.RelY,
                RelWidth = this.RelWidth,
                RelHeight = this.RelHeight
            };
        }
    }
}
