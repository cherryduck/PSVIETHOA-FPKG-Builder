using PsViethoa.FpkgBuilder.Core.ExFat;

namespace PsViethoa.FpkgBuilder.Core.Services;

public enum SourceKind
{
    None,
    Folder,
    ExFatImage,
}

/// <summary>Mô tả nguồn đã chọn: thư mục hoặc ảnh exFAT (kèm thư mục ứng dụng bên trong ảnh).</summary>
public sealed record SourceInfo(SourceKind Kind, string Path, string AppRootInImage, string? VolumeLabel)
{
    public bool IsExFat => Kind == SourceKind.ExFatImage;

    public string DisplayName => System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(Path));
}

/// <summary>Nhận diện loại nguồn và tìm thư mục ứng dụng (chứa sce_sys) trong thư mục hoặc ảnh exFAT.</summary>
public static class SourceLocator
{
    public const int MaxImageSearchDepth = 3;

    public static SourceKind Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return SourceKind.None;
        }

        if (Directory.Exists(path))
        {
            return SourceKind.Folder;
        }

        if (File.Exists(path) && (HasExFatExtension(path) || ExFatImage.IsExFatFile(path)))
        {
            return SourceKind.ExFatImage;
        }

        return SourceKind.None;
    }

    public static bool HasExFatExtension(string path) =>
        string.Equals(Path.GetExtension(path), ExFatImage.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Phân giải nguồn; với ảnh exFAT sẽ mở ảnh để tìm thư mục ứng dụng. Ném lỗi nếu không hợp lệ.</summary>
    public static SourceInfo Resolve(string path)
    {
        switch (Detect(path))
        {
            case SourceKind.Folder:
                return new SourceInfo(SourceKind.Folder, Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), string.Empty, null);
            case SourceKind.ExFatImage:
            {
                using var image = ExFatImage.Open(path);
                var appRoot = FindAppRoot(image) ?? throw new InvalidDataException(Localization.Loc.T("Val.ExFatNoApp"));
                return new SourceInfo(SourceKind.ExFatImage, Path.GetFullPath(path), appRoot.Path.TrimStart('/'), image.VolumeLabel);
            }
            default:
                throw new FileNotFoundException(Localization.Loc.T("Val.SourceMissing"), path);
        }
    }

    /// <summary>Tìm thư mục ứng dụng trong ảnh: ưu tiên gốc, rồi tìm dần theo chiều sâu (bỏ qua thư mục rác).</summary>
    public static ExFatEntry? FindAppRoot(ExFatImage image)
    {
        var level = new List<ExFatEntry> { image.Root };
        for (var depth = 0; depth <= MaxImageSearchDepth && level.Count > 0; depth++)
        {
            var next = new List<ExFatEntry>();
            foreach (var directory in level)
            {
                var children = image.Enumerate(directory).ToList();
                if (children.Any(c => c.IsDirectory && string.Equals(c.Name, "sce_sys", StringComparison.OrdinalIgnoreCase)))
                {
                    return directory;
                }

                next.AddRange(children
                    .Where(c => c.IsDirectory && !JunkFileFinder.IsJunkDirectoryName(c.Name))
                    .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase));
            }

            level = next;
        }

        return null;
    }

    /// <summary>Mở ảnh và trả về mục thư mục ứng dụng tương ứng với SourceInfo.</summary>
    public static ExFatEntry ResolveAppRoot(ExFatImage image, SourceInfo source)
    {
        if (string.IsNullOrEmpty(source.AppRootInImage))
        {
            return image.Root;
        }

        return image.Find(source.AppRootInImage) ?? throw new InvalidDataException(Localization.Loc.T("Val.ExFatNoApp"));
    }
}
