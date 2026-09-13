namespace PsViethoa.FpkgBuilder.App.Services;

/// <summary>Ghi chẩn đoán ra stderr khi đặt PSVIETHOA_DEBUG=1 (dùng khi phát triển).</summary>
public static class DebugLog
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("PSVIETHOA_DEBUG") == "1";

    public static void Write(string message)
    {
        if (Enabled)
        {
            Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
        }
    }
}
