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

/// <summary>Mức độ của một dòng nhật ký.</summary>
public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success,
}
