using System.Reflection;

namespace PsViethoa.FpkgBuilder.App;

/// <summary>Thông tin tĩnh của ứng dụng.</summary>
public static class AppInfo
{
    public const string Name = "PSVIETHOA FPKG Builder";

    public const string DataFolderName = "PSVIETHOA FPKG Builder";

    public const string Team = "PSVIETHOA";

    public const string Authors = "Nguyễn Thanh Sơn & Ngô Phi Phương";

    public const string MainProjectAuthor = "Drakmor";

    public const string AuthorUrl = "https://github.com/SvenGDK";

    public static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "2.0.0";

    public static string PlatformLabel =>
        OperatingSystem.IsMacOS() ? "macOS" : OperatingSystem.IsWindows() ? "Windows" : OperatingSystem.IsLinux() ? "Linux" : "Khác";
}
