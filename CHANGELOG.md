# Changelog

## 2.1.1 — 2026-09-14

- **Extracted app trees are now in Sony layout.** The "original app tree" option merges param.json, icon0.png, pic0.png and the playgo files from the CNT into `sce_sys/`, so the output folder can be used directly as a source to build an FPKG again (CLI: `pkg-extract` does the same; `--no-sce-sys` skips it, `--cnt` still exports the raw CNT tables to `cnt/`).
- Unhandled exceptions on the UI thread are logged to `error.log` and shown in the build log instead of terminating the app; unobserved task exceptions are logged.
- Verified on a 36 GB / 166,707-file package (ASTRO BOT): info 0.16 s, listing 3.8 s, extraction ~200 MB/s.
- The default preset is now simply called **Standard** ("Level 4 · recommended"); the explanation of its equivalence with the old Kraken 7 moved to the detail text.
- "Open output folder" / "Open with external app" now use the OS command (Finder `open`, Explorer, `xdg-open`) first — fixes the button doing nothing on macOS.
- The extract-mode output folder follows the newly opened package again when it was only the previous package's automatic "<name>-extract" suggestion.

## 2.1.0 — 2026-09-13

Updated to the latest **LibProsperoPkg** (from `fpkg-gui 0.6.2`) and exposed its new capabilities.

### Library (LibProsperoPkg)
- Faster large-package builds through overlapping reads, compression and writes, with fewer intermediate copies.
- Block deduplication and an in-build compression cache.
- Improved built-in Kraken compression, especially at levels 8–9.
- PFS v2 / v3 selection, configurable compression block size and automatic shuffle selection for PFS v3.
- Automatic fallback to the built-in Kraken encoder when Oodle Reduced is unavailable.
- Improved progress reporting (byte throughput), reduced log verbosity.
- File-handling and build-cancellation fixes; existing packages are preserved if a rebuild fails.

### Measured on a 21.3 GB real game (macOS, Apple Silicon, built-in Kraken)
| Library | Time | Package |
|---|---|---|
| LibProsperoPkg shipped with 2.0.0, "Kraken 7" (= Normal encoder) | 3 min 40 s | 9.03 GiB |
| LibProsperoPkg shipped with 2.1.0, **Standard (Kraken 4, default)** | **2 min 13 s** | **8.86 GiB** (−1.9 %) |
| LibProsperoPkg shipped with 2.1.0, Smallest (Kraken 7 Optimal) | 12 min 49 s | 8.54 GiB (−5.5 %) |

Side by side on the same 400 MB set the 2.0.0 engine produced identical packages at levels 4 and 7 (its "level 7" was the Normal encoder) and the 2.1.0 engine at level 4 reproduces that result at the same speed, so **Standard** keeps the old size and speed while **Smallest** is a genuinely new, slower Optimal mode.

### App / CLI
- New advanced options: **PFS compression format (v2 / v3)**, **Kraken block size (128–256 KiB)**, **pre-compression shuffle pattern**, **automatic shuffle analysis**, **skip PFS input check** (expert), **physical layout optimisation**.
- **Presets re-mapped to the new encoder.** The 2.0.0 engine used the same Normal encoder for levels 4–7 (identical output at level 4 and 7); in 2.1.0 level 4 reproduces that result at the same speed while level 7 is a new, ~5–6× slower *Optimal* mode. The default preset is therefore **Standard = Kraken 4** (the old "Kraken 7" size and speed, plus dedup/layout gains; CLI id `balanced`, aliases `sony` / `standard`); **Smallest = Kraken 7 Optimal** (~3 % smaller, 5–6× slower) stays available.
- New **Maximum** preset (Kraken 9 + PFS v3 + shuffle analysis) next to Fast / Standard / Smallest. Per the library author: the PFS v3 shuffle mechanism optimises texture compression, is very time-consuming and only fully effective at level 9, and PFS v3 packages need PS5 firmware 7.00 or newer — the app shows these warnings in the advanced options and the build log.
- Build panel shows the live **throughput** (MiB/s) next to the ETA; progress model covers the new planning and layout phases.
- Smoother progress on large games: the inner-compression phase is now estimated from the library’s per-file lines (byte-weighted), so the bar moves every few seconds instead of once per multi-GB file; phase weights re-tuned from a 21 GB real-game build.
- New `PSVIETHOA_SCROLL=<pixels>` dev hook for documentation screenshots.
- CLI: `--pfs`, `--block-size`, `--shuffle`, `--shuffle-analysis`, `--shuffle-prediction-level`, `--skip-pfs-input-check`, `--no-layout-optimization`, `--source-mode`, `--project` (GP5).
- New **Extract PKG** mode (Build | Extract mode bar under the header): inspect a `.pkg` (FIH / CNT headers, region map, every `param.json` field, icon, sce_sys entries) and list the files of the inner PPR-PFS image; filter, tick files/folders and extract a selection or the whole package with progress and cancel; export `sce_sys` / SI entries; optional SHA-256. PLAINTEXT_NOAUTH packages are decoded in place in 4 MiB chunks (LRU cache, no temp image); Native (AES-XTS) packages are decrypted to the temp folder first; retail packages are information-only. CLI: `pkg-info`, `pkg-list`, `pkg-extract` (`--include` globs, `--cnt`, `--threads`). Dev hooks `PSVIETHOA_MODE=extract`, `PSVIETHOA_EXTRACT_PKG=<pkg>`, `PSVIETHOA_EXTRACT_PREVIEW=<path or file name inside the package>`, `PSVIETHOA_AUTOEXTRACT=1`.
- **GP5 project as a third source type** (`.gp5`, mirroring fpkg-gui's Folder | GP5 selector): ".gp5 project" button in step 1 and drag & drop; metadata, size and validation are read through the project (Normal or Flat layout, `global_exclude` / `dir_exclude` / `file_exclude` masks, relative paths resolved from the `.gp5` folder); the project's passcode pre-fills the passcode field; new advanced **Source reading mode** (automatic top-level `.gp5` / folder only) for folder sources; `fpkg-cli inspect` and `build --source x.gp5`.
- New PSVIETHOA logo (`docs/logo.svg`): app icon on macOS / Windows, window header and README banner are generated from it (`scripts/make-icons.py`, `scripts/make-banner.py`).
- Credits shown in the header, footer, help window and CLI.
- Fixed: runtime language switch now updates every label; help window no longer overflows horizontally.

## 2.0.0 — 2026-09-13
- First cross-platform release (macOS Apple Silicon / Intel, Windows x64) — C# / .NET 10 + Avalonia.
- Bilingual UI (Vietnamese / English), `.exfat` disk-image sources, speed presets, free-space & junk checks, ETA, verification, `fpkg-cli`.
