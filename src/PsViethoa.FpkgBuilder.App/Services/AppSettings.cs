using PsViethoa.FpkgBuilder.Core.Models;
using PsViethoa.FpkgBuilder.Core.Services;

namespace PsViethoa.FpkgBuilder.App.Services;

/// <summary>Cấu hình được lưu giữa các phiên làm việc.</summary>
public sealed class AppSettings
{
    /// <summary>Thư mục ứng dụng hoặc tệp ảnh .exfat.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public ExFatStrategy ExFat { get; set; } = ExFatStrategy.Auto;

    /// <summary>Ngôn ngữ giao diện: "vi" hoặc "en".</summary>
    public string Language { get; set; } = "vi";

    public string OutputFolder { get; set; } = string.Empty;

    public string TemporaryFolder { get; set; } = string.Empty;

    public string ContentId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Version { get; set; } = VersionHelper.Default;

    public PackageKind Kind { get; set; } = PackageKind.Application;

    public OuterImageMode ImageMode { get; set; } = OuterImageMode.PlaintextNoAuth;

    public KrakenBackendKind KrakenBackend { get; set; } = KrakenBackendKind.Auto;

    public int KrakenLevel { get; set; } = BuildRequest.DefaultKrakenLevel;

    public int Threads { get; set; }

    public int PlayGoChunks { get; set; } = BuildRequest.MaxPlayGoChunks;

    public bool Deterministic { get; set; } = true;

    public bool ComputeSha256 { get; set; }

    public bool PreventSleep { get; set; } = true;

    public bool OverrideSdk { get; set; }

    public int SdkMajor { get; set; } = 1;

    public string PublishingToolsPath { get; set; } = string.Empty;

    public bool AdvancedExpanded { get; set; }

    public bool AutoScrollLog { get; set; } = true;

    public string Theme { get; set; } = "Dark";

    public double WindowWidth { get; set; } = 1320;

    public double WindowHeight { get; set; } = 880;

    public List<string> RecentSources { get; set; } = new();
}

public static class SettingsService
{
    public static string Directory => JsonFileStore.AppDataDirectory(AppInfo.DataFolderName);

    public static string Path => System.IO.Path.Combine(Directory, "settings.json");

    public static AppSettings Load() => JsonFileStore.Load<AppSettings>(Path);

    public static bool Save(AppSettings settings) => JsonFileStore.Save(Path, settings);
}
