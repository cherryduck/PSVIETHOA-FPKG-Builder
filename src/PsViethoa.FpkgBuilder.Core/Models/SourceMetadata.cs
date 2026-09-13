namespace PsViethoa.FpkgBuilder.Core.Models;

/// <summary>Thông tin đọc được từ thư mục nguồn (sce_sys/param.json, icon, PlayGo…).</summary>
public sealed class SourceMetadata
{
    public bool HasSceSys { get; set; }

    /// <summary>Nguồn là ảnh exFAT (không phải thư mục).</summary>
    public bool IsExFat { get; set; }

    public string? VolumeLabel { get; set; }

    /// <summary>Đường dẫn thư mục ứng dụng bên trong ảnh ("" = gốc).</summary>
    public string AppRootInImage { get; set; } = string.Empty;

    /// <summary>Nội dung icon0.png khi nguồn là ảnh exFAT (không có tệp trên đĩa).</summary>
    public byte[]? IconBytes { get; set; }

    /// <summary>Nguồn là tệp dự án GP5 (.gp5).</summary>
    public bool IsGp5 { get; set; }

    /// <summary>Bố cục dự án GP5: "Normal" (rootdir đi đệ quy) hoặc "Flat" (liệt kê tường minh).</summary>
    public string? Gp5Layout { get; set; }

    /// <summary>Thư mục ứng dụng mà dự án GP5 trỏ tới (đã phân giải; null nếu dự án Flat không liệt kê sce_sys/param.json).</summary>
    public string? Gp5RootFolder { get; set; }

    /// <summary>Loại volume trong dự án GP5 (prospero_app, prospero_patch, prospero_ac, prospero_al).</summary>
    public string? Gp5VolumeType { get; set; }

    /// <summary>Passcode 32 ký tự ghi trong dự án GP5 (null nếu không có hoặc sai độ dài).</summary>
    public string? Gp5Passcode { get; set; }

    public bool HasParamJson { get; set; }

    public string? ParamJsonPath { get; set; }

    public string? ParamJsonError { get; set; }

    public string? ContentId { get; set; }

    public string? TitleId { get; set; }

    public string? Version { get; set; }

    public string? Title { get; set; }

    public int? SdkMajor { get; set; }

    public string? SdkVersionRaw { get; set; }

    public int? CategoryType { get; set; }

    public string? IconPath { get; set; }

    public bool HasEboot { get; set; }

    public bool HasPlayGoChunk { get; set; }

    public bool HasPlayGoHashTable { get; set; }

    public bool HasPlayGoFicm { get; set; }

    public bool HasPlayGoScenario { get; set; }

    public int PlayGoFileCount =>
        (HasPlayGoChunk ? 1 : 0) + (HasPlayGoHashTable ? 1 : 0) + (HasPlayGoFicm ? 1 : 0);

    public bool HasCompletePlayGo => PlayGoFileCount == 3;
}
