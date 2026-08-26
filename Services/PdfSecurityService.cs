using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using WinPdfDocument = Windows.Data.Pdf.PdfDocument;
using PdfSharpReader = PdfSharp.Pdf.IO.PdfReader;
using PdfSharpOpenMode = PdfSharp.Pdf.IO.PdfDocumentOpenMode;
using PdfSharpException = PdfSharp.Pdf.IO.PdfReaderException;

namespace pdfMerge.Services
{
    /// <summary>
    /// Centralized service for PDF encryption detection, password verification, and session credential caching.
    /// </summary>
    public static class PdfSecurityService
    {
        private static readonly ConcurrentDictionary<string, string> _filePasswords = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retrieves the cached password for a file, if any.
        /// </summary>
        public static string? GetPassword(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            string fullPath = Path.GetFullPath(filePath);
            _filePasswords.TryGetValue(fullPath, out var pwd);
            return pwd;
        }

        /// <summary>
        /// Stores the verified password for a file in the session cache.
        /// </summary>
        public static void SetPassword(string filePath, string password)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            string fullPath = Path.GetFullPath(filePath);
            _filePasswords[fullPath] = password;
        }

        /// <summary>
        /// Removes the cached password for a file.
        /// </summary>
        public static void ClearPassword(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            string fullPath = Path.GetFullPath(filePath);
            _filePasswords.TryRemove(fullPath, out _);
        }

        /// <summary>
        /// Clears all cached passwords for the session.
        /// </summary>
        public static void ClearAll()
        {
            _filePasswords.Clear();
        }

        /// <summary>
        /// Checks whether the specified PDF file is encrypted / password-protected.
        /// </summary>
        public static bool IsFilePasswordProtected(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string fullPath = Path.GetFullPath(filePath);

            if (!File.Exists(fullPath) || PdfService.IsSupportedImageFile(fullPath))
            {
                return false;
            }

            try
            {
                using var stream = File.OpenRead(fullPath);
                using var doc = PdfSharpReader.Open(stream, PdfSharpOpenMode.Import);
                return false;
            }
            catch (PdfSharpException ex) when (IsPasswordRelatedException(ex))
            {
                return true;
            }
            catch (Exception ex) when (IsPasswordRelatedException(ex))
            {
                return true;
            }
        }

        /// <summary>
        /// Asynchronously verifies whether a provided password can successfully open the encrypted PDF.
        /// Tests both PdfSharp and Windows.Data.Pdf engines.
        /// </summary>
        public static async Task<(bool Success, string? ErrorMessage)> VerifyPasswordAsync(string filePath, string password)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return (false, "File path is empty.");
            }

            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath))
            {
                return (false, "File does not exist.");
            }

            try
            {
                // 1. Verify with PdfSharp
                using (var stream = File.OpenRead(fullPath))
                {
                    using (var doc = PdfSharpReader.Open(stream, password, PdfSharpOpenMode.Import))
                    {
                        if (doc.PageCount <= 0)
                        {
                            return (false, "Document contains no pages.");
                        }
                    }
                }

                // 2. Verify with Windows.Data.Pdf (WinRT engine)
                try
                {
                    StorageFile file = await StorageFile.GetFileFromPathAsync(fullPath);
                    var winDoc = await WinPdfDocument.LoadFromFileAsync(file, password);
                    if (winDoc == null)
                    {
                        return (false, "Failed to initialize PDF renderer.");
                    }
                }
                catch (Exception winEx)
                {
                    System.Diagnostics.Debug.WriteLine($"WinRT password test warning: {winEx.Message}");
                    // If PdfSharp opened it successfully, we consider it valid even if WinRT had a minor quirk
                }

                return (true, null);
            }
            catch (PdfSharpException ex) when (IsPasswordRelatedException(ex))
            {
                return (false, "Incorrect password. Please try again.");
            }
            catch (Exception ex)
            {
                if (IsPasswordRelatedException(ex))
                {
                    return (false, "Incorrect password. Please try again.");
                }
                return (false, $"Unable to open document: {ex.Message}");
            }
        }

        private static bool IsPasswordRelatedException(Exception ex)
        {
            string msg = ex.Message.ToLowerInvariant();
            return msg.Contains("password") ||
                   msg.Contains("protected") ||
                   msg.Contains("encrypt") ||
                   msg.Contains("security") ||
                   ex.GetType().Name.Contains("Encrypted") ||
                   ex.GetType().Name.Contains("Security");
        }
    }
}
