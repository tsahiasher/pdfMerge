# PDF Merge & Page Manager — Bug Report

This document details all bugs, logic defects, memory leaks, UI/UX issues, and edge cases discovered during a comprehensive codebase review of the **pdfMerge** project.

---

## 📌 Summary Table

| ID | Severity | Component | Issue Description | Location |
|---|---|---|---|---|
| **BUG-01** | 🔴 Critical | Application Lifecycle | Forceful process kill (`Process.Kill()`) on window close | `MainWindow.xaml.cs:L77` |
| **BUG-02** | 🔴 Critical | Signature / Watermark | Unvalidated Upload tab stamps UI instruction box onto PDF | `SignatureWindow.xaml.cs:L646-647, L678` |
| **BUG-03** | 🔴 Critical | Signature Placement | Distorted / shifted signature coordinates on letterboxed previews | `SignatureWindow.xaml.cs:L155-169` |
| **BUG-04** | 🔴 Critical | PDF Image Importer | `MemoryStream` lifecycle / premature GC disposal in fallback image loader | `PdfService.cs:L960-967` |
| **BUG-05** | 🟠 High | Printing | `LocalPrintServer` disposed before `PrintDocument` executes | `PrintPreviewWindow.xaml.cs:L439-441` |
| **BUG-06** | 🟠 High | Split Feature | Duplicate split part labels silently overwrite output files | `SplitWindow.xaml.cs:L323-328` |
| **BUG-07** | 🟠 High | State Restoration | "Revert All" loses document color coding & file association | `MainWindow.xaml.cs:L459-490` |
| **BUG-08** | 🟡 Medium | PDF Thumbnail Rendering | No fallback renderer when WinRT `Windows.Data.Pdf` fails | `PdfService.cs:L171-175` |
| **BUG-09** | 🟡 Medium | Multithreading / Concurrency | `SemaphoreSlim` lifecycle race condition in thumbnail loading | `MainWindow.xaml.cs:L301-337` |
| **BUG-10** | 🟡 Medium | UI Drag & Drop | `DragOver` collection modification causes layout jitter & redraw storm | `MainWindow.xaml.cs:L812-824` |
| **BUG-11** | 🟡 Medium | Page Selection | Dropping a single dragged page unconditionally deselects it | `MainWindow.xaml.cs:L759-762` |
| **BUG-12** | 🟡 Medium | Bookmarks | Overwriting bookmark mapping when duplicate pages exist | `PdfService.cs:L254, L279` |
| **BUG-13** | 🟡 Medium | UI & Bookmarks | `ChkPreserveBookmarks` enabled even without bookmarks | `MainWindow.xaml.cs:L1233` |
| **BUG-14** | 🟡 Medium | Printing Quality | Low print DPI due to fixed 1600px rasterization | `PdfDocumentPaginator.cs:L37, PageRenderService.cs:L60` |
| **BUG-15** | 🟢 Low | UI Responsiveness | 500ms dialog cooldown drops valid user clicks | `MainWindow.xaml.cs:L39-42` |
| **BUG-16** | 🟢 Low | Page Selection on Drag | Dragging an unselected item adds it to existing multi-selection | `MainWindow.xaml.cs:L720-723` |
| **BUG-17** | 🟢 Low | Split Window Presets | Clicking "Odd Pages" or "Even Pages" appends duplicates | `SplitWindow.xaml.cs:L193-229` |

---

## 🔴 Critical Severity Bugs

### 1. Forceful Process Termination (`Process.Kill()`) on Window Close
- **Location:** `MainWindow.xaml.cs:L77`
- **Code:**
  ```csharp
  protected override void OnClosed(EventArgs e)
  {
      ...
      base.OnClosed(e);
      Application.Current.Shutdown();

      // Bypass Environment.Exit(0) DLL detaching deadlocks and forcefully kill the process.
      System.Diagnostics.Process.GetCurrentProcess().Kill();
  }
  ```
- **Symptom & Impact:** Terminating the process with `Process.GetCurrentProcess().Kill()` terminates the process abruptly without:
  - Running CLR finalizers or flushing unwritten file buffers.
  - Allowing background tasks or worker threads to cleanly dispose unmanaged handles.
  - Emitting exit codes or telemetry.
- **Root Cause:** A workaround was used to avoid deadlocks when WinRT / COM runtime detaches DLLs during shutdown.
- **Recommended Fix:** Cancel all active `CancellationTokenSource` tokens, cleanly dispose background streams, and let WPF's `ShutdownMode="OnMainWindowClose"` shut down the CLR naturally.

---

