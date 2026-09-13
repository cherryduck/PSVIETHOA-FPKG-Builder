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
