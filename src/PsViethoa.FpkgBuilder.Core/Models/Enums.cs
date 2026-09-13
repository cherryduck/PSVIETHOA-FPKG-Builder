namespace PsViethoa.FpkgBuilder.Core.Models;

/// <summary>Loại gói PS5 cần tạo.</summary>
public enum PackageKind
{
    /// <summary>Ứng dụng / trò chơi (APP) – thư mục đã có sce_sys + eboot.</summary>
    Application,

    /// <summary>Ứng dụng homebrew.</summary>
    Homebrew,

    /// <summary>Nội dung bổ sung (DLC) có dữ liệu (AC).</summary>
    DlcWithData,
}

/// <summary>Cách biểu diễn lớp PFS ngoài.</summary>
public enum OuterImageMode
{
    /// <summary>Không mã hoá, không xác thực lớp ngoài – chế độ khuyên dùng cho FPKG debug.</summary>
    PlaintextNoAuth,

    /// <summary>Mã hoá AES-XTS như gói gốc.</summary>
    Native,
}

/// <summary>Bộ mã hoá Kraken dùng cho ảnh PPR/NAPS.</summary>
public enum KrakenBackendKind
{
    /// <summary>Tự chọn: dùng libScePubTools.dll (Oodle gốc) nếu có trên Windows, nếu không dùng bộ nén tích hợp.</summary>
    Auto,

    /// <summary>Bộ nén Kraken thuần .NET tích hợp trong LibProsperoPkg – chạy trên mọi hệ điều hành.</summary>
    BuiltIn,

    /// <summary>Oodle gốc qua libScePubTools.dll (chỉ Windows).</summary>
    PublishingTools,

    /// <summary>Không nén khối kernel – nhanh nhất nhưng gói lớn nhất.</summary>
    Uncompressed,
}

/// <summary>Cách đưa nội dung ảnh exFAT vào quá trình tạo gói.</summary>
public enum ExFatStrategy
{
    /// <summary>Gắn ảnh trên macOS (không sao chép); giải nén ở nơi khác hoặc khi ảnh có tệp rác.</summary>
    Auto,

    /// <summary>Gắn ảnh bằng hdiutil (chỉ macOS).</summary>
    Mount,

    /// <summary>Giải nén thư mục ứng dụng ra thư mục tạm rồi tạo gói.</summary>
    Extract,
}

/// <summary>Định dạng container nén PFS ghi vào ảnh (không phải phiên bản superblock PFS).</summary>
public enum PfsFormat
{
    /// <summary>PFS v2 — mặc định, tương thích rộng; không có tuỳ chọn mở rộng.</summary>
    V2,

    /// <summary>PFS v3 — định dạng PS5 mới hơn: region hints, shuffle trước nén, dự đoán shuffle.</summary>
    V3,
}

/// <summary>Mẫu shuffle byte trước khi nén Kraken (chỉ PFS v3). Tên trùng với ProsperoPfsShufflePattern của thư viện.</summary>
public enum ShufflePatternKind
{
    None,
    PredictForStructs,
    PredictForBc1,
    PredictForBc2,
    PredictForBc3,
    PredictForBc4,
    PredictForBc5,
    Shuffle11111111,
    Shuffle116,
    Shuffle116116,
    Shuffle116224,
    Shuffle224,
    Shuffle26,
    Shuffle2626,
    Shuffle44,
    Shuffle4444,
    Shuffle8224,
    Shuffle844,
    Shuffle88,
}

/// <summary>Cách đọc nguồn: thư mục rời hay dự án GP5.</summary>
public enum SourceMode
{
    /// <summary>Dùng GP5 ở gốc nếu có, nếu không dùng thư mục rời.</summary>
    Auto,

    /// <summary>Luôn dùng cây thư mục rời, bỏ qua GP5.</summary>
    Folder,

    /// <summary>Dùng đúng tệp dự án GP5 đã chỉ định.</summary>
    Gp5Project,
}

/// <summary>Mức độ của một dòng nhật ký.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success,
}
