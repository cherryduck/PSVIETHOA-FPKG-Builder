using LibProsperoPkg;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Chuẩn hoá phiên bản nội dung NN.NNN.NNN của PS5.</summary>
public static class VersionHelper
{
    public const string Default = "01.000.000";

    /// <summary>Thử chuyển chuỗi phiên bản (NN.NNN.NNN hoặc NN.NN cũ) về dạng chuẩn.</summary>
    public static bool TryCanonicalize(string? text, out string canonical)
    {
        canonical = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!ProsperoContentVersion.TryParse(text.Trim(), out var version))
        {
            return false;
        }

        canonical = version.Canonical;
        return true;
    }

    public static string CanonicalOrDefault(string? text) => TryCanonicalize(text, out var canonical) ? canonical : Default;
}
