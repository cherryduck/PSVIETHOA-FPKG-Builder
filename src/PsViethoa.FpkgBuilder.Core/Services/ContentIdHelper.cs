using System.Text;
using System.Text.RegularExpressions;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Kiểm tra và tạo Content ID / Title ID của PS5.</summary>
public static partial class ContentIdHelper
{
    public const int ContentIdLength = 36;
    public const int TitleIdLength = 9;
    public const int LabelLength = 16;
    public const string DefaultPublisherPrefix = "UP9000";
    public const string Format = "UP0000-PPSA00000_00-XXXXXXXXXXXXXXXX";

    [GeneratedRegex("^[A-Z]{2}[0-9]{4}-[A-Z]{4}[0-9]{5}_00-[A-Z0-9]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentIdPattern();

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex TitleIdPattern();

    public static bool IsValid(string? contentId) => contentId != null && ContentIdPattern().IsMatch(contentId);

    public static bool IsValidTitleId(string? titleId) => titleId != null && TitleIdPattern().IsMatch(titleId);

    /// <summary>Lấy Title ID (9 ký tự, ví dụ PPSA00000) từ Content ID hợp lệ.</summary>
    public static string? TitleIdOf(string? contentId) =>
        IsValid(contentId) ? contentId!.Substring(7, TitleIdLength) : null;

    /// <summary>Chuẩn hoá chuỗi nhập: cắt khoảng trắng, viết hoa.</summary>
    public static string Normalize(string? contentId) => (contentId ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Ghép Content ID từ tiền tố nhà phát hành, Title ID và nhãn 16 ký tự.</summary>
    public static string Compose(string publisherPrefix, string titleId, string label)
    {
        var prefix = SanitizeUpper(publisherPrefix, 6, '0');
        var title = SanitizeUpper(titleId, TitleIdLength, '0');
        var tail = SanitizeUpper(label, LabelLength, '0');
        return $"{prefix}-{title}_00-{tail}";
    }

    /// <summary>Gợi ý Content ID khi thư mục nguồn không có param.json.</summary>
    public static string Suggest(string? titleId, string? title)
    {
        var validTitleId = IsValidTitleId(titleId) ? titleId! : "PPSA00000";
        var label = string.IsNullOrWhiteSpace(title) ? "PSVIETHOA" : title;
        return Compose(DefaultPublisherPrefix, validTitleId, label);
    }

    private static string SanitizeUpper(string? input, int length, char pad)
    {
        var builder = new StringBuilder(length);
        foreach (var character in (input ?? string.Empty).ToUpperInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                if (builder.Length == length)
                {
                    break;
                }
            }
        }

        while (builder.Length < length)
        {
            builder.Append(pad);
        }

        return builder.ToString();
    }
}
