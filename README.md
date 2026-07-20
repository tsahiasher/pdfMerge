# PDF Merger & Page Manager

A modern Windows desktop application built in C# for viewing, organizing, rotating, deleting, and merging PDF documents.

---

## What the Tool Does

### 📄 1. PDF File Import
- **Drag & Drop**: Easily drop one or multiple PDF files directly into the application.
- **File List Management**: Reorder input files or remove specific files before processing.
- **File Information**: View file size, total page count, and status badges for loaded documents.

### 🔍 2. Visual Page Thumbnails
- **High-Fidelity Previews**: Displays interactive thumbnail previews for every page across all imported documents.
- **Live Sequence Tracking**: Shows exact output page numbers as pages are reordered.

### 🖐️ 3. Interactive Page Reordering
- **Drag & Drop Page Movement**: Pick up any page and drop it at a specific position in the document.
- **Visual Insertion Marker**: A glowing vertical insertion line indicates the exact drop position before releasing the mouse button.
- **Contiguous Multi-Page Move**: Move groups of selected pages together as a single block while strictly preserving their internal sequence.
- **Edge Auto-Scrolling**: Dragging near the top or bottom edge of the gallery automatically scrolls the view for long documents.

### 🎯 4. Flexible Selection Modes
- **Rubber-Band Marquee Selection**: Click and drag a selection rectangle on the background to multi-select multiple pages at once.
- **Keyboard Shortcuts**: Select pages using standard `Ctrl+Click`, `Shift+Click`, or `Ctrl+A`.

### ↻ 5. Lossless Page Rotation
- **Metadata-Only Rotation**: Rotate individual or multi-selected pages Clockwise (+90°) or Counter-Clockwise (-90°).
- **0% Quality Loss**: Modifies only the PDF `/Rotate` dictionary entries without re-compressing or rasterizing text, vector graphics, fonts, or images.

### 🗑️ 6. Page Filtering & Deletion
- Delete individual pages or multi-selected page groups with a single click.

### ⚡ 7. PDF Merging & Export
- Combines all configured pages in your custom order and rotation angles into a single output PDF document.
