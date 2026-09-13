using System.Text.Json;
using System.Text.Json.Nodes;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Ép <c>applicationDrmType</c> trong sce_sys/param.json thành "standard" trong lúc tạo gói (fpkg-gui 0.6.5: "Forces the DRM
/// mode to be redefined as standard — resolves the lock issue": gói mang DRM "free" bị khoá trên PS5). Thư viện đọc param.json
/// thẳng từ thư mục nguồn nên tệp được ghi đè tạm thời và khôi phục nguyên vẹn (từng byte) khi Dispose — kể cả khi tạo gói lỗi.
/// </summary>
public sealed class ParamJsonDrmSwap : IDisposable
{
    public const string StandardDrm = "standard";

    private readonly string _path;
    private readonly byte[] _original;
    private bool _restored;

    private ParamJsonDrmSwap(string path, byte[] original, string previousValue)
    {
        _path = path;
        _original = original;
        PreviousValue = previousValue;
    }

    /// <summary>Giá trị applicationDrmType trước khi ép.</summary>
    public string PreviousValue { get; }

    /// <summary>Đọc applicationDrmType của một param.json (null nếu thiếu trường hoặc tệp không đọc được).</summary>
    public static string? ReadDrmType(string paramJsonPath)
    {
        try
        {
            using var stream = File.OpenRead(paramJsonPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("applicationDrmType", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Cần ép không: trường có mặt và khác "standard" (thiếu trường thì thư viện tự dùng "standard").</summary>
    public static bool NeedsRewrite(string? drmType) =>
        !string.IsNullOrWhiteSpace(drmType) && !string.Equals(drmType.Trim(), StandardDrm, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ghi tạm applicationDrmType = "standard" nếu cần. Trả về null khi không cần đổi; ném IOException khi tệp không ghi được
    /// (ví dụ ảnh gắn chỉ đọc) — người gọi quyết định bỏ qua hay đổi chiến lược.
    /// </summary>
    public static ParamJsonDrmSwap? Apply(string paramJsonPath, Action<LogEntry>? log)
    {
        var current = ReadDrmType(paramJsonPath);
        if (!NeedsRewrite(current))
        {
            return null;
        }

        var original = File.ReadAllBytes(paramJsonPath);
        var node = JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                   as JsonObject ?? throw new InvalidDataException("param.json");
        node["applicationDrmType"] = StandardDrm;
        var rewritten = JsonSerializer.SerializeToUtf8Bytes(node, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllBytes(paramJsonPath, rewritten);
        log?.Invoke(new LogEntry(LogLevel.Info, Loc.F("Plan.DrmForced", current!, StandardDrm)));
        return new ParamJsonDrmSwap(paramJsonPath, original, current!);
    }

    public void Dispose()
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        try
        {
            File.WriteAllBytes(_path, _original);
        }
        catch (Exception)
        {
            // Không khôi phục được (rất hiếm): tệp nguồn còn giữ "standard" — vô hại cho lần tạo gói sau.
        }
    }
}
