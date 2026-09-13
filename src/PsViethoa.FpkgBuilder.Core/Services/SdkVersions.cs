using System.Globalization;
using LibProsperoPkg.Content;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Một thế hệ SDK PS5 (1..11) cùng mã .sceversion tương ứng.</summary>
public sealed record SdkGeneration(int Major, string Release, ulong ExecutableVersion)
{
    public string Label => Major == SdkVersions.MaxMajor ? Localization.Loc.F("Sdk.Latest", Major) : Localization.Loc.F("Sdk.Label", Major);

    public override string ToString() => Label;
}

/// <summary>Bảng SDK PS5 lấy từ LibProsperoPkg.</summary>
public static class SdkVersions
{
    public const int MinMajor = 1;
    public const int MaxMajor = 11;

    private static readonly Lazy<IReadOnlyList<SdkGeneration>> AllLazy = new(() =>
    {
        var list = new List<SdkGeneration>();
        for (var major = MinMajor; major <= MaxMajor; major++)
        {
            try
            {
                var release = ProsperoSdkVersions.GetByMajor(major);
                list.Add(new SdkGeneration(major, release.Release, release.ExecutableVersion));
            }
            catch (Exception)
            {
                list.Add(new SdkGeneration(major, $"{major}.000", 0));
            }
        }

        return list;
    });

    public static IReadOnlyList<SdkGeneration> All => AllLazy.Value;

    public static SdkGeneration? Get(int major) => All.FirstOrDefault(g => g.Major == major);

    /// <summary>Tìm nhãn phát hành cho một mã .sceversion đầy đủ.</summary>
    public static string DescribeExecutableVersion(ulong executableVersion)
    {
        foreach (var candidate in ProsperoSdkVersions.Releases)
        {
            if (candidate.ExecutableVersion == executableVersion)
            {
                return candidate.Release;
            }
        }

        return $"0x{executableVersion:X16}";
    }

    /// <summary>Đọc SDK major từ chuỗi sdkVersion "0x04500000…" trong param.json.</summary>
    public static bool TryReadMajor(string? version, out int major)
    {
        major = 0;
        if (version == null || version.Length != 18 ||
            !version.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
            !byte.TryParse(version.AsSpan(2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var encoded))
        {
            return false;
        }

        var high = encoded >> 4;
        var low = encoded & 0xF;
        if (high > 9 || low > 9)
        {
            return false;
        }

        major = high * 10 + low;
        return major is >= MinMajor and <= MaxMajor;
    }
}
