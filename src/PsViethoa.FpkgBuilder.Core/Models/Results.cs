namespace PsViethoa.FpkgBuilder.Core.Models;

/// <summary>Kết quả sau khi tạo và kiểm tra gói.</summary>
public sealed record BuildOutcome(
    string OutputPath,
    IReadOnlyList<string> Warnings,
    PackageVerification Verification,
    TimeSpan Elapsed);

/// <summary>Kết quả kiểm tra cấu trúc tệp PKG.</summary>
public sealed record PackageVerification(
    string ContainerType,
    long Length,
    byte SignedByte,
    ushort OuterMode,
    string? SeedMarker,
    string? Sha256,
    string? ContentId,
    int EntryCount,
    bool IsOfficial)
{
    public string ContainerLabel => ContainerType switch
    {
        "FullDebug" => Localization.Loc.T("Container.FullDebug"),
        "FullRetail" => Localization.Loc.T("Container.FullRetail"),
        "Meta" => Localization.Loc.T("Container.Meta"),
        _ => ContainerType,
    };
}

/// <summary>Ảnh chụp tiến trình tại một thời điểm.</summary>
public sealed record BuildProgress(
    string Phase,
    double PhasePercent,
    double OverallPercent,
    TimeSpan Elapsed,
    TimeSpan? Eta,
    int PhaseNumber,
    bool IsComplete,
    string? Throughput = null);

/// <summary>Thống kê thư mục nguồn.</summary>
public sealed record FolderStats(
    long FileCount,
    long DirectoryCount,
    long TotalBytes,
    long LargestFileBytes,
    TimeSpan ScanDuration);

/// <summary>Đánh giá dung lượng trống cho một lần tạo gói.</summary>
public sealed record DiskSpaceReport(
    string OutputVolume,
    long OutputFreeBytes,
    string TemporaryVolume,
    long TemporaryFreeBytes,
    long EstimatedTemporaryBytes,
    long EstimatedOutputBytes,
    bool SameVolume,
    bool Sufficient,
    string Summary);

/// <summary>Một lỗi nhập liệu gắn với một trường cụ thể.</summary>
public sealed record ValidationError(string Field, string Message);

/// <summary>Cấu hình tốc độ/nén đóng gói sẵn (tên/mô tả lấy từ bảng chuỗi theo Id).</summary>
public sealed record BuildPreset(
    string Id,
    KrakenBackendKind Backend,
    int KrakenLevel,
    PfsFormat PfsFormat = PfsFormat.V2,
    bool ShuffleAnalysis = false)
{
    public string Name => Localization.Loc.T($"Preset.{Id}.Name");

    public string Short => Localization.Loc.T($"Preset.{Id}.Short");

    public string Tagline => Localization.Loc.T($"Preset.{Id}.Tagline");

    public string Detail => Localization.Loc.T($"Preset.{Id}.Detail");
}

/// <summary>Tệp rác hệ điều hành tìm thấy trong thư mục nguồn.</summary>
public sealed record JunkFile(string Path, long Length, bool IsDirectory, bool IsReadOnly = false);
