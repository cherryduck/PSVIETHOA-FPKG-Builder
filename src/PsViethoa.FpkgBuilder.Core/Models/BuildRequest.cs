namespace PsViethoa.FpkgBuilder.Core.Models;

/// <summary>Toàn bộ tham số của một lần tạo gói.</summary>
public sealed class BuildRequest
{
    public const int PasscodeLength = 32;
    public const int MinKrakenLevel = -4;
    public const int MaxKrakenLevel = 9;
    public const int DefaultKrakenLevel = 4;
    public const int MaxThreads = 256;
    public const int MinPlayGoChunks = 1;
    public const int MaxPlayGoChunks = 64;
    public const int MinSdkMajor = 1;
    public const int MaxSdkMajor = 11;
    public const int MinKrakenBlockKiB = 128;
    public const int MaxKrakenBlockKiB = 256;
    public const int DefaultKrakenBlockKiB = 256;

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

    /// <summary>Định dạng container nén PFS (v2 mặc định, v3 mở các tuỳ chọn shuffle).</summary>
    public PfsFormat PfsFormat { get; set; } = PfsFormat.V2;

    /// <summary>Kích thước khối nén Kraken, KiB (128..256; 256 = mặc định SDK).</summary>
    public int KrakenBlockKiB { get; set; } = DefaultKrakenBlockKiB;

    /// <summary>Mẫu shuffle trước nén (chỉ có tác dụng với PFS v3).</summary>
    public ShufflePatternKind ShufflePattern { get; set; } = ShufflePatternKind.None;

    /// <summary>Tự đánh giá mọi mẫu shuffle trong lúc nén và chọn mẫu tốt nhất (PFS v3).</summary>
    public bool ShuffleAnalysis { get; set; }

    /// <summary>Mức Kraken dùng để phân giải bí danh shuffle dự đoán; null = tự động theo SDK (PFS v3).</summary>
    public int? ShufflePredictionLevel { get; set; }

    /// <summary>Bỏ kiểm tra tương thích input-header PFS v3 (chuyên gia).</summary>
    public bool SkipPfsInputCheck { get; set; }

    /// <summary>Gộp khối và điều chỉnh canh lề bố cục vật lý như Publishing Tools (khuyên bật).</summary>
    public bool LayoutOptimization { get; set; } = true;

    /// <summary>Ép applicationDrmType = "standard" trong lúc tạo gói (tránh game bị khoá trên PS5); tệp nguồn được khôi phục sau đó.</summary>
    public bool ForceStandardDrm { get; set; } = true;

    /// <summary>Cách đọc nguồn (thư mục rời / GP5).</summary>
    public SourceMode SourceMode { get; set; } = SourceMode.Auto;

    /// <summary>Tệp dự án GP5 khi SourceMode = Gp5Project.</summary>
    public string? ProjectFilePath { get; set; }

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
