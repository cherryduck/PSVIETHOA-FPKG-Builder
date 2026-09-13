using PsViethoa.FpkgBuilder.Core.ExFat;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Chuẩn hoá và kiểm tra một BuildRequest trước khi đưa vào engine.</summary>
public static class BuildPreparer
{
    public const string FieldSource = "Source";
    public const string FieldOutput = "Output";
    public const string FieldTemporary = "Temporary";
    public const string FieldContentId = "ContentId";
    public const string FieldPasscode = "Passcode";
    public const string FieldVersion = "Version";
    public const string FieldThreads = "Threads";
    public const string FieldPlayGo = "PlayGo";
    public const string FieldSdk = "Sdk";
    public const string FieldKrakenLevel = "KrakenLevel";
    public const string FieldPublishingTools = "PublishingTools";
    public const string FieldExFat = "ExFat";

    /// <summary>Gợi ý thư mục xuất: cạnh nguồn, tên "&lt;nguồn&gt;-pkg" (bỏ đuôi .exfat nếu là ảnh).</summary>
    public static string SuggestOutputFolder(string sourcePath)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        var parent = Directory.GetParent(full)?.FullName ?? full;
        var name = File.Exists(full) ? Path.GetFileNameWithoutExtension(full) : Path.GetFileName(full);
        return Path.Combine(parent, name + "-pkg");
    }

    /// <summary>
    /// Gợi ý thư mục tạm cùng ổ đĩa với thư mục xuất (tránh sao chép chéo ổ).
    /// Với ổ hệ thống dùng thư mục cache của người dùng thay vì gốc ổ (macOS không cho ghi vào "/").
    /// </summary>
    public static string SuggestTemporaryFolder(string outputFolder)
    {
        try
        {
            var mount = DiskSpaceAdvisor.ResolveMountPoint(outputFolder);
            if (!string.IsNullOrEmpty(mount))
            {
                var isSystemVolume = OperatingSystem.IsWindows()
                    ? string.Equals(mount, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)
                    : mount == "/" || mount.StartsWith("/System/", StringComparison.Ordinal);

                if (!isSystemVolume)
                {
                    return Path.Combine(mount, "fpkg-temp");
                }
            }
        }
        catch (Exception)
        {
        }

        if (OperatingSystem.IsMacOS())
        {
            var caches = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "PSVIETHOA FPKG Builder");
            return Path.Combine(caches, "temp");
        }

        return Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetTempPath()), "fpkg-temp");
    }

    /// <summary>Trả về bản sao đã chuẩn hoá (đường dẫn đầy đủ, Content ID viết hoa, phiên bản chuẩn, thư mục tạm mặc định).</summary>
    public static BuildRequest Normalize(BuildRequest request)
    {
        var result = request.Clone();
        result.SourcePath = FullPathOrEmpty(request.SourcePath);
        result.OutputFolder = FullPathOrEmpty(request.OutputFolder);
        result.ContentId = ContentIdHelper.Normalize(request.ContentId);
        result.Title = (request.Title ?? string.Empty).Trim();
        result.Passcode = request.Passcode ?? string.Empty;
        result.Version = VersionHelper.TryCanonicalize(request.Version, out var canonical) ? canonical : (request.Version ?? string.Empty).Trim();

        var temporary = (request.TemporaryFolder ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(temporary) && !string.IsNullOrEmpty(result.OutputFolder))
        {
            temporary = SuggestTemporaryFolder(result.OutputFolder);
        }

        result.TemporaryFolder = FullPathOrEmpty(temporary);
        result.PublishingToolsPath = string.IsNullOrWhiteSpace(request.PublishingToolsPath) ? null : FullPathOrEmpty(request.PublishingToolsPath);
        return result;
    }

    /// <summary>Kiểm tra request (đã hoặc chưa chuẩn hoá). Trả về danh sách lỗi theo trường.</summary>
    public static IReadOnlyList<ValidationError> Validate(BuildRequest request)
    {
        var errors = new List<ValidationError>();
        var normalized = Normalize(request);

        var kind = SourceLocator.Detect(normalized.SourcePath);
        switch (kind)
        {
            case SourceKind.None:
                errors.Add(new ValidationError(FieldSource, Loc.T("Val.SourceMissing")));
                break;
            case SourceKind.Folder:
                if (!Directory.Exists(Path.Combine(normalized.SourcePath, "sce_sys")))
                {
                    errors.Add(new ValidationError(FieldSource, Loc.T("Val.SourceNoSceSys")));
                }

                break;
            case SourceKind.ExFatImage:
                try
                {
                    SourceLocator.Resolve(normalized.SourcePath);
                }
                catch (InvalidDataException ex)
                {
                    errors.Add(new ValidationError(FieldSource, ex.Message));
                }
                catch (Exception ex)
                {
                    errors.Add(new ValidationError(FieldSource, Loc.T("Val.ExFatInvalid") + " " + ex.Message));
                }

                if (normalized.ExFat == ExFatStrategy.Mount && !ExFatMounter.IsAvailable)
                {
                    errors.Add(new ValidationError(FieldExFat, Loc.T("Val.ExFatMountUnsupported")));
                }

                break;
        }

        if (string.IsNullOrEmpty(normalized.OutputFolder))
        {
            errors.Add(new ValidationError(FieldOutput, Loc.T("Val.OutputMissing")));
        }
        else if (kind == SourceKind.Folder && IsInside(normalized.OutputFolder, normalized.SourcePath))
        {
            errors.Add(new ValidationError(FieldOutput, Loc.T("Val.OutputInsideSource")));
        }

        if (kind == SourceKind.Folder && !string.IsNullOrEmpty(normalized.TemporaryFolder) && IsInside(normalized.TemporaryFolder, normalized.SourcePath))
        {
            errors.Add(new ValidationError(FieldTemporary, Loc.T("Val.TempInsideSource")));
        }

        if (!ContentIdHelper.IsValid(normalized.ContentId))
        {
            errors.Add(new ValidationError(FieldContentId, Loc.F("Val.ContentId", ContentIdHelper.Format)));
        }

        if (normalized.Passcode.Length != BuildRequest.PasscodeLength || normalized.Passcode.Any(c => c > 127 || char.IsControl(c)))
        {
            errors.Add(new ValidationError(FieldPasscode, Loc.T("Val.Passcode")));
        }

        if (!VersionHelper.TryCanonicalize(normalized.Version, out _))
        {
            errors.Add(new ValidationError(FieldVersion, Loc.T("Val.Version")));
        }

        if (normalized.KrakenLevel is < BuildRequest.MinKrakenLevel or > BuildRequest.MaxKrakenLevel)
        {
            errors.Add(new ValidationError(FieldKrakenLevel, Loc.T("Val.KrakenLevel")));
        }

        if (normalized.Threads is < 0 or > BuildRequest.MaxThreads)
        {
            errors.Add(new ValidationError(FieldThreads, Loc.T("Val.Threads")));
        }

        if (normalized.PlayGoChunks is < BuildRequest.MinPlayGoChunks or > BuildRequest.MaxPlayGoChunks)
        {
            errors.Add(new ValidationError(FieldPlayGo, Loc.T("Val.PlayGo")));
        }

        if (normalized.SdkMajorOverride is { } sdk && sdk is < BuildRequest.MinSdkMajor or > BuildRequest.MaxSdkMajor)
        {
            errors.Add(new ValidationError(FieldSdk, Loc.T("Val.Sdk")));
        }

        if (normalized.KrakenBackend == KrakenBackendKind.PublishingTools)
        {
            if (!PublishingToolsLocator.IsSupportedPlatform)
            {
                errors.Add(new ValidationError(FieldPublishingTools, Loc.T("Val.PubToolsPlatform")));
            }
            else if (PublishingToolsLocator.Find(normalized.PublishingToolsPath) == null)
            {
                errors.Add(new ValidationError(FieldPublishingTools, Loc.T("Val.PubToolsMissing")));
            }
        }
        else if (!string.IsNullOrEmpty(normalized.PublishingToolsPath) && normalized.KrakenBackend == KrakenBackendKind.Auto &&
                 PublishingToolsLocator.IsSupportedPlatform && !File.Exists(normalized.PublishingToolsPath))
        {
            errors.Add(new ValidationError(FieldPublishingTools, Loc.T("Val.PubToolsPath")));
        }

        return errors;
    }

    /// <summary>Quyết định backend Kraken thật sự sẽ dùng cho request.</summary>
    public static KrakenBackendKind ResolveBackend(BuildRequest request, out string? publishingToolsPath)
    {
        publishingToolsPath = null;
        switch (request.KrakenBackend)
        {
            case KrakenBackendKind.Uncompressed:
                return KrakenBackendKind.Uncompressed;
            case KrakenBackendKind.BuiltIn:
                return KrakenBackendKind.BuiltIn;
            case KrakenBackendKind.PublishingTools:
                publishingToolsPath = PublishingToolsLocator.Find(request.PublishingToolsPath);
                return KrakenBackendKind.PublishingTools;
            default:
                if (PublishingToolsLocator.IsSupportedPlatform)
                {
                    publishingToolsPath = PublishingToolsLocator.Find(request.PublishingToolsPath);
                    if (publishingToolsPath != null)
                    {
                        return KrakenBackendKind.PublishingTools;
                    }
                }

                return KrakenBackendKind.BuiltIn;
        }
    }

    public static bool IsInside(string candidate, string container)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(container))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var inner = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var outer = Path.TrimEndingDirectorySeparator(Path.GetFullPath(container));
        if (string.Equals(inner, outer, comparison))
        {
            return true;
        }

        return (inner + Path.DirectorySeparatorChar).StartsWith(outer + Path.DirectorySeparatorChar, comparison);
    }

    private static string FullPathOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception)
        {
            return path.Trim();
        }
    }
}
