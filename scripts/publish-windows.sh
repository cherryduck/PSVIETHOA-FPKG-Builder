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

find "$OUT" \( -name "*.pdb" -o -name "LibProsperoPkg.xml" \) -delete
mv "$OUT/app/PsViethoa.FpkgBuilder.App.exe" "$OUT/app/$APP_NAME.exe"

(cd "$OUT" && rm -f "$APP_NAME-$VERSION-$RID.zip" && zip -qry "$APP_NAME-$VERSION-$RID.zip" app fpkg-cli)
echo "   -> $OUT/$APP_NAME-$VERSION-$RID.zip"
echo "Xong."
