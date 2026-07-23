# PDF Merger & Page Manager

A modern Windows desktop application built in C# for viewing, organizing, rotating, deleting, signing, printing, and merging PDF documents and image files.

---

## What the Tool Does

### 📄 1. File Import (PDFs & Images)
- **Drag & Drop**: Drop PDF files or images (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`, `.webp`) directly into the application.
- **Unified File Picker**: The **Add Files** button opens a dialog supporting both PDF documents and image files.
- **File List Management**: Reorder input files or remove specific files before processing.

### ✏️ 2. Interactive Page Signing Wizard (4 Creation Modes)
- **Top-Right Pencil Icon (✏️)**: Open the 2-step signing wizard on any thumbnail card.
- **Step 1: Placement Bounding Box**: Drag a rectangle on the full-page preview specifying the exact placement box.
- **Step 2: 4-Tabbed Signature Creation**:
  - **✏️ Draw**: Ink canvas with stroke thickness bar (1px to 15px), Undo, and Clear.
  - **⌨️ Type**: Type text in English or Hebrew with dynamic handwriting font pills.
  - **📤 Upload**: Drag & drop signature image with auto background transparency.
  - **✔ Symbol**: Stamp selector for Checkmark / V (`✔`), Cross / X (`✖`), Star (`★`), and Approved (`APPROVED`).
- **Saved Signatures Gallery**: Save created signatures to your local library for future reuse.

### 🖼️ 3. High-Resolution Image Export (PNG / JPG)
- Export selected pages to high-resolution `.png` or `.jpg` image files with rotations and signatures preserved.

### 🔍 4. Visual Page Thumbnails
- High-fidelity interactive thumbnail previews for every PDF page and image.
- 4-level zoom slider (`🔍-` / `🔍+`).

### 🖐️ 5. Interactive Page Reordering & Selection
- Drag & drop page movement with dynamic grid reflow and low-opacity ghost card.
- Rubber-band marquee selection box, select all (`✓✓`), deselect (`✕`), and lossless rotation (+90° / -90°).

### 🔄 6. Revert & State Restore
- **Revert All**: Undo all page moves, rotations, and signature additions to restore original source document state.
- **Clear All**: Reset gallery and file lists in one click.

### ⚡ 7. PDF Merging & Export
- Merge configured PDF, image, and signed pages into a single output PDF document.

### 🖨️ 8. Native PDF Printing (With Live Preview Window)
- **🖨️ Print Button**: Located on the top action bar to open an interactive live Print Preview & Settings window.
- **Live Preview & Controls**:
  - **Live Page Canvas**: Flip through rendered pages with `◀ Prev` / `Next ▶` navigation.
  - **Color vs. Black & White / Grayscale**: Toggle full-color printing or grayscale mode with real-time preview updates.
  - **Printer Selection**: Choose any physical or virtual printer queue.
  - **Print Range Selection**: Print all pages, only selected pages (`Selected Pages Only`), or a custom range (e.g. `1-3, 5`).
  - **Two-Sided Printing (Duplex)**: Support for single-sided or two-sided printing (`Flip on Long Edge` / `Flip on Short Edge`).
  - **Copies**: Configure copy count with quick increment buttons.
- **Full Preservation**: Printed output preserves all applied page rotations, image pages, page ordering, and burned signature overlays.
