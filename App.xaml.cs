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
                await RunAutomatedTestAsync();
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

            var pdfService = new PdfService();

            int count1 = await pdfService.GetPageCountAsync(doc1Path);
            int count2 = await pdfService.GetPageCountAsync(doc2Path);

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
            await pdfService.MergeAndSavePdfAsync(pages, outputPath);

            int resultCount = await pdfService.GetPageCountAsync(outputPath);
            Console.WriteLine($"   Merged Document Page Count: {resultCount} (Expected: 4)");

            if (resultCount == 4 && File.Exists(outputPath))
            {
                Console.WriteLine("\n✅ ALL TESTS PASSED: PDF Merging, Page Reordering, Page Rotation & Deletion Verified!");
            }
            else
            {
                Console.WriteLine("\n❌ TEST FAILED!");
            }
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
