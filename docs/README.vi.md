# PSVIETHOA FPKG Builder

Ứng dụng **tạo gói FPKG (FIH debug) cho PS5** từ thư mục ứng dụng đã chuẩn bị **hoặc từ ảnh đĩa exFAT (`.exfat`)**, chạy **native trên macOS (Apple Silicon & Intel) và Windows x64**, giao diện **song ngữ Việt / Anh** (chuyển ngay trong header), kèm công cụ dòng lệnh `fpkg-cli`.

> **English summary:** cross-platform (macOS + Windows) PS5 FPKG builder written in C# / .NET 10 + Avalonia on top of LibProsperoPkg 1.2.0. Sources can be an app folder or an exFAT disk image (`.exfat`, mounted read-only on macOS or extracted elsewhere). Bilingual UI (Vietnamese / English), speed presets (default *Smallest* = Sony SDK standard, Kraken level 7), free-space and junk-file checks, ETA, verification, and a `fpkg-cli` for scripting. See `docs/screenshots/` for the UI.

Được viết lại hoàn toàn từ dự án WPF `FpkgBuilderVi` (chỉ chạy Windows) sang **C# / .NET 10 + Avalonia UI 11.3**, giữ nguyên lõi tạo gói **LibProsperoPkg 1.2.0** (Drakmor & SvenGDK) và bổ sung nhiều tối ưu cho việc build các thư mục game nặng (vài chục GB).

![Giao diện tối](docs/screenshots/ui-dark.png)

---

## Tính năng

| Nhóm | Chi tiết |
|---|---|
| Nguồn | Thư mục ứng dụng (chứa `sce_sys`) **hoặc ảnh đĩa exFAT `.exfat`** (volume thuần, MBR hay GPT). Bộ đọc exFAT thuần .NET đọc param.json/icon/kích thước trực tiếp từ ảnh; khi build: **macOS gắn ảnh bằng hdiutil (không sao chép)**, Windows hoặc ảnh có tệp rác thì **giải nén ra thư mục tạm** (tự bỏ qua `.DS_Store`, `._*`…) rồi tự dọn |
| Ngôn ngữ | Tiếng Việt / English, đổi ngay lập tức, lưu lựa chọn; CLI nhận `--lang vi\|en` hoặc biến `FPKG_LANG` |
| Tạo gói | FIH debug (signed byte 0x00), PLAINTEXT_NOAUTH hoặc Native AES-XTS, APP / Homebrew / DLC, PlayGo tự động 1–64 khối, ghi đè SDK 1–11, passcode, bản dựng xác định |
| Nén | Bộ nén **Kraken tích hợp thuần .NET** (chạy trên macOS/Windows/Linux, đa luồng) hoặc **Oodle gốc** qua `libScePubTools.dll` (Windows) — chế độ *Tự động* tự chọn, không bao giờ âm thầm rơi về "không nén" |
| Tốc độ | Preset **Nhanh · Cân bằng · Nhỏ nhất** (mặc định **Nhỏ nhất** = Kraken mức 7, chuẩn Sony Publishing Tools), tuỳ chỉnh mức nén -4..9 và số luồng, chế độ không nén cho test nhanh |
| Trước khi build | Đọc `sce_sys/param.json` + `icon0.png`, quét dung lượng song song, **kiểm tra dung lượng trống** ổ tạm/ổ xuất, **phát hiện & dọn tệp rác** (`.DS_Store`, `Thumbs.db`, `._*`, `__MACOSX`…) để chúng không lọt vào gói |
| Trong khi build | Thanh tiến trình tổng thể theo trọng số giai đoạn, **ước tính thời gian còn lại**, nhật ký ảo hoá 20.000 dòng không giật, hủy an toàn, **chống máy ngủ** (caffeinate / SetThreadExecutionState) |
| Sau khi build | Tự kiểm tra cấu trúc FIH/PFS ngoài, SHA-256 tuỳ chọn (bộ đệm 4 MB), bảng kết quả, sao chép/lưu nhật ký, mở thư mục kết quả |
| Giao diện | Theme tối OLED (mặc định) & sáng, kéo–thả thư mục, thư mục gần đây, kiểm tra lỗi ngay tại trường, phím tắt Ctrl/⌘+B · F5 · Esc · F1 |
| CLI | `fpkg-cli build / inspect / verify / clean-junk / info` — dùng cho build hàng loạt, script, CI |

