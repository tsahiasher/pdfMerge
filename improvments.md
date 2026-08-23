# PDF Merge & Page Manager — Suggested Improvements

This document outlines key architectural, performance, UX/UI, feature, and code quality improvements recommended for the **pdfMerge** application.

---

## 🏗️ 1. Architecture & Design Patterns

### 1.1 Complete MVVM (Model-View-ViewModel) Refactoring
- **Current State:** The application is built using code-behind across `MainWindow.xaml.cs`, `PrintPreviewWindow.xaml.cs`, `SignatureWindow.xaml.cs`, and `SplitWindow.xaml.cs`. These files combine UI event handling, business logic, asynchronous task coordination, and PDF manipulation directly.
- **Improvement:**
  - Introduce ViewModels (`MainViewModel`, `PrintPreviewViewModel`, `SignatureViewModel`, `SplitViewModel`) extending `ObservableObject`.
  - Use data-binding with commands (`ICommand` / `RelayCommand` / `AsyncRelayCommand`) for all button and keyboard interactions.
  - Implement a `DialogService` / `NavigationService` interface to decouple view instantiation and modal dialog popups from business logic.

### 1.2 Dependency Injection & Service Abstraction
- **Current State:** Services like `PdfService`, `PageRenderService`, `PageReorderService`, and `PageSelectionService` are static utility classes.
- **Improvement:**
  - Define interfaces: `IPdfService`, `IPageRenderService`, `IPrintService`, `IFileService`, `ISettingsService`.
  - Register services in a standard `Microsoft.Extensions.DependencyInjection` container (`IServiceProvider`) initialized during `App.OnStartup`.
  - Enables modularity and unit test coverage with mock services.

---

## ⚡ 2. Performance & Memory Optimizations

### 2.1 UI Virtualization with `VirtualizingWrapPanel`
- **Current State:** The page gallery uses a standard WPF `WrapPanel` inside a `ListBox`. Every loaded page creates visual tree elements and holds bitmap references simultaneously. Loading a 300+ page document creates hundreds of visual elements, leading to high memory usage and scrolling slowdowns.
- **Improvement:**
  - Replace `WrapPanel` with a virtualizing wrap panel (such as `WpfToolkit.Controls.VirtualizingWrapPanel`).
  - Only visible cards on screen are materialized in the visual tree, allowing the application to handle 1,000+ pages with low memory consumption and 60 FPS scrolling.

### 2.2 LRU Memory Cache & Thumbnail Offloading
- **Current State:** `PdfPageItem.OriginalThumbnail` keeps all decoded `BitmapSource` thumbnails in RAM permanently until the page or file is removed.
- **Improvement:**
  - Implement an in-memory LRU (Least Recently Used) cache with configurable memory limits (e.g. 150 MB).
  - Offload non-visible thumbnails to temporary disk cache or regenerate on demand using WinRT/PDFium rasterization.
  - Use `WeakReference<BitmapSource>` for cached thumbnails to allow the garbage collector to reclaim memory under pressure.

### 2.3 Insertion Adorner for Smooth Drag-and-Drop Reordering
- **Current State:** `LstPages_DragOver` removes and re-inserts items into the `ObservableCollection` during active mouse movement, causing continuous collection notifications and layout thrashing.
- **Improvement:**
  - Maintain the collection unaltered during dragging.
  - Render an adorner line (insertion marker) between cards to indicate target drop position.
  - Execute the actual `Move` or reorder operation only once inside `LstPages_Drop`.

---

## 🎨 3. UX, UI & Feature Enhancements

### 3.1 Undo / Redo Command History Stack
- **Current State:** The application only provides a single "Revert All" button that restores the initial state of the files.
- **Improvement:**
  - Implement the Command Pattern with an Undo/Redo stack (`Ctrl+Z` / `Ctrl+Y`).
  - Track atomic actions: `RotatePagesCommand`, `DeletePagesCommand`, `ReorderPagesCommand`, `ApplySignatureCommand`, `InsertPagesCommand`.
  - Provide an undo/redo toolbar button with action tooltips (e.g., "Undo Rotate Page 3").

### 3.2 Full-Screen Page Zoom / Double-Click Page Inspector
- **Current State:** Thumbnails can be resized between 4 zoom levels (160px - 320px), but there is no full-screen high-resolution page viewer.
- **Improvement:**
  - Double-clicking any page thumbnail opens a full-resolution inspection modal with zoom, pan, rotation, and side-by-side page comparison.

