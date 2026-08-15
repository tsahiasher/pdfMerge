using System;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace pdfMerge.Helpers
{
    /// <summary>
    /// Utility loading Open Hand and Closed Hand cursors from the project Cursors folder / assembly resources.
    /// </summary>
    public static class CursorUtility
    {
        private static Cursor? _openHand;
        private static Cursor? _closedHand;

        public static Cursor OpenHand => _openHand ??= LoadCursorFromProject("openhand.cur", Cursors.Hand);
        public static Cursor ClosedHand => _closedHand ??= LoadCursorFromProject("closedhand.cur", Cursors.SizeAll);

        private static Cursor LoadCursorFromProject(string fileName, Cursor fallback)
        {
            try
            {
                // 1. Try WPF Assembly Resource Stream (pack://application:,,,/Cursors/filename)
                var resourceUri = new Uri($"/Cursors/{fileName}", UriKind.Relative);
                var streamInfo = Application.GetResourceStream(resourceUri);
                if (streamInfo?.Stream != null)
                {
                    return new Cursor(streamInfo.Stream);
                }
            }
            catch { }

            try
            {
                // 2. Try Relative File Path (Cursors/filename)
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cursors", fileName);
                if (File.Exists(localPath))
                {
                    using var stream = File.OpenRead(localPath);
                    return new Cursor(stream);
                }

                string curDir = Path.Combine(Directory.GetCurrentDirectory(), "Cursors", fileName);
                if (File.Exists(curDir))
                {
                    using var stream = File.OpenRead(curDir);
                    return new Cursor(stream);
                }
            }
            catch { }

            return fallback;
        }
    }
}