## Yêu cầu

* **Để chạy bản phát hành:** không cần cài gì thêm (bản self-contained kèm .NET).
* **Để build từ mã nguồn:** [.NET SDK 10](https://dotnet.microsoft.com/download) trở lên. Trên macOS cài qua Homebrew: `brew install dotnet`.
* Oodle gốc (`libScePubTools.dll`) chỉ dùng được trên Windows x64; trên macOS ứng dụng luôn dùng Kraken tích hợp.

## Cấu trúc mã nguồn

```
PSVIETHOA_Fpkg_Builder/
├─ PsViethoa.FpkgBuilder.slnx
├─ Directory.Build.props              # net10.0, phiên bản, GC/PGO
├─ libs/                              # LibProsperoPkg.dll (+xml/pdb), libScePubTools.dll (Windows)
├─ src/
│  ├─ PsViethoa.FpkgBuilder.Core/     # Lõi: engine, validation, quét thư mục, tiến trình, tiện ích
│  │  ├─ Models/                      # BuildRequest, BuildOutcome, LogEntry, SourceMetadata…
│  │  └─ Services/                    # BuildEngine, BuildPreparer, PhaseCatalog, ProgressTracker,
│  │                                  # FolderScanner, JunkFileFinder, DiskSpaceAdvisor, SleepInhibitor…
│  ├─ PsViethoa.FpkgBuilder.App/      # Giao diện Avalonia (MVVM, CommunityToolkit.Mvvm)
│  │  ├─ Styles/                      # Tokens (theme tối/sáng), Icons (SVG path), Controls, ControlThemes
│  │  ├─ ViewModels/MainViewModel.cs
│  │  ├─ Views/                       # MainWindow, HelpWindow, MessageDialog
│  │  └─ Assets/                      # icon, font JetBrains Mono (OFL)
│  └─ PsViethoa.FpkgBuilder.Cli/      # fpkg-cli
├─ tests/PsViethoa.FpkgBuilder.Tests/ # xunit
├─ scripts/                           # publish-macos.sh, publish-windows.sh/.ps1, run-dev.sh, make-icons.py
└─ design-system/ps5-fpkg-builder/    # MASTER.md — hệ thống thiết kế (màu, chữ, khoảng cách)
```

## Build & chạy

```bash
# Khôi phục gói và build toàn bộ
dotnet build PsViethoa.FpkgBuilder.slnx -c Release

# Chạy giao diện (macOS/Linux)
scripts/run-dev.sh
# hoặc
dotnet run --project src/PsViethoa.FpkgBuilder.App -c Debug

# Chạy kiểm thử
dotnet test
```

> macOS + Homebrew: nếu chạy trực tiếp tệp thực thi trong `bin/` báo "You must install .NET", hãy đặt `export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec` (script `run-dev.sh` tự làm việc này). Bản phát hành self-contained không cần bước này.

### Đóng gói phát hành

```bash
scripts/publish-macos.sh              # dist/osx-arm64 & dist/osx-x64: "PSVIETHOA FPKG Builder.app" + fpkg-cli + .zip
scripts/publish-windows.sh            # dist/win-x64: "PSVIETHOA FPKG Builder.exe" (một tệp) + fpkg-cli + libScePubTools.dll
# Trên Windows:
powershell -ExecutionPolicy Bypass -File scripts\publish-windows.ps1
```

Bản macOS được ký ad-hoc; lần đầu mở, chuột phải → **Mở** (Gatekeeper). Bản Windows chưa ký, SmartScreen có thể hỏi — chọn *Run anyway*.

## Dùng dòng lệnh

```bash
fpkg-cli info --lang en                                 # kiểm tra khoá debug, bộ nén mặc định
fpkg-cli inspect "/path/PPSA12345"                      # đọc param.json, dung lượng, tệp rác, dung lượng trống
fpkg-cli inspect "/path/PPSA12345.exfat"                # ảnh exFAT: đọc trực tiếp không cần mount
fpkg-cli build --source "/path/PPSA12345.exfat" --output "/path/out"          # mặc định: Nhỏ nhất (Sony), exfat auto
fpkg-cli build --source "/path/PPSA12345.exfat" --output "/path/out" --exfat extract
fpkg-cli build --source "/path/PPSA12345" --output "/path/out" --preset fast --clean-junk
fpkg-cli build -s SRC -o OUT --backend none             # không nén (nhanh nhất)
fpkg-cli build -s SRC -o OUT --level 7 --threads 8 --sha256 --sdk 4
fpkg-cli verify "/path/out/UP9000-PPSA12345_00-XXXX-A0100-V0100.pkg" --sha256
fpkg-cli clean-junk "/path/PPSA12345" --dry-run
```

Mã thoát: `0` thành công · `1` tham số sai · `2` tạo gói thất bại · `3` bị hủy (Ctrl+C dừng an toàn).

## Tối ưu tốc độ — số liệu đo thực tế

Máy đo: MacBook Apple Silicon (15 nhân logic), macOS 26, Kraken tích hợp, nguồn 400 MB (một nửa nén được, một nửa ngẫu nhiên).

| Cấu hình | Thời gian | Kích thước gói | Đỉnh dung lượng thư mục tạm |
|---|---|---|---|
| Kraken mức 2 (Nhanh) | 7,3 s | 254,3 MB | 257 MB |
| Kraken mức 4 (Cân bằng) | 7,9 s | 254,2 MB | 257 MB |
| Kraken mức 7 (Nhỏ nhất — mặc định, chuẩn Sony SDK) | 8,5 s | 254,2 MB | 257 MB |
| Không nén | 3,8 s | 423,4 MB | 403 MB |

Rút ra:

* Thư viện tự **lưu thô các tệp không nén được** (video, audio, texture đã nén) nên với dữ liệu game thật, khác biệt giữa các mức Kraken thường nhỏ; mức 2 tiết kiệm ~15 % thời gian nén.
* **Không nén** nhanh gấp đôi nhưng gói lớn hơn nhiều — hợp để test.
* Đỉnh dung lượng tạm ≈ kích thước ảnh trong đã nén (≤ nguồn); gói `.pkg` ≈ nguồn. Ứng dụng dùng hệ số dự phòng **1,1× nguồn cho ổ tạm và 1,1× cho ổ xuất** khi cảnh báo dung lượng.
* Sau giai đoạn PFS ngoài có khoảng lặng ~3–4 s cố định (tính digest & bọc khoá RSA) — không phụ thuộc kích thước.
* Các tối ưu ở tầng ứng dụng: quét thư mục song song một lượt (`FileSystemEnumerable`), nhật ký gom theo lô 80 ms và ảo hoá, tiến trình gộp theo trọng số giai đoạn để ETA ổn định, `TieredPGO` + GC đồng thời, thư mục tạm cùng ổ với thư mục xuất (tránh copy chéo ổ), chống máy ngủ.

**Đo với game thật** (ảnh `PPSA27625.exfat` 21,3 GB, 176 tệp, gắn bằng hdiutil, preset Nhỏ nhất — Kraken mức 7, 15 luồng): **3 phút 40 giây**, gói `.pkg` **9,03 GB** (≈ 42 % dung lượng nguồn nhờ Kraken nén các tệp `.ucas`), thư mục tạm được dọn sạch và ảnh tự tháo sau khi xong. Với game 70 GB ước tính khoảng 12–20 phút tuỳ ổ đĩa.

## Móc phát triển (dev hooks)

Ứng dụng có vài biến môi trường phục vụ kiểm thử không cần thao tác tay (dùng để tạo ảnh chụp trong `docs/screenshots/`):

| Biến | Tác dụng |
|---|---|
| `PSVIETHOA_SCREENSHOT=/duong/dan.png` | Mở cửa sổ, chụp giao diện ra PNG rồi thoát |
| `PSVIETHOA_SCROLL=end` | Cuộn cột cấu hình xuống cuối trước khi chụp |
| `PSVIETHOA_AUTOBUILD=/duong/dan/prefix` | Tự bấm **Tạo gói PKG**, chụp `prefix-building.png` và `prefix-done.png` rồi thoát |
| `PSVIETHOA_DEBUG=1` | In chẩn đoán (hộp thoại, bước build) ra stderr |
| `PSVIETHOA_SWITCH_LANG=en\|vi` | Đổi ngôn ngữ 1 giây sau khi mở (kiểm thử binding `{l:T}` cập nhật lúc chạy) |

Cấu hình người dùng nằm ở `~/Library/Application Support/PSVIETHOA FPKG Builder/settings.json` (macOS) hoặc `%APPDATA%\PSVIETHOA FPKG Builder\settings.json` (Windows); lỗi nghiêm trọng ghi vào `error.log` cùng thư mục.

## Ảnh giao diện

| Tối (mặc định) | Sáng |
|---|---|
| ![](docs/screenshots/ui-dark.png) | ![](docs/screenshots/ui-light.png) |

| Tuỳ chọn nâng cao | Sau khi tạo gói |
|---|---|
| ![](docs/screenshots/ui-advanced.png) | ![](docs/screenshots/ui-build-done.png) |

| Nguồn là ảnh .exfat (VI) | English |
|---|---|
| ![](docs/screenshots/ui-exfat-vi.png) | ![](docs/screenshots/ui-exfat-en.png) |

## Hệ thống thiết kế

Được sinh bằng skill *ui-ux-pro-max* (`design-system/ps5-fpkg-builder/MASTER.md`): phong cách **Dark Mode (OLED)** cho developer tool, nền `#0F172A`, thẻ `#1B2336`, nhấn xanh lá "run green" `#22C55E`, chữ **Inter** (UI) + **JetBrains Mono** (Content ID, nhật ký), icon vector Material Design (không dùng emoji), độ tương phản chữ ≥ 4.5:1 ở cả hai theme, mọi nút ≥ 36 px, phím tắt & focus rõ ràng.

## Ảnh đĩa exFAT (.exfat)

* Nhận diện tự động theo chữ ký `EXFAT   ` ở đầu volume, hoặc trong phân vùng MBR/GPT; các offset thông dụng (63, 2048 sector…) cũng được dò.
* Thư mục ứng dụng được tìm tới độ sâu 3 bên trong ảnh (ưu tiên gốc, ví dụ dump có `sce_sys` ngay gốc như `PPSA27625.exfat`).
* **macOS:** `hdiutil attach -readonly -imagekey diskimage-class=CRawDiskImage` → build thẳng từ điểm gắn, tháo khi xong. Chế độ *Tự động* chỉ gắn khi ảnh không có tệp rác; có tệp rác thì giải nén để bỏ qua chúng.
* **Windows/Linux:** giải nén bằng bộ đọc thuần .NET (3 luồng, bộ đệm 4 MB) ra `<thư mục tạm>/exfat-<tên>-<hash>/`, cần thêm dung lượng trống ≈ dữ liệu trong ảnh; xoá sau khi build (kể cả khi hủy).
* Kiểm thử: `tests/PsViethoa.FpkgBuilder.Tests/ExFatTests.cs` với ảnh mẫu 8 MB (volume thuần, MBR lồng thư mục, GPT rỗng) và ảnh thật nếu có trong `~/Downloads`.
* Đối chứng độc lập: cùng ảnh `PPSA06438.exfat` (Let's Build A Zoo, 813 MB) tạo gói theo hai cách — gắn bằng hdiutil và giải nén bằng bộ đọc thuần .NET — cho ra hai tệp `.pkg` **giống hệt nhau từng byte** (cùng SHA-256), tức bộ đọc exFAT đọc dữ liệu chính xác như driver của macOS.

## Ghi công & giấy phép

* **PSVIETHOA — Nguyễn Thanh Sơn & Ngô Phi Phương**: phát triển ứng dụng đa nền tảng, giao diện song ngữ, hỗ trợ ảnh exFAT, `fpkg-cli`.
* **Main project: Drakmor** — tác giả LibProsperoPkg, lõi tạo gói FPKG (PFS, NAPS, Kraken, PlayGo, kiểm tra gói).
* Lõi tạo gói: **LibProsperoPkg 1.2.0** — Drakmor (cùng SvenGDK ở công cụ GUI gốc); tệp `libs/LibProsperoPkg.dll` được giữ nguyên.
* `libScePubTools.dll` — Sony Publishing Tools (tuỳ chọn, chỉ Windows).
* Font JetBrains Mono — SIL Open Font License 1.1; Inter — SIL OFL (qua gói Avalonia.Fonts.Inter).
* Icon — Material Design Icons (Pictogrammers, Apache 2.0).
* Mã nguồn ứng dụng — PSVIETHOA (Nguyễn Thanh Sơn & Ngô Phi Phương), 2026.
