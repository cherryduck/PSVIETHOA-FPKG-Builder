using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Các cấu hình tốc độ/nén đóng gói sẵn. Mặc định là "Nhỏ nhất" (Kraken mức 7 — chuẩn Sony Publishing Tools).</summary>
public static class BuildPresets
{
    public static readonly BuildPreset Fast = new("fast", KrakenBackendKind.Auto, 2);

    public static readonly BuildPreset Balanced = new("balanced", KrakenBackendKind.Auto, 4);

    public static readonly BuildPreset Smallest = new("smallest", KrakenBackendKind.Auto, BuildRequest.DefaultKrakenLevel);

    public static BuildPreset Default => Smallest;

    public static IReadOnlyList<BuildPreset> All { get; } = [Fast, Balanced, Smallest];

    public static BuildPreset? Match(KrakenBackendKind backend, int level) =>
        All.FirstOrDefault(p => p.Backend == backend && p.KrakenLevel == level);

    public static BuildPreset? ById(string? id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void Apply(BuildPreset preset, BuildRequest request)
    {
        request.KrakenBackend = preset.Backend;
        request.KrakenLevel = preset.KrakenLevel;
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
