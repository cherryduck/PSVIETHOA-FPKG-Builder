using System.Globalization;
using System.Text.RegularExpressions;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Ước lượng phần trăm của giai đoạn nén ảnh trong từ các dòng nhật ký theo từng tệp lớn của LibProsperoPkg
/// ("processing large file: … (N bytes)" và "Kraken level 7: 40% of …"), có trọng số theo kích thước tệp
/// so với tổng số byte của ảnh trong. Thư viện không ghi phần trăm tổng cho giai đoạn này, nên nếu không ước lượng
/// thanh tiến trình sẽ đứng yên suốt phần dài nhất của một lần tạo gói lớn.
/// </summary>
public sealed partial class InnerDataEstimator
{
    private readonly Dictionary<string, (long Size, double Percent)> _files = new(StringComparer.Ordinal);
    private long _totalBytes;

    [GeneratedRegex(@"^\[inner\]\s+(?:Planning\b[^:]*:\s*[\d,.]+\s+files,\s*(?<bytes>[\d,.]+)\s+uncompressed bytes|Preparing\s+[\d,.]+\s+inner files\s+\((?<bytes>[\d,.]+)\s+bytes\))", RegexOptions.CultureInvariant)]
    private static partial Regex TotalPattern();

    [GeneratedRegex(@"^\[inner\]\s+processing large file:\s+(?<path>.+?)\s+\((?<bytes>[\d,.]+)\s+bytes\)", RegexOptions.CultureInvariant)]
    private static partial Regex LargeFilePattern();

    [GeneratedRegex(@"^\[inner\]\s+\S.*?\blevel\s+-?\d+:\s+(?<pct>\d{1,3})%\s+of\s+(?<path>\S.*?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FileProgressPattern();

    /// <summary>Tổng số byte chưa nén của ảnh trong (0 nếu chưa biết).</summary>
    public long TotalBytes => _totalBytes;

    /// <summary>Số tệp lớn đã thấy trong nhật ký.</summary>
    public int LargeFileCount => _files.Count;

    /// <summary>
    /// Quan sát một dòng nhật ký (đã bỏ tiền tố thời gian). Trả về true kèm phần trăm ước lượng
    /// khi dòng đó làm tiến trình nén ảnh trong thay đổi.
    /// </summary>
    public bool TryObserve(string message, out double percent)
    {
        percent = 0;
        if (!message.StartsWith("[inner]", StringComparison.Ordinal))
        {
            return false;
        }

        var total = TotalPattern().Match(message);
        if (total.Success)
        {
            _totalBytes = ParseLong(total.Groups["bytes"].Value);
            return false;
        }

        var large = LargeFilePattern().Match(message);
        if (large.Success)
        {
            var path = large.Groups["path"].Value;
            if (!_files.ContainsKey(path))
            {
                _files[path] = (ParseLong(large.Groups["bytes"].Value), 0);
            }

            return false;
        }

        var progress = FileProgressPattern().Match(message);
        if (progress.Success && _files.TryGetValue(progress.Groups["path"].Value, out var file))
        {
            var pct = Math.Clamp(double.Parse(progress.Groups["pct"].Value, CultureInfo.InvariantCulture), 0, 100);
            _files[progress.Groups["path"].Value] = (file.Size, Math.Max(file.Percent, pct));
            percent = Estimate();
            return _totalBytes > 0;
        }

        return false;
    }

    private double Estimate()
    {
        if (_totalBytes <= 0)
        {
            return 0;
        }

        double done = 0;
        foreach (var (size, pct) in _files.Values)
        {
            done += size * pct / 100.0;
        }

        // Không bao giờ tự báo 100%: các tệp nhỏ không ghi nhật ký, và dòng "complete"/"cache" của thư viện mới khép giai đoạn.
        return Math.Clamp(done * 100.0 / _totalBytes, 0, 99);
    }

    private static long ParseLong(string text)
    {
        var digits = text.Replace(",", string.Empty).Replace(".", string.Empty);
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
