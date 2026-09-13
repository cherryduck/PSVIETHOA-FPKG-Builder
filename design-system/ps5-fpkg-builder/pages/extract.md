# Extract Page Overrides

> **PROJECT:** PS5 FPKG Builder
> **Generated:** 2026-09-13 22:18:53
> **Page Type:** Tool workspace (package extraction: browse + bulk select + preview + long-running task)

> ⚠️ **IMPORTANT:** Rules in this file **override** the Master file (`design-system/MASTER.md`).
> Only deviations from the Master are documented here. For all other rules, refer to the Master.

---

## Page-Specific Rules

### Layout
- Same two-column workspace as the build mode: left = numbered step cards ("Step 1 · Source package & output folder", "Step 2 · Content to extract"), right = status card + tabbed "Files & preview | Log" area.
- Density 7/10: 12–16 px inner gaps, 8 px between related controls; the file list fills the remaining height (star-sized grid row), no nested scroll viewers.
- Mode switch in the window header (segmented "Build PKG | Extract PKG") reuses the SegmentRadio control theme of the language toggle.

### Bulk actions
- Checkbox column + one action bar (filter, Select all / none / Invert, "Selected X / N files (showing M)" + selected bytes). Primary actions live in the status card only (Extract · Cancel · Open output folder); never per-row action buttons.
- Long paths: `TextTrimming="CharacterEllipsis"` + full path in ToolTip; mono font for paths; sizes right-aligned in a muted column.

### Feedback
- Long tasks: determinate progress (bytes done / total), current file, elapsed, Cancel is the only enabled action while running; inputs disabled during extraction.
- Completion: success banner with the output folder + "Open output folder"; failure: error banner with the message. Package loading: thin indeterminate progress in the card.
- Sub-line under the headline states the reading strategy ("Reading directly from the PKG — no temporary files" vs "Decoding the inner image to a temporary file…").

### Empty & edge states
- No package: friendly empty state (icon, "Choose a .pkg to see its contents", drag-and-drop hint).
- No highlighted row: "Select a file to preview".
- Retail / non-debug packages: info banner "cannot be extracted", info still shown.
- Extracting into a non-empty folder → confirmation dialog stating that existing files are overwritten.

### Accessibility
- Icon-only buttons: ToolTip + AutomationProperties.Name. Keyboard: Enter applies the filter, Space toggles a row, Ctrl/⌘+A selects all. Keep the theme focus adorners. Contrast ≥ 4.5:1 in dark and light via token brushes only.

### Performance
- `ListBox` + `VirtualizingStackPanel`, `ObservableCollection` rows, background filtering with 150 ms debounce.
- Preview reads ≤ 256 KiB (text), hex dump ≤ 4 KiB, images decoded to ≤ 512 px width; reads go through the range reader (no full decode for plaintext packages).

### Motion & icons
- Subtle only: the shared 150–200 ms opacity/brush transitions; no slides.
- PathIcon geometries from `Styles/Icons.axaml` (Material Design Icons); no emoji.
