using System.Text;
using PsViethoa.FpkgBuilder.Core.ExFat;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Dấu vết AMPR emu trong nguồn: module giả, tệp chỉ mục và việc eboot có gọi libSceAmpr hay không.</summary>
public sealed record AmprInfo(
    bool EmuModule,
    bool EmuIndex,
    bool EbootImports,
    IReadOnlyList<string> LeftoverPaths)
{
    public static readonly AmprInfo Empty = new(false, false, false, Array.Empty<string>());

    /// <summary>Nguồn còn tệp của AMPR emu (module và/hoặc chỉ mục) — vô dụng trong gói vì engine luôn bỏ module.</summary>
    public bool HasLeftovers => LeftoverPaths.Count > 0;

    /// <summary>Có gì đó liên quan AMPR để cảnh báo người dùng.</summary>
    public bool Relevant => HasLeftovers || EbootImports;
}

/// <summary>
/// Tìm dấu vết AMPR (sce::Ampr — AMM + APR, quản lý bộ nhớ và nạp asset tốc độ cao) trong nguồn.
/// <para>
/// Bản dump chạy qua ShadowMount đặt bản giả lập <c>fakelib/libSceAmpr.sprx</c> kèm <c>ampr_emu.index</c>. Khi đóng gói,
/// LibProsperoPkg luôn bỏ <c>fakelib/libSceAmpr.sprx</c> (Drakmor: cần cho backport và dlc_emu) nhưng hiện còn sót tệp chỉ
/// mục — chính tác giả nói sẽ bỏ nốt ở bản sau. Lớp này phát hiện để (1) cảnh báo game dùng AMPR có thể treo ở màn hình
/// splash khi cài từ gói, (2) dọn tàn dư emu trước khi tạo gói.
/// </para>
/// </summary>
public static class AmprInspector
{
    /// <summary>Module giả lập (engine luôn bỏ khi tạo gói).</summary>
    public const string EmuModuleName = "libSceAmpr.sprx";

    /// <summary>Tệp chỉ mục do công cụ dump sinh ra khi thấy module giả lập.</summary>
    public const string EmuIndexName = "ampr_emu.index";

    public const string FakeLibFolder = "fakelib";

    /// <summary>Tên thư viện trong bảng import của eboot.bin khi game thật sự dùng AMPR.</summary>
    private static ReadOnlySpan<byte> ImportMarker => "libSceAmpr"u8;

    /// <summary>Chỉ quét phần đầu tệp thực thi: bảng chuỗi dynlib nằm ngay sau header.</summary>
    private const long MaxEbootScanBytes = 64L * 1024 * 1024;

    private const int ScanChunk = 1 << 20;

    /// <summary>Các đường dẫn (tương đối thư mục ứng dụng) sẽ bị loại khỏi gói khi dọn tàn dư emu.</summary>
    public static IEnumerable<string> LeftoverCandidates()
    {
        yield return EmuIndexName;
        yield return FakeLibFolder + "/" + EmuIndexName;
        yield return FakeLibFolder + "/" + EmuModuleName;
    }

    public static AmprInfo ScanFolder(string sourceFolder, CancellationToken cancellationToken)
    {
        try
        {
            var leftovers = new List<string>();
            var module = false;
            var index = false;
            foreach (var relative in LeftoverCandidates())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var full = Path.Combine(sourceFolder, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    continue;
                }

                leftovers.Add(relative);
                if (relative.EndsWith(EmuModuleName, StringComparison.OrdinalIgnoreCase))
                {
                    module = true;
                }
                else
                {
                    index = true;
                }
            }

            var imports = false;
            var eboot = Path.Combine(sourceFolder, "eboot.bin");
            if (File.Exists(eboot))
            {
                using var stream = File.OpenRead(eboot);
                imports = ContainsImport(stream, cancellationToken);
            }

            return new AmprInfo(module, index, imports, leftovers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AmprInfo.Empty;
        }
    }

    public static AmprInfo ScanImage(ExFatImage image, ExFatEntry appRoot, CancellationToken cancellationToken)
    {
        try
        {
            var leftovers = new List<string>();
            var module = false;
            var index = false;
            foreach (var relative in LeftoverCandidates())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = image.Find(relative, appRoot);
                if (entry == null || entry.IsDirectory)
                {
                    continue;
                }

                leftovers.Add(relative);
                if (relative.EndsWith(EmuModuleName, StringComparison.OrdinalIgnoreCase))
                {
                    module = true;
                }
                else
                {
                    index = true;
                }
            }

            var imports = false;
            var eboot = image.Find("eboot.bin", appRoot);
            if (eboot is { IsDirectory: false, Length: > 0 })
            {
                using var stream = image.OpenRead(eboot);
                imports = ContainsImport(stream, cancellationToken);
            }

            return new AmprInfo(module, index, imports, leftovers);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return AmprInfo.Empty;
        }
    }

    /// <summary>Tìm chuỗi "libSceAmpr" trong phần đầu tệp thực thi (đọc theo khối, có phần chồng lấn ở biên).</summary>
    public static bool ContainsImport(Stream stream, CancellationToken cancellationToken)
    {
        var marker = ImportMarker;
        var overlap = marker.Length - 1;
        var buffer = new byte[ScanChunk + overlap];
        var carried = 0;
        long scanned = 0;

        while (scanned < MaxEbootScanBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, carried, ScanChunk);
            if (read <= 0)
            {
                break;
            }

            var available = carried + read;
            if (buffer.AsSpan(0, available).IndexOf(marker) >= 0)
            {
                return true;
            }

            scanned += read;
            carried = Math.Min(overlap, available);
            buffer.AsSpan(available - carried, carried).CopyTo(buffer);
        }

        return false;
    }

    /// <summary>Mô tả ngắn cho nhật ký / lệnh inspect.</summary>
    public static string Describe(AmprInfo info) =>
        info.Relevant
            ? string.Join(", ", info.LeftoverPaths) + (info.EbootImports ? (info.HasLeftovers ? " · eboot→libSceAmpr" : "eboot→libSceAmpr") : string.Empty)
            : "—";

    internal static string ToUtf8Marker() => Encoding.UTF8.GetString(ImportMarker);
}