### 2. Unvalidated Upload Tab Stamps UI Instruction Text onto PDF
- **Location:** `SignatureWindow.xaml.cs:L646-647` & `L678`
- **Code:**
  ```csharp
  private BitmapSource GetCurrentSignatureBitmap()
  {
      ...
      switch (_activeTabIndex)
      {
          case 2: // Upload
              if (_loadedImageSignature != null) return _loadedImageSignature;
              return RenderVisualToBitmap(PnlUploadInstructions, 600, 180);
      ...
  ```
  ```csharp
  private void BtnFinishStep2_Click(object sender, RoutedEventArgs e)
  {
      if (_activeTabIndex == 0 && InkSignCanvas.Strokes.Count == 0 && _loadedImageSignature == null && LstSavedSignatures.SelectedItem == null)
      {
          MessageBox.Show(this, "Please draw a signature...", ...);
          return;
      }
      ...
  ```
- **Symptom & Impact:** If a user selects the **Upload** tab (Tab 2) and clicks "Apply Signature" without selecting or dropping an image file, validation is bypassed because the validation check only inspects `_activeTabIndex == 0`. The application then renders the `PnlUploadInstructions` UI block ("*Drag & drop signature image here or click to choose*") into a bitmap and permanently stamps this text box onto the PDF page.
- **Recommended Fix:** Validate that `_loadedImageSignature != null` when `_activeTabIndex == 2` before allowing the wizard to finish.

---

### 3. Signature Placement Coordinate Distortion on Letterboxed Page Previews
- **Location:** `SignatureWindow.xaml.cs:L155-169`
- **Code:**
  ```csharp
  double imgWidth = ImgPagePreview.ActualWidth;
  double imgHeight = ImgPagePreview.ActualHeight;

  if (imgWidth <= 0) imgWidth = Math.Max(1, GridStep1Canvas.ActualWidth);
  if (imgHeight <= 0) imgHeight = Math.Max(1, GridStep1Canvas.ActualHeight);

  double vx = Math.Max(0, (rectX - imgOffset.X) / imgWidth);
  double vy = Math.Max(0, (rectY - imgOffset.Y) / imgHeight);
  double vw = Math.Min(1.0 - vx, rectW / imgWidth);
  double vh = Math.Min(1.0 - vy, rectH / imgHeight);
  ```
- **Symptom & Impact:** `ImgPagePreview` is configured with `Stretch="Uniform"`. If the window aspect ratio does not perfectly match the PDF page aspect ratio, WPF pillarboxes or letterboxes the image within `ImgPagePreview`. The calculation divides by `ImgPagePreview.ActualWidth` and `ActualHeight` instead of the actual rendered bitmap rectangle inside the image control, causing normalized coordinates `(vx, vy, vw, vh)` to be misaligned, stretched, or offset from where the user drew the box.
- **Recommended Fix:** Compute the inner rendered image rectangle by comparing `ImgPagePreview.Source.Width / Height` with `ImgPagePreview.ActualWidth / ActualHeight` and calculate relative coordinates within the inner uniform aspect bounds.

---

### 4. `MemoryStream` Lifetime / Premature GC Disposal in `CreateXImageFromFile` Fallback
- **Location:** `Services/PdfService.cs:L960-967`
- **Code:**
  ```csharp
  private static XImage CreateXImageFromFile(string filePath)
  {
      try { return XImage.FromFile(filePath); }
      catch (Exception ex)
      {
          using var fileStream = File.OpenRead(filePath);
          var decoder = BitmapDecoder.Create(fileStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
          BitmapFrame frame = decoder.Frames[0];

          var ms = new MemoryStream();
          var encoder = new PngBitmapEncoder();
          encoder.Frames.Add(frame);
          encoder.Save(ms);
          ms.Position = 0;

          return XImage.FromStream(ms);
      }
  }
  ```
- **Symptom & Impact:** In the fallback path for images that `XImage.FromFile` cannot open directly (e.g. WebP, ICO, TIFF), `ms` is created inside the method scope and passed to `XImage.FromStream(ms)`. Because `ms` is not retained by the caller or attached to `outputDocument`, if garbage collection runs before `outputDocument.Save()` finishes writing image streams, stream read errors, corrupted PDF pages, or `ObjectDisposedException` will occur.
- **Recommended Fix:** Cache and track all generated memory streams in a list inside `MergeAndSavePdfAsync` and dispose them only in the `finally` block after `outputDocument.Save()` completes.

---

## 🟠 High Severity Bugs

