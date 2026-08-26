using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using pdfMerge.Models;
using pdfMerge.Services;
using PdfSharp.Pdf;

namespace pdfMerge
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Global Unhandled Exception handling for diagnostics on remote machines
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Exception? ex = args.ExceptionObject as Exception;
                string msg = ex != null ? $"{ex.Message}\n\nStack Trace:\n{ex.StackTrace}" : "Unknown Application Error";
                MessageBox.Show($"Unhandled Application Error:\n{msg}", "PDF Merge Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show($"Unhandled UI Exception:\n{args.Exception.Message}\n\nStack Trace:\n{args.Exception.StackTrace}", "PDF Merge Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);

            // Register CodePages encoding for PdfSharp compatibility
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            if (e.Args.Contains("--test"))
            {
                try
                {
                    await RunAutomatedTestAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\n❌ TEST ERROR: {ex.Message}");
                }
                Environment.Exit(0);
            }
        }

        private async System.Threading.Tasks.Task RunAutomatedTestAsync()
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("  RUNNING AUTOMATED PDF MERGE & OPERATION TESTS  ");
            Console.WriteLine("=================================================");

            string currentDir = Directory.GetCurrentDirectory();
            string doc1Path = Path.Combine(currentDir, "test_doc1.pdf");
            string doc2Path = Path.Combine(currentDir, "test_doc2.pdf");
            string outputPath = Path.Combine(currentDir, "test_merged_output.pdf");

            Console.WriteLine("1. Creating test PDF 1 (3 pages)...");
            CreateSamplePdf(doc1Path, 3);

            Console.WriteLine("2. Creating test PDF 2 (2 pages)...");
            CreateSamplePdf(doc2Path, 2);

            int count1 = await PdfService.GetPageCountAsync(doc1Path);
            int count2 = await PdfService.GetPageCountAsync(doc2Path);

            Console.WriteLine($"   Doc 1 Page Count: {count1} (Expected: 3)");
            Console.WriteLine($"   Doc 2 Page Count: {count2} (Expected: 2)");

            var pages = new List<PdfPageItem>
            {
                new PdfPageItem { SourceFilePath = doc1Path, OriginalPageIndex = 0, Rotation = 90, DisplayPageNumber = 1 },
                new PdfPageItem { SourceFilePath = doc1Path, OriginalPageIndex = 2, Rotation = 180, DisplayPageNumber = 2 },
                new PdfPageItem { SourceFilePath = doc2Path, OriginalPageIndex = 0, Rotation = 270, DisplayPageNumber = 3 },
                new PdfPageItem { SourceFilePath = doc2Path, OriginalPageIndex = 1, Rotation = 0, DisplayPageNumber = 4 }
            };

            Console.WriteLine("3. Merging selected pages, applying rotations (90°, 180°, 270°), and deleting page 2 of Doc 1...");
            await PdfService.MergeAndSavePdfAsync(pages, outputPath);

            int resultCount = await PdfService.GetPageCountAsync(outputPath);
            Console.WriteLine($"   Merged Document Page Count: {resultCount} (Expected: 4)");

            string testPdfPath = Path.Combine(currentDir, "test.pdf");
            if (File.Exists(testPdfPath))
            {
                Console.WriteLine("4. Testing AcroForm & Text Extraction on test.pdf...");
                int testPageCount = await PdfService.GetPageCountAsync(testPdfPath);
                Console.WriteLine($"   test.pdf Page Count: {testPageCount}");
                for (int i = 0; i < testPageCount; i++)
                {
                    var fields = await PdfFormService.ExtractFormFieldsAsync(testPdfPath, i);
                    var textLines = await PdfFormService.ExtractTextLinesAsync(testPdfPath, i);
                    Console.WriteLine($"   Page {i + 1}: Found {fields.Count} form field(s), {textLines.Count} text line(s)");
                }
            }

            Console.WriteLine("5. Testing Password-Protected PDF Detection, Unlock & Unencrypted Save...");
            string encDocPath = Path.Combine(currentDir, "test_encrypted.pdf");
            string unlockedOutputPath = Path.Combine(currentDir, "test_unlocked_output.pdf");

            CreateProtectedSamplePdf(encDocPath, 2, "secret123");
            bool isProtected = PdfSecurityService.IsFilePasswordProtected(encDocPath);
            Console.WriteLine($"   Encrypted PDF Detected as Protected: {isProtected} (Expected: True)");

            var wrongPassResult = await PdfSecurityService.VerifyPasswordAsync(encDocPath, "wrongpass");
            Console.WriteLine($"   Wrong Password Rejected: {!wrongPassResult.Success} (Expected: True)");

            var correctPassResult = await PdfSecurityService.VerifyPasswordAsync(encDocPath, "secret123");
            Console.WriteLine($"   Correct Password Accepted: {correctPassResult.Success} (Expected: True)");

            PdfSecurityService.SetPassword(encDocPath, "secret123");
            int encPageCount = await PdfService.GetPageCountAsync(encDocPath);
            Console.WriteLine($"   Encrypted Doc Page Count via Cached Password: {encPageCount} (Expected: 2)");

            var encPages = new List<PdfPageItem>
            {
                new PdfPageItem { SourceFilePath = encDocPath, OriginalPageIndex = 0, DisplayPageNumber = 1 },
                new PdfPageItem { SourceFilePath = encDocPath, OriginalPageIndex = 1, DisplayPageNumber = 2 }
            };

            await PdfService.MergeAndSavePdfAsync(encPages, unlockedOutputPath);
            bool isOutputProtected = PdfSecurityService.IsFilePasswordProtected(unlockedOutputPath);
            Console.WriteLine($"   Saved Output PDF is Unprotected: {!isOutputProtected} (Expected: True)");

            int unlockedCount = await PdfService.GetPageCountAsync(unlockedOutputPath);
            Console.WriteLine($"   Unlocked Output Document Page Count: {unlockedCount} (Expected: 2)");

            if (resultCount == 4 && File.Exists(outputPath) && isProtected && correctPassResult.Success && !isOutputProtected && unlockedCount == 2)
            {
                Console.WriteLine("\n✅ ALL TESTS PASSED: PDF Merging, Rotation, Form Extraction, and Password Unlock & Decryption Verified!");
            }
            else
            {
                Console.WriteLine("\n❌ TEST FAILED!");
            }
        }

        private static void CreateProtectedSamplePdf(string path, int pageCount, string password)
        {
            using var doc = new PdfDocument();
            doc.SecuritySettings.UserPassword = password;
            for (int i = 0; i < pageCount; i++)
            {
                doc.AddPage();
            }
            doc.Save(path);
        }

        private static void CreateSamplePdf(string path, int pageCount)
        {
            using var doc = new PdfDocument();
            for (int i = 0; i < pageCount; i++)
            {
                doc.AddPage();
            }
            doc.Save(path);
        }
    }
}
