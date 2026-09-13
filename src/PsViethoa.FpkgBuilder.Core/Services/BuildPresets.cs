using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Các cấu hình tốc độ/nén đóng gói sẵn. Mặc định là "Chuẩn Sony" (id balanced, Kraken mức 4): engine 2.0.0 dùng cùng bộ nén Normal
/// cho mức 4–7 nên mức 4 của engine 2.1.0 cho đúng kết quả "Kraken 7" cũ với tốc độ cũ; "Nhỏ nhất" (mức 7 Optimal3) nhỏ hơn ~3 % nhưng chậm gấp 5–6 lần.
/// </summary>
public static class BuildPresets
{
    public static readonly BuildPreset Fast = new("fast", KrakenBackendKind.Auto, 2);

    public static readonly BuildPreset Balanced = new("balanced", KrakenBackendKind.Auto, 4);

    public static readonly BuildPreset Smallest = new("smallest", KrakenBackendKind.Auto, 7);

    /// <summary>Nén tối đa: Kraken 9 + PFS v3 + tự phân tích shuffle (chậm nhất, thử nghiệm).</summary>
    public static readonly BuildPreset Maximum = new("maximum", KrakenBackendKind.Auto, BuildRequest.MaxKrakenLevel, PfsFormat.V3, ShuffleAnalysis: true);

    public static BuildPreset Default => Balanced;

    public static IReadOnlyList<BuildPreset> All { get; } = [Fast, Balanced, Smallest, Maximum];

    public static BuildPreset? Match(KrakenBackendKind backend, int level, PfsFormat pfsFormat = PfsFormat.V2, bool shuffleAnalysis = false) =>
        All.FirstOrDefault(p => p.Backend == backend && p.KrakenLevel == level && p.PfsFormat == pfsFormat && p.ShuffleAnalysis == shuffleAnalysis);

    /// <summary>Tìm preset theo id; "sony"/"standard" là bí danh của preset mặc định (Chuẩn Sony = mức 4).</summary>
    public static BuildPreset? ById(string? id) =>
        string.Equals(id, "sony", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "standard", StringComparison.OrdinalIgnoreCase)
            ? Balanced
            : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void Apply(BuildPreset preset, BuildRequest request)
    {
        request.KrakenBackend = preset.Backend;
        request.KrakenLevel = preset.KrakenLevel;
        request.PfsFormat = preset.PfsFormat;
        request.ShuffleAnalysis = preset.ShuffleAnalysis;
        // Preset không mang mẫu shuffle cố định (Match() cũng chỉ nhận None) — "Tối đa" để thư viện tự chọn qua phân tích.
        request.ShufflePattern = ShufflePatternKind.None;
    }

    /// <summary>Tên hiển thị của mẫu shuffle: "Shuffle 1-1-6 · stride 8" (các mẫu tự đoán lấy từ bảng chuỗi).</summary>
    public static string ShufflePatternLabel(ShufflePatternKind pattern)
    {
        var name = pattern.ToString();
        if (name.StartsWith("Shuffle", StringComparison.Ordinal) && name.Length > 7 && char.IsDigit(name[7]))
        {
            var digits = name[7..];
            var stride = digits.Sum(c => c - '0');
            return $"Shuffle {string.Join("-", digits.ToCharArray())} · stride {stride}";
        }

        return Loc.T("Shuffle." + name);
    }

    /// <summary>Tên mức nén Kraken theo cách gọi của Oodle.</summary>
    public static string KrakenLevelName(int level) => level switch
    {
        -4 => "HyperFast4",
        -3 => "HyperFast3",
        -2 => "HyperFast2",
        -1 => "HyperFast1",
        0 => "None",
        1 => "SuperFast",
        2 => "VeryFast",
        3 => "Fast",
        4 => "Normal",
        5 => "Optimal1",
        6 => "Optimal2",
        7 => "Optimal3",
        8 => "Optimal4",
        9 => "Optimal5",
        _ => level.ToString(),
    };

    public static string KrakenLevelHint(int level) => Loc.T(level switch
    {
        <= -1 => "Level.HyperFast",
        0 => "Level.None",
        1 or 2 => "Level.VeryFast",
        3 => "Level.Fast",
        4 => "Level.Normal",
        5 or 6 => "Level.High",
        7 => "Level.Default",
        8 => "Level.VeryHigh",
        _ => "Level.Max",
    });
}
