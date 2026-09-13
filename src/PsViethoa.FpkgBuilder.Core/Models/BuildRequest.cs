namespace PsViethoa.FpkgBuilder.Core.Models;

/// <summary>Toàn bộ tham số của một lần tạo gói.</summary>
public sealed class BuildRequest
{
    public const int PasscodeLength = 32;
    public const int MinKrakenLevel = -4;
    public const int MaxKrakenLevel = 9;
    public const int DefaultKrakenLevel = 7;
    public const int MaxThreads = 256;
    public const int MinPlayGoChunks = 1;
    public const int MaxPlayGoChunks = 64;
    public const int MinSdkMajor = 1;
    public const int MaxSdkMajor = 11;

    /// <summary>Thư mục ứng dụng (chứa sce_sys) hoặc tệp ảnh đĩa exFAT (.exfat).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Cách xử lý khi nguồn là ảnh exFAT.</summary>
    public ExFatStrategy ExFat { get; set; } = ExFatStrategy.Auto;

    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>Thư mục tạm cho ảnh trung gian. Để trống sẽ tự chọn cùng ổ đĩa với thư mục xuất.</summary>
    public string TemporaryFolder { get; set; } = string.Empty;

    public string ContentId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Version { get; set; } = "01.000.000";

    public string Passcode { get; set; } = new('0', PasscodeLength);

    public PackageKind Kind { get; set; } = PackageKind.Application;

    public OuterImageMode ImageMode { get; set; } = OuterImageMode.PlaintextNoAuth;

    public KrakenBackendKind KrakenBackend { get; set; } = KrakenBackendKind.Auto;

    /// <summary>Mức nén Kraken -4..9 (7 = mặc định SDK).</summary>
    public int KrakenLevel { get; set; } = DefaultKrakenLevel;

    /// <summary>Số luồng nén; 0 = số nhân CPU logic.</summary>
    public int Threads { get; set; }

    public int PlayGoChunks { get; set; } = MaxPlayGoChunks;

    public bool Deterministic { get; set; } = true;

    public bool ComputeSha256 { get; set; }

    /// <summary>Ghi đè SDK major (1..11); null = giữ nguyên metadata gốc.</summary>
    public int? SdkMajorOverride { get; set; }

    /// <summary>Đường dẫn libScePubTools.dll (chỉ dùng khi backend là PublishingTools/Auto trên Windows).</summary>
    public string? PublishingToolsPath { get; set; }

    /// <summary>Ngăn máy ngủ trong lúc tạo gói.</summary>
    public bool PreventSleep { get; set; } = true;

    public BuildRequest Clone() => (BuildRequest)MemberwiseClone();
}
