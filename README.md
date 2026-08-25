# PDF Merger & Page Manager

A modern, high-performance Windows desktop application built in C# (.NET 9 / WPF) for merging, organizing, editing, filling forms, signing, splitting, printing, and exporting PDF documents and images.

---

## Key Features (By Importance)

### ⚡ 1. PDF Merging & Document Assembly
- **Unified Document Creation**: Combine multiple PDF files and images into a single, polished PDF document.
- **Full Fidelity Preservation**: Seamlessly preserves all page rotations, form entries, freehand drawings, highlights, and signatures.
- **Fast Single-Click Export**: Save the compiled document with automatic validation and error recovery.

### 📝 2. Interactive Full-Page Editor
Double-click any page thumbnail (or click the **Edit** icon) to open the full-screen interactive page editor:
- **Form Field Filling & Text Editing**:
  - Automatically detects AcroForm fields (text fields, checkboxes, combo boxes) and underlying text layout.
  - Type directly into fields; multiline fields automatically expand on <kbd>Enter</kbd>.
  - High-visibility badge indicators for checked boxes.
  - Global text size controls (**Auto**, **Small**, **Medium**, **Large**).
- **Dual-Mode & Rectangle Highlighting**:
  - **Text Highlighting**: Snaps directly to PDF text line geometry with zero spill into margins.
  - **Freehand Ribbon**: Smooth highlighter drawing across whitespace and margins.
  - **Rectangle Highlight**: Drag a crosshair to highlight clean rectangular areas.
  - Adjustable highlighter width and one-click undo.
- **Draw Pen**:
  - Smooth freehand ink drawing in crisp black with customizable pixel thickness and undo.
- **Precision Eraser**:
  - Circular follower cursor that erases strokes, highlights, and annotations under the brush without wiping unaffected drawings.
- **View & Navigation**:
  - Multi-level zoom, **Fit Width**, and **Fit Page** with full multi-level **Undo** history.

### ✍️ 3. Document Signing & Signature Library
- **Direct Canvas Placement**: Select the Signature tool, drag a placement box on the page, and place your signature directly.
- **4 Signature Creation Modes**:
  - **✏️ Draw**: Smooth ink canvas with stroke thickness slider, clear, and undo.
  - **⌨️ Type**: Type your name in English or Hebrew with styled handwriting fonts.
  - **📤 Upload**: Import signature image files with automatic background transparency removal.
  - **✔ Symbol**: Preset stamps for Checkmark (`✔`), Cross (`✖`), Star (`★`), and Approved (`APPROVED`).
- **Saved Signatures Gallery**: Save frequently used signatures to your local library for instant one-click reuse.
- **Signature Manipulation**: Click any placed signature to move, resize (preserving aspect ratio), or delete directly on the canvas.

### 🖐️ 4. Visual Page Organizer & Layout Manager
- **Interactive Thumbnails**: High-resolution live thumbnails with real-time updates reflecting all edits and signatures.
- **Multi-Level Zoom**: 4-level zoom slider (`🔍-` / `🔍+`) to switch between overview grid and detailed page inspection.
- **Drag & Drop Page Reordering**: Drag pages to reorder with live grid reflow and visual drag ghost card.
- **Multi-Page Selection**:
  - Click, <kbd>Ctrl</kbd>+Click, and <kbd>Shift</kbd>+Click range selection.
  - Rubber-band marquee selection box on the workspace canvas.
  - Quick action buttons: Select All (`✓✓`), Invert Selection, and Clear Selection (`✕`).
- **Page Transformations & Cleanup**:
  - Lossless rotation (+90° / -90° / 180°).
  - Delete individual pages or selected batches.
  - **Revert All**: Restore the workspace back to original imported files.

### ✂️ 5. PDF Splitting & Batch Extraction
- **Flexible Custom Ranges**: Define custom extraction ranges (e.g. `1-3, 5, 7-10`) with optional custom output part labels.
- **One-Click Split Presets**:
  - **Split Into Single Pages**: Extract every page into its own individual PDF.
  - **Split Every N Pages**: Chunk document into fixed page intervals.
  - **Split Selected Pages Only**: Extract only currently selected thumbnail pages.
- **Batch Output Configurator**: Preview filenames, destination folders, and page counts before saving.

### 🖨️ 6. Native PDF Printing & Live Print Preview
- **Interactive Print Preview Window**: Flip through rendered pages with live `◀ Prev` / `Next ▶` navigation.
- **Color & Grayscale Toggles**: Switch between full-color printing and grayscale mode with immediate visual preview.
- **Flexible Print Range**: Print all pages, only selected pages, or a custom page range (e.g. `1-3, 5`).
- **Two-Sided Printing (Duplex)**: Full support for single-sided or double-sided printing (Flip on Long Edge / Flip on Short Edge).
- **Copies & Printer Selection**: Target any local or network printer queue with quick copy count controls.

### 🖼️ 7. High-Resolution Image Export
- **Multi-Format Export**: Export pages to high-resolution `.png` or `.jpg` image formats.
- **Preserved Annotations**: Exported images include all page rotations, text entries, drawings, and signatures.

### 📂 8. Universal File Import
- **Drag & Drop**: Drag PDF files or images (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`, `.webp`) directly into the window.
- **Unified File Picker**: Single **Add Files** button supporting both PDF files and image formats simultaneously.
- **Source File List**: Reorder or remove source documents from the queue at any time.

---

## System Requirements & Installation

- **Operating System**: Windows 10 (Build 19041+) or Windows 11 (64-bit).
- **Runtime**: Self-contained executable (no separate .NET runtime installation required).
- **Installation Options**:
  - **Setup Installer**: `Output/PDFMerge_Setup.exe` (installs with Start Menu & Desktop shortcuts).
  - **Portable Executable**: `publish/pdfMerge.exe` (run directly without installation).
