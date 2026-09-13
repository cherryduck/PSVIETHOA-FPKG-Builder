using System.Text;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Kiểu xem trước của một tệp trong gói.</summary>
public enum PreviewKind
{
    None,

    /// <summary>Tệp rỗng.</summary>
    Empty,

    /// <summary>Văn bản UTF-8 (json, txt, xml, ini, cfg… hoặc dữ liệu hầu hết in được).</summary>
    Text,

    /// <summary>Ảnh PNG/JPEG.</summary>
    Image,

    /// <summary>Nhị phân → hex dump vài KiB đầu.</summary>
    Hex,
}

/// <summary>Bộ trợ giúp xem trước: nhận dạng kiểu, giải mã văn bản, hex dump — chỉ làm việc trên vài KiB đầu tệp.</summary>
public static class PackagePreview
{
    /// <summary>Số byte tối đa đọc để xem trước văn bản.</summary>
    public const int MaxTextBytes = 256 * 1024;

    /// <summary>Số byte tối đa của hex dump.</summary>
    public const int MaxHexBytes = 4096;

    /// <summary>Ảnh lớn hơn mức này không xem trước (tránh giải mã ảnh khổng lồ).</summary>
    public const int MaxImageBytes = 32 * 1024 * 1024;

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".json", ".txt", ".xml", ".ini", ".cfg", ".conf", ".md", ".csv", ".log", ".html", ".htm", ".css", ".js", ".lua", ".yaml", ".yml", ".toml", ".sh", ".bat", ".gp5", ".xhtml", ".svg",
    };

    private static readonly HashSet<string> ProbeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sfo", ".dat", ".inf", ".txt", ".list", ".cue", ".sig", ".def", "",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg",
    };

    /// <summary>Chọn kiểu xem trước theo phần mở rộng và nội dung đầu tệp.</summary>
    public static PreviewKind Detect(string path, ReadOnlySpan<byte> head, long size)
    {
        if (size == 0 || head.Length == 0)
        {
            return PreviewKind.Empty;
        }

        var extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension) && size <= MaxImageBytes && LooksLikeImage(head))
        {
            return PreviewKind.Image;
        }

        if (LooksLikeImage(head) && size <= MaxImageBytes)
        {
            return PreviewKind.Image;
        }

        if (TextExtensions.Contains(extension) && IsMostlyPrintable(head))
        {
            return PreviewKind.Text;
        }

        if (ProbeExtensions.Contains(extension) && IsMostlyPrintable(head))
        {
            return PreviewKind.Text;
        }

        return PreviewKind.Hex;
    }

    /// <summary>PNG hoặc JPEG theo chữ ký đầu tệp.</summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> head) =>
        (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) ||
        (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF);

    /// <summary>
    /// Dữ liệu "hầu hết in được": không có byte NUL, ≥ 95 % là ký tự in được/khoảng trắng ASCII
    /// hoặc byte thuộc chuỗi UTF-8 hợp lệ.
    /// </summary>
    public static bool IsMostlyPrintable(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            return false;
        }

        var start = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
        var printable = 0;
        var total = 0;
        for (var i = start; i < data.Length; i++)
        {
            var b = data[i];
            total++;
            if (b == 0)
            {
                return false;
            }

            if (b is 0x09 or 0x0A or 0x0D || (b >= 0x20 && b < 0x7F))
            {
                printable++;
                continue;
            }

            if (b >= 0x80)
            {
                // Đếm chuỗi UTF-8 hợp lệ (2–4 byte) như ký tự in được.
                var length = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 0;
                if (length > 0 && i + length - 1 < data.Length)
                {
                    var valid = true;
                    for (var k = 1; k < length; k++)
                    {
                        if ((data[i + k] & 0xC0) != 0x80)
                        {
                            valid = false;
                            break;
                        }
                    }

                    if (valid)
                    {
                        printable += length;
                        total += length - 1;
                        i += length - 1;
                        continue;
                    }
                }
            }
        }

        return total > 0 && printable * 100L / total >= 95;
    }

    /// <summary>Giải mã văn bản UTF-8 (bỏ BOM, thay byte hỏng bằng U+FFFD).</summary>
    public static string DecodeText(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            data = data[3..];
        }

        return Encoding.UTF8.GetString(data);
    }

    /// <summary>Hex dump kiểu "offset  16 byte hex  |ASCII|", tối đa <see cref="MaxHexBytes"/>.</summary>
    public static string HexDump(ReadOnlySpan<byte> data, long baseOffset = 0)
    {
        if (data.Length > MaxHexBytes)
        {
            data = data[..MaxHexBytes];
        }

        var builder = new StringBuilder(data.Length / 16 * 80 + 80);
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            builder.Append((baseOffset + offset).ToString("X8")).Append("  ");
            var line = data.Slice(offset, Math.Min(16, data.Length - offset));
            for (var i = 0; i < 16; i++)
            {
                if (i < line.Length)
                {
                    builder.Append(line[i].ToString("X2"));
                }
                else
                {
                    builder.Append("  ");
                }

                builder.Append(i == 7 ? "  " : " ");
            }

            builder.Append(" |");
            foreach (var b in line)
            {
                builder.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }

            builder.Append('|').AppendLine();
        }

        return builder.ToString();
    }
}
