namespace PsViethoa.FpkgBuilder.Core.Models;

/// <summary>Một dòng nhật ký có mốc thời gian và mức độ.</summary>
public sealed class LogEntry
{
    public LogEntry(LogLevel level, string message)
        : this(DateTime.Now, level, message)
    {
    }

    public LogEntry(DateTime time, LogLevel level, string message)
    {
        Time = time;
        Level = level;
        Message = message;
    }

    public DateTime Time { get; }

    public LogLevel Level { get; }

    public string Message { get; }

    public string TimeText => Time.ToString("HH:mm:ss");

    public bool IsInfo => Level == LogLevel.Info;

    public bool IsWarning => Level == LogLevel.Warning;

    public bool IsError => Level == LogLevel.Error;

    public bool IsSuccess => Level == LogLevel.Success;

    /// <summary>Phân loại một thông điệp do LibProsperoPkg phát ra.</summary>
    public static LogEntry FromLibrary(string message)
    {
        var cleaned = Services.PhaseCatalog.StripTimestamp(message).TrimEnd();
        return new LogEntry(Classify(cleaned), cleaned);
    }

    public static LogLevel Classify(string message)
    {
        var text = message.AsSpan().TrimStart();

        // Bỏ tiền tố dạng "[tag] " nếu có.
        if (text.Length > 0 && text[0] == '[')
        {
            var closing = text.IndexOf(']');
            if (closing is > 0 and < 32)
            {
                text = text[(closing + 1)..].TrimStart();
            }
        }

        if (text.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("WARN:", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("CẢNH BÁO", StringComparison.OrdinalIgnoreCase))
        {
            return LogLevel.Warning;
        }

        if (text.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("LỖI", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Exception", StringComparison.Ordinal))
        {
            return LogLevel.Error;
        }

        return LogLevel.Info;
    }

    public override string ToString() => $"[{TimeText}] {Message}";
}