### 3.3 Enhanced Digital & Ink Signatures
- **Current State:** Signature window supports basic black ink, text handwriting fonts, uploaded images, and 4 static symbols.
- **Improvement:**
  - **Ink Color Selection:** Add color choices (Blue, Navy, Black, Red) and highlighter mode.
  - **Signature Resizing & Repositioning:** Allow clicking placed signatures directly on the thumbnail canvas to drag, scale, or remove them without re-opening the wizard.
  - **Transparent Background Thresholding:** When uploading scanned image signatures (JPEG/PNG with white or shaded paper backgrounds), provide a background removal slider with automatic alpha thresholding.
  - **Date & Title Stamp:** Provide an option to attach an automatic date stamp (e.g., "Signed on 2026-08-23") below the signature.

### 3.4 PDF Security & Optimization Tools
- **Current State:** Cannot load password-protected PDFs or apply security to output PDFs.
- **Improvement:**
  - **Password Prompt:** Display a password prompt dialog when importing encrypted PDFs.
  - **Document Encryption:** Add an option in the Save dialog to set user/owner passwords and permissions (prevent printing, copying, or modifying).
  - **PDF Compression & Downsampling:** Provide export options to downsample high-DPI images (e.g. 150 DPI for email, 300 DPI for print) and strip unused metadata to reduce output file size.

### 3.5 Keyboard Shortcuts & Accessibility
- **Current State:** Limited keyboard shortcut support.
- **Improvement:**
  - Add standard shortcuts:
    - `Ctrl + O`: Add Files
    - `Ctrl + S`: Merge & Save All
    - `Ctrl + Shift + S`: Save Selected
    - `Ctrl + P`: Print
    - `Ctrl + A`: Select All
    - `Delete` / `Backspace`: Delete Selected Pages
    - `R`: Rotate Selected Clockwise
    - `Shift + R`: Rotate Selected Counter-Clockwise
    - `Ctrl + Z`: Undo
    - `Ctrl + Y`: Redo
  - Full keyboard focus navigation and screen-reader accessibility labels (`AutomationProperties.Name`).

### 3.6 Theme Customization (Light / Dark Mode)
- **Current State:** Dark slate theme is hardcoded throughout the styles.
- **Improvement:**
  - Implement dynamic ResourceDictionaries for Theme switching (`ThemeManager.SetTheme(AppTheme.Dark | AppTheme.Light | AppTheme.System)`).

---

## 🛡️ 4. Code Quality, Robustness & Security

### 4.1 Atomic File Saving
- **Current State:** Direct saving with `outputDocument.Save(outputPath)` can corrupt target files if an unexpected error occurs midway or if saving over an open file.
- **Improvement:**
  - Write to a temporary file in the same directory (`$"{outputPath}.tmp.{Guid.NewGuid()}"`), flush, and execute an atomic `File.Move(..., overwrite: true)` or `File.Replace(...)`.

### 4.2 Cross-Platform Rendering Engine Fallback (PDFium)
- **Current State:** Thumbnail rendering relies exclusively on Windows 10/11 WinRT `Windows.Data.Pdf`. If running on environments without WinRT PDF support or encountering non-standard PDFs, rendering fails.
- **Improvement:**
  - Embed or package Google's PDFium engine (`PdfiumViewer` / `bblanchon.PDFium.Win32`) as a primary or fallback rendering engine for cross-version consistency.

### 4.3 Structured Logging & Diagnostics
- **Current State:** Diagnostic messages are output via `Debug.WriteLine` or console output in test mode.
- **Improvement:**
  - Integrate structured logging using `Microsoft.Extensions.Logging` or `Serilog`.
  - Write rolling log files to `%LocalAppData%\pdfMerge\Logs\app-YYYYMMDD.log` for customer support diagnostics and error tracing.

### 4.4 Automated Unit & Integration Test Coverage
- **Current State:** Tests are limited to a `--test` command line switch verifying simple 2-document merge.
- **Improvement:**
  - Create dedicated test projects (`pdfMerge.Tests`):
    - **Unit Tests:** Range parser tests (`ParseCustomPageRange`), rotation normalization, bookmark reconstruction algorithm, layout coordinate math.
    - **Integration Tests:** Corrupted PDF handling, password-protected PDF handling, image format conversions, memory leak tests under high page counts.
