#!/bin/zsh
# Đóng gói bản Windows x64 (tệp .exe đơn, tự chứa .NET) — chạy được từ macOS/Linux/Windows có dotnet SDK.
# Cách dùng:  scripts/publish-windows.sh
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
APP_NAME="PSVIETHOA FPKG Builder"
VERSION=$(grep -oE '<Version>[^<]+' "$ROOT/Directory.Build.props" | sed 's/<Version>//')
RID="win-x64"
OUT="$ROOT/dist/$RID"
R2R="${R2R:-true}"   # đặt R2R=false nếu máy build không tải được crossgen2 cho win-x64

echo "==> Publish $RID -> $OUT"
rm -rf "$OUT"
mkdir -p "$OUT/app" "$OUT/fpkg-cli"

dotnet publish "$ROOT/src/PsViethoa.FpkgBuilder.App/PsViethoa.FpkgBuilder.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=$R2R -p:DebugType=none \
  -o "$OUT/app"

dotnet publish "$ROOT/src/PsViethoa.FpkgBuilder.Cli/PsViethoa.FpkgBuilder.Cli.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=$R2R -p:DebugType=none \
  -o "$OUT/fpkg-cli"

# Oodle gốc (tuỳ chọn, chỉ Windows) đặt cạnh ứng dụng để chế độ "Tự động" dùng được.
cp "$ROOT/libs/libScePubTools.dll" "$OUT/app/" 2>/dev/null || true
cp "$ROOT/libs/libScePubTools.dll" "$OUT/fpkg-cli/" 2>/dev/null || true

# Driver Dokan (LGPL/MIT, bộ cài MSI nguyên bản của dự án Dokany) kèm theo để ứng dụng tự cài khi người dùng bấm
# "Bật gắn ảnh trực tiếp": ảnh .exfat/.ffpfsc được gắn thành ổ ảo thay vì giải nén. Tải một lần vào .cache/, kiểm SHA-256.
DOKAN_VERSION="${DOKAN_VERSION:-2.3.1.1000}"
DOKAN_SHA256="${DOKAN_SHA256:-69ff8cb37bfec3a75921c85ffd1c6370b50a9ec4ecef2cf3a009d488dcbf5465}"
DOKAN_CACHE="$ROOT/.cache/dokan-$DOKAN_VERSION"
mkdir -p "$DOKAN_CACHE"
if [[ ! -f "$DOKAN_CACHE/Dokan_x64.msi" ]] || ! echo "$DOKAN_SHA256  $DOKAN_CACHE/Dokan_x64.msi" | shasum -a 256 -c --status; then
  echo "==> Tải Dokan_x64.msi v$DOKAN_VERSION"
  curl -fsSL -o "$DOKAN_CACHE/Dokan_x64.msi" "https://github.com/dokan-dev/dokany/releases/download/v$DOKAN_VERSION/Dokan_x64.msi"
  echo "$DOKAN_SHA256  $DOKAN_CACHE/Dokan_x64.msi" | shasum -a 256 -c --status || { echo "SHA-256 của Dokan_x64.msi không khớp"; exit 1; }
fi
for f in license.lgpl.txt license.mit.txt; do
  [[ -f "$DOKAN_CACHE/$f" ]] || curl -fsSL -o "$DOKAN_CACHE/$f" "https://raw.githubusercontent.com/dokan-dev/dokany/v$DOKAN_VERSION/$f" || rm -f "$DOKAN_CACHE/$f"
done
mkdir -p "$OUT/app/redist"
cp "$DOKAN_CACHE/Dokan_x64.msi" "$OUT/app/redist/"
cp "$DOKAN_CACHE"/license.*.txt "$OUT/app/redist/" 2>/dev/null || true
cat > "$OUT/app/redist/README.txt" <<TXT
Dokan_x64.msi — Dokan Library $DOKAN_VERSION (x64), unmodified installer from https://github.com/dokan-dev/dokany
(LGPL-3.0 / MIT — see license.lgpl.txt and license.mit.txt). SHA-256: $DOKAN_SHA256
PSVIETHOA FPKG Builder installs it only when you click "Enable direct image mounting" (one admin prompt), so that
.exfat / .ffpfsc images can be mounted as a read-only virtual drive instead of being extracted to the temp folder.
TXT

find "$OUT" \( -name "*.pdb" -o -name "LibProsperoPkg.xml" \) -delete
mv "$OUT/app/PsViethoa.FpkgBuilder.App.exe" "$OUT/app/$APP_NAME.exe"

(cd "$OUT" && rm -f "$APP_NAME-$VERSION-$RID.zip" && zip -qry "$APP_NAME-$VERSION-$RID.zip" app fpkg-cli)
echo "   -> $OUT/$APP_NAME-$VERSION-$RID.zip"

# Bộ cài Setup.exe (NSIS — `brew install makensis`): cài ứng dụng + driver Dokan trong một lần, người dùng không cài gì thêm.
if command -v makensis >/dev/null 2>&1; then
  echo "==> Tạo bộ cài Setup.exe (NSIS)"
  makensis -V2 -DVERSION="$VERSION" -DDIST="$OUT" -DOUTFILE="$OUT/$APP_NAME-$VERSION-$RID-Setup.exe" \
    -DICON="$ROOT/src/PsViethoa.FpkgBuilder.App/Assets/app.ico" "$ROOT/installer/windows/PSVIETHOA.nsi"
  echo "   -> $OUT/$APP_NAME-$VERSION-$RID-Setup.exe"
else
  echo "   (bỏ qua Setup.exe: chưa có makensis — brew install makensis)"
fi
echo "Xong."