### 5. `LocalPrintServer` Disposed Before `PrintDocument` Executes
- **Location:** `Views/PrintPreviewWindow.xaml.cs:L439-441`
- **Code:**
  ```csharp
  if (CmbPrinters != null && CmbPrinters.SelectedItem is PrintQueueItem selectedItem && !string.IsNullOrEmpty(selectedItem.FullName))
  {
      using var printServer = new LocalPrintServer();
      printDialog.PrintQueue = printServer.GetPrintQueue(selectedItem.FullName);
  } // printServer is disposed here!

  ...
  printDialog.PrintDocument(paginator, "PDF Merge Print Job");
  ```
- **Symptom & Impact:** The `LocalPrintServer` instance is wrapped in a `using` block and disposed immediately after obtaining the `PrintQueue`. When `printDialog.PrintDocument()` is subsequently called, the underlying native print queue handle may be invalid, throwing `PrintQueueException` or `ObjectDisposedException`.
- **Recommended Fix:** Keep `printServer` in scope during `printDialog.PrintDocument(...)` or assign `PrintQueue` through `new PrintServer().GetPrintQueue(...)` scoped to the method.

---

### 6. Duplicate Split Part Labels Silently Overwrite Output Files
- **Location:** `Views/SplitWindow.xaml.cs:L323-328`
- **Code:**
  ```csharp
  foreach (var part in SplitRanges)
  {
      if (part.Pages.Count == 0) continue;

      string sanitizedLabel = string.Concat(part.Name.Split(Path.GetInvalidFileNameChars())).Trim();
      if (string.IsNullOrWhiteSpace(sanitizedLabel)) sanitizedLabel = $"Part_{count + 1}";

      string targetPath = Path.Combine(folder, $"{baseName}_{sanitizedLabel}.pdf");

      await PdfService.MergeAndSavePdfAsync(part.Pages, targetPath);
      count++;
  }
  ```
- **Symptom & Impact:** If the user specifies multiple parts with the same label (e.g. "Invoice", "Invoice" or "Part 1", "Part 1"), each subsequent part writes to the exact same file path, silently overwriting the previous part. The user receives a message stating "Successfully split PDF into N parts", but only the last overwritten part exists on disk.
- **Recommended Fix:** Track used file names in a `HashSet<string>` and append numeric suffixes (e.g. `Invoice (2).pdf`) if duplicates exist.

---

### 7. "Revert All" Loses Document Color Coding and Visual File Linkage
- **Location:** `MainWindow.xaml.cs:L459-490`
- **Code:**
  ```csharp
  foreach (var snap in _originalPagesSnapshot)
  {
      var restored = new PdfPageItem
      {
          SourceFilePath = snap.SourceFilePath,
          OriginalPageIndex = snap.OriginalPageIndex,
          ...
          // DocumentColorHex is never assigned here!
      };
      ...
      Pages.Add(restored);
  }
  PageReorderService.ReindexSequenceNumbers(Pages);
  UpdateUIState();
  // UpdateDocumentColors() is NEVER called here!
  ```
- **Symptom & Impact:** When clicking "Revert All", restored pages do not receive their original `DocumentColorHex` and `UpdateDocumentColors()` is not called. All page cards in the gallery reset to default blue (`#0EA5E9`), losing the visual multi-file color distinction.
- **Recommended Fix:** Call `UpdateDocumentColors()` inside `BtnRevert_Click` after restoring pages.

---

## 🟡 Medium Severity Bugs

### 8. No Fallback Renderer When WinRT `Windows.Data.Pdf` Fails for Thumbnails
- **Location:** `Services/PdfService.cs:L171-175`
- **Symptom & Impact:** `GetPageCountAsync` has a fallback to PdfSharp when WinRT fails, but `RenderPageThumbnailAsync` does not. In environments without WinRT PDF support (such as Windows Server or corrupted PDF streams), thumbnails fail silently and remain permanently in the loading state.
- **Recommended Fix:** Implement a fallback software rasterizer (such as PDFium) or display a placeholder thumbnail indicating the page index and filename.

---

### 9. Concurrency `SemaphoreSlim` Lifecycle Race in `LoadThumbnailsInBackgroundAsync`
- **Location:** `MainWindow.xaml.cs:L301-337`
- **Code:**
  ```csharp
  using var semaphore = new SemaphoreSlim(3);
  ...
  await Task.WhenAll(tasks);
  ```
- **Symptom & Impact:** If the loading operation is cancelled via token, `Task.WhenAll(tasks)` throws `OperationCanceledException`, causing execution to exit the method and dispose the `SemaphoreSlim`. Background worker threads that were in the process of finishing may call `semaphore.Release()` in their `finally` block, causing `ObjectDisposedException`.
- **Recommended Fix:** Use a class-level, non-disposed `SemaphoreSlim` or ensure all tasks have fully concluded before disposing the semaphore.

