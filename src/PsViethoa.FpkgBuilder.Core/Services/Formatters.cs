using System.Globalization;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Định dạng dung lượng, thời gian, tốc độ theo kiểu tiếng Việt.</summary>
public static class Formatters
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Size(long bytes)
    {
        if (bytes < 0)
        {
            return "—";
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} B"
            : $"{value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture)} {Units[unit]}";
    }

    public static string SizeWithBytes(long bytes) =>
        $"{Size(bytes)} ({bytes.ToString("N0", CultureInfo.InvariantCulture)} byte)";

    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Đồng hồ mm:ss hoặc h:mm:ss.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes:00}:{span.Seconds:00}";
    }

    /// <summary>Thời lượng dễ đọc: "2 giờ 05 phút", "3 phút 20 giây", "45 giây".</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalHours >= 1)
        {
            return Localization.Loc.F("Duration.HoursMinutes", (int)span.TotalHours, span.Minutes);
        }

        if (span.TotalMinutes >= 1)
        {
            return Localization.Loc.F("Duration.MinutesSeconds", span.Minutes, span.Seconds);
        }

        return Localization.Loc.F("Duration.Seconds", Math.Max(1, (int)Math.Ceiling(span.TotalSeconds)));
    }

    public static string Rate(double bytesPerSecond) =>
        bytesPerSecond <= 0 ? "—" : $"{Size((long)bytesPerSecond)}/s";
}
