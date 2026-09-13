using Avalonia;

namespace PsViethoa.FpkgBuilder.App;

internal static class Program
{
    // Điểm vào: không chạm vào bất kỳ API Avalonia nào trước khi AppMain chạy,
    // vì mọi thứ chưa được khởi tạo và có thể làm hỏng ứng dụng.
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/// <summary>Ghi lỗi nghiêm trọng ra tệp trong thư mục dữ liệu ứng dụng.</summary>
internal static class CrashLog
{
    public static string Path =>
        System.IO.Path.Combine(Core.Services.JsonFileStore.AppDataDirectory(AppInfo.DataFolderName), "error.log");

    public static void Write(Exception exception)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            System.IO.File.AppendAllText(
                Path,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // Ghi nhật ký lỗi không được phép gây thêm lỗi.
        }
    }
}