---

### 10. `DragOver` Event Modifies Collection on Every Pixel Movement
- **Location:** `MainWindow.xaml.cs:L812-824`
- **Symptom & Impact:** During drag-and-drop page reordering, `LstPages_DragOver` removes and re-inserts items into `Pages` (`ObservableCollection`) continuously as the mouse moves. This triggers `CollectionChanged` events on every mouse move, executing event handlers and forcing full layout re-measurement during dragging, resulting in UI stuttering.
- **Recommended Fix:** Use a visual insertion adorner (drop indicator line) during `DragOver` and apply the collection reordering once upon `Drop`.

---

### 11. Dropping a Single Dragged Page Unconditionally Deselects It
- **Location:** `MainWindow.xaml.cs:L759-762`
- **Code:**
  ```csharp
  if (draggedItems.Count == 1)
  {
      draggedItems[0].IsSelected = false;
  }
  ```
- **Symptom & Impact:** If a user selects a single page and drags it to a new location, it is automatically deselected upon drop. However, dragging multiple pages leaves them selected. This behavior is inconsistent.
- **Recommended Fix:** Preserve the selection state consistently regardless of whether one or multiple pages were dragged.

---

### 12. Bookmark Page Mapping Collision When Pages Are Duplicated
- **Location:** `Services/PdfService.cs:L254, L279`
- **Code:**
  ```csharp
  pageMap[(fullSourcePath, item.OriginalPageIndex)] = page;
  ```
- **Symptom & Impact:** If a page from a source PDF is included multiple times (e.g. page duplicated in the output document), the dictionary overwrites the entry. Recreated bookmarks will only point to the last occurrence of that page in the document.
- **Recommended Fix:** Map bookmark destinations to the first output page instance or maintain a list of target page references.

---

### 13. `ChkPreserveBookmarks` Enabled Without Validating Source Bookmarks
- **Location:** `MainWindow.xaml.cs:L1233`
- **Code:**
  ```csharp
  ChkPreserveBookmarks.IsEnabled = hasFiles;
  ```
- **Symptom & Impact:** The checkbox is enabled whenever files exist, but `PdfService.HasBookmarks(...)` is never called. Users are offered the option to preserve bookmarks even when none of the source files contain outlines.
- **Recommended Fix:** Check `PdfService.HasBookmarks(Pages)` asynchronously when files are loaded and update `ChkPreserveBookmarks.IsEnabled` accordingly.

---

### 14. Fixed 1600px Width Limits Print Output Resolution
- **Location:** `Models/PdfDocumentPaginator.cs:L37` & `Services/PageRenderService.cs:L60`
- **Symptom & Impact:** Printing renders pages through a fixed 1600px rasterized bitmap. On 300 DPI or 600 DPI physical printers, standard Letter/A4 pages require 2550x3300 to 5100x6600 pixels. Printing at 1600px results in pixelated, blurry text and lines.
- **Recommended Fix:** Calculate rendering resolution dynamically based on `printDialog.PrintQueue.DefaultPrintTicket.PageResolution` or render at 300+ DPI.

---

## 🟢 Low Severity Bugs

### 15. 500ms Dialog Cooldown Drops Legitimate Clicks
- **Location:** `MainWindow.xaml.cs:L39-42`
- **Symptom & Impact:** `IsDialogCooldownActive()` drops all mouse clicks within 500ms of any dialog closing. This makes the interface feel unresponsive if a user immediately clicks an action button after closing a dialog.
- **Recommended Fix:** Lower cooldown to 100ms or remove it in favor of proper event handling.

---

### 16. Dragging Unselected Item Adds to Multi-Selection
- **Location:** `MainWindow.xaml.cs:L720-723`
- **Symptom & Impact:** If a user has pages 1, 2, and 3 selected and drags page 8, page 8 is added to the selection (`clickedPage.IsSelected = true`) and all 4 pages are dragged together. Standard Windows behavior is to drag only the clicked item if it was not part of the active selection.
- **Recommended Fix:** If the clicked item is not selected, clear previous selection and select only the clicked item before starting the drag.

---

### 17. Quick Split Preset Buttons Append Duplicate Ranges
- **Location:** `Views/SplitWindow.xaml.cs:L193-229`
- **Symptom & Impact:** Clicking "Odd Pages" or "Even Pages" multiple times appends duplicate ranges to the list without checking if they already exist or clearing prior ranges.
- **Recommended Fix:** Check if the range already exists or offer an option to clear existing ranges before applying presets.
