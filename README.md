# PDF Merger & Page Manager

A modern Windows desktop application built in C# for viewing, organizing, rotating, deleting, signing, and merging PDF documents and image files.

---

## What the Tool Does

### 📄 1. File Import (PDFs & Images)
- **Drag & Drop**: Easily drop one or multiple PDF files or images (`.png`, `.jpg`, `.jpeg`, `.bmp`, `.tiff`, `.webp`) directly into the application.
- **Unified File Picker**: The **Add Files** button opens a dialog supporting both PDF documents and image files seamlessly.
- **File List Management**: Reorder input files or remove specific files before processing.
- **File Information**: View file size, page count, and status badges for all loaded files.

### ✏️ 2. Interactive Page Signing Wizard
- **Top-Right Pencil Icon (✏️)**: Every thumbnail card features a pencil sign button to open the 2-step signing wizard.
- **Step 1: Placement Bounding Box**: Drag a rectangle on the full-page preview specifying the exact position and dimensions where the signature should be placed. Redragging replaces the previous box.
- **Step 2: Signature Creation Modes**:
  - **Freehand Drawing Canvas**: Draw your signature with smooth mouse ink drawing.
  - **Load Image Signature**: Import a PNG/JPG signature image file.
  - **Saved Signatures Gallery**: Select from previously saved signatures stored locally in `%AppData%/pdfMerge/Signatures`.
- **Controls & Navigation**:
  - **Back**: Navigation button to return to Step 1.
  - **Undo & Clear**: Undo mouse strokes or reset the canvas.
  - **Save Signature**: Save drawn signature to your local library for future reuse.
  - **Apply Signature**: Permanently burn the signature onto the page at exact coordinates.

### 🖼️ 3. High-Resolution Image Export (PNG / JPG)
- **Export Selected Pages as Images**: Export selected PDF pages or image pages to high-resolution `.png` or `.jpg` image files with rotations and signatures preserved.

### 🔍 4. Visual Page Thumbnails
- **High-Fidelity Previews**: Displays interactive thumbnail previews for every PDF page and imported image (including signed page overlays).
- **Live Sequence Tracking**: Shows exact output page numbers as pages are reordered.
- **4-Level Zoom Slider**: Adjust preview card sizes dynamically (`- [1|2|3|4] +`).

### 🖐️ 5. Interactive Page Reordering & Dynamic Reflow
- **Drag & Drop Page Movement**: Drag any page thumbnail and drop it at a specific position in the document.
- **Actual Thumbnail Drag Ghost**: Displays the actual page image floating under the cursor at low opacity while dragging.
- **Live Grid Reflow**: Surrounding cards shift and reflow dynamically in real time to show where the items will land.
- **Contiguous Multi-Page Move**: Dragging any card within a multi-selection moves all selected pages together as a single block.
- **Edge Auto-Scrolling**: Dragging near the top or bottom edge of the gallery automatically scrolls the view for long documents.

### 🎯 6. Flexible Real-Time Selection Modes
- **Instant Toolbar Button Binding**: Checking or unchecking any CheckBox immediately updates `Delete Selected`, `Save Selected`, and `Export Images` button states.
- **Top-Left Checkbox Selection**: Select or deselect pages using the CheckBox on the top-left of each card.
- **Rubber-Band Marquee Selection**: Click and drag a selection rectangle on the background to multi-select multiple pages at once.
- **Toolbar Actions**: Select All or Deselect All with a single click.

### ↻ 7. Lossless Page Rotation
- **Metadata-Only Rotation**: Rotate individual or multi-selected pages Clockwise (+90°) or Counter-Clockwise (-90°).
- **0% Quality Loss**: Modifies only the PDF `/Rotate` dictionary entries without re-compressing or rasterizing text, vector graphics, fonts, or images.

### 🗑️ 8. Page Filtering & Deletion
- Delete individual pages or multi-selected page groups with a single click.
- **Revert All**: Undo all page reordering, page rotations, signatures, and page deletions to restore original files.

### ⚡ 9. PDF Merging & Export
- **Merge All**: Combines all configured PDF, image, and signed pages into a single output PDF document.
- **Save Selected**: Merge and export only the currently selected pages to a new PDF.
- **High-Resolution Vector Signature Burning**: Burns applied signatures onto the PDF output canvas at exact placement coordinates.
