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

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Đọc applicationDrmType của một param.json (null nếu thiếu trường hoặc tệp không đọc được).</summary>
    public static string? ReadDrmType(string paramJsonPath)
    {
        try
        {
            return ReadDrmType(File.ReadAllBytes(paramJsonPath));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Đọc applicationDrmType từ nội dung param.json trong bộ nhớ (null nếu thiếu trường hoặc JSON hỏng).</summary>
    public static string? ReadDrmType(ReadOnlyMemory<byte> paramJson)
    {
        try
        {
            using var document = JsonDocument.Parse(paramJson, DocumentOptions);
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
    /// Tạo bản param.json với applicationDrmType = "standard" từ nội dung gốc (dùng để đè tệp trên ổ ảo mà không đụng ảnh).
    /// Trả về null khi không cần đổi; <paramref name="previous"/> là giá trị cũ.
    /// </summary>
    public static byte[]? Rewrite(byte[] original, out string? previous)
    {
        previous = ReadDrmType(original);
        if (!NeedsRewrite(previous))
        {
            return null;
        }

        var node = JsonNode.Parse(original, documentOptions: DocumentOptions) as JsonObject ?? throw new InvalidDataException("param.json");
        node["applicationDrmType"] = StandardDrm;
        return JsonSerializer.SerializeToUtf8Bytes(node, WriteOptions);
    }

    /// <summary>
    /// Ghi tạm applicationDrmType = "standard" nếu cần. Trả về null khi không cần đổi; ném IOException khi tệp không ghi được
    /// (ví dụ ảnh gắn chỉ đọc) — người gọi quyết định bỏ qua hay đổi chiến lược.
    /// </summary>
    public static ParamJsonDrmSwap? Apply(string paramJsonPath, Action<LogEntry>? log)
    {
        var original = File.ReadAllBytes(paramJsonPath);
        var rewritten = Rewrite(original, out var current);
        if (rewritten == null)
        {
            return null;
        }

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
