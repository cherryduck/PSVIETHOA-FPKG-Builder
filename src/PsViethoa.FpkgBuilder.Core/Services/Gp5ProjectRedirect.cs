using LibProsperoPkg.GP5;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Bản sao của dự án GP5, ghi ra thư mục tạm, trỏ rootdir sang thư mục gương. Tệp .gp5 của người dùng chỉ được đọc, không bao giờ
/// bị sửa — đó là quy tắc bất di bất dịch của công cụ.
/// <para>
/// Mọi đường dẫn tương đối trong dự án được tính từ thư mục chứa tệp .gp5, nên bản sao nằm chỗ khác phải đổi hết sang đường dẫn
/// tuyệt đối, nếu không thư viện sẽ tìm tệp ở sai chỗ.
/// </para>
/// </summary>
public sealed class Gp5ProjectRedirect : IDisposable
{
    private readonly string _folder;
    private bool _removed;

    private Gp5ProjectRedirect(string path, string folder)
    {
        Path = path;
        _folder = folder;
    }

    /// <summary>Tệp .gp5 tạm mà thư viện sẽ đọc.</summary>
    public string Path { get; }

    /// <summary>
    /// Ghi bản sao dự án vào thư mục tạm. <paramref name="mirrorFolder"/> là thư mục gương thay cho rootdir (null nếu không cần
    /// bỏ hay sửa gì — khi đó vẫn tạo bản sao để tệp gốc chắc chắn không bị đụng tới). Trả về null nếu không đọc/ghi được.
    /// </summary>
    public static Gp5ProjectRedirect? Create(BuildRequest request, SourceInfo source, string? appFolder, string? mirrorFolder, Action<LogEntry> log)
    {
        try
        {
            var project = Gp5Project.ReadFrom(source.Path) ?? throw new InvalidDataException(Loc.T("Val.Gp5Invalid"));
            var projectDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(source.Path))!;
            var folder = System.IO.Path.Combine(request.TemporaryFolder, "gp5-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);

            Absolutise(project, projectDirectory);

            // Chuyển MỌI đường dẫn nằm trong thư mục ứng dụng sang thư mục gương. Bố cục Flat không có <rootdir>, mà liệt kê
            // từng tệp, nên nếu chỉ đổi rootdir thì gương bị bỏ qua hoàn toàn và gói vẫn mang param.json gốc lẫn tệp cần bỏ.
            if (mirrorFolder != null && appFolder != null)
            {
                Redirect(project, System.IO.Path.GetFullPath(appFolder), mirrorFolder);

                // Mục liệt kê tường minh trỏ tới tệp đã bị bỏ khỏi gương (bộ playgo*, tàn dư emu) phải bỏ hẳn khỏi bản sao
                // dự án, nếu không thư viện dừng với lỗi "GP5 source file was not found".
                Prune(project, mirrorFolder);
            }

            var path = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(source.Path));
            Gp5Project.WriteTo(project, path);
            log(new LogEntry(LogLevel.Info, Loc.F("Plan.Gp5Redirect", path)));
            return new Gp5ProjectRedirect(path, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            log(new LogEntry(LogLevel.Warning, Loc.F("Plan.Gp5CleanupFailed", ex.Message)));
            return null;
        }
    }

    /// <summary>Bỏ các mục tường minh trỏ tới tệp không còn trong gương (đã được cố tình loại khỏi gói).</summary>
    private static void Prune(Gp5Project project, string mirrorFolder)
    {
        if (project.RootDir is { } root)
        {
            root.Files = Prune(root.Files, mirrorFolder);
            Prune(root.Directories, mirrorFolder);
        }

        project.Files = Prune(project.Files, mirrorFolder);
        Prune(project.Folders, mirrorFolder);
    }

    private static List<Gp5File>? Prune(List<Gp5File>? files, string mirrorFolder)
    {
        if (files == null)
        {
            return files;
        }

        return files
            .Where(file => file.SourcePath == null
                           || !file.SourcePath.StartsWith(mirrorFolder, StringComparison.OrdinalIgnoreCase)
                           || File.Exists(file.SourcePath))
            .ToList();
    }

    private static void Prune(List<Gp5Dir>? directories, string mirrorFolder)
    {
        foreach (var directory in directories ?? [])
        {
            directory.Files = Prune(directory.Files, mirrorFolder);
            Prune(directory.Directories, mirrorFolder);
        }
    }

    /// <summary>Đổi mọi đường dẫn trỏ vào thư mục ứng dụng sang thư mục gương (áp dụng cho cả rootdir lẫn từng mục Flat).</summary>
    private static void Redirect(Gp5Project project, string appFolder, string mirrorFolder)
    {
        if (project.RootDir is { } root)
        {
            root.SourcePath = Remap(root.SourcePath, appFolder, mirrorFolder);
            Redirect(root.Files, appFolder, mirrorFolder);
            Redirect(root.Directories, appFolder, mirrorFolder);
        }

        Redirect(project.Files, appFolder, mirrorFolder);
        Redirect(project.Folders, appFolder, mirrorFolder);
    }

    private static void Redirect(List<Gp5File>? files, string appFolder, string mirrorFolder)
    {
        foreach (var file in files ?? [])
        {
            file.SourcePath = Remap(file.SourcePath, appFolder, mirrorFolder);
        }
    }

    private static void Redirect(List<Gp5Dir>? directories, string appFolder, string mirrorFolder)
    {
        foreach (var directory in directories ?? [])
        {
            directory.SourcePath = Remap(directory.SourcePath, appFolder, mirrorFolder);
            Redirect(directory.Files, appFolder, mirrorFolder);
            Redirect(directory.Directories, appFolder, mirrorFolder);
        }
    }

    /// <summary>Đường dẫn nằm trong thư mục ứng dụng thì trỏ sang chỗ tương ứng trong gương; ngoài ra giữ nguyên.</summary>
    private static string? Remap(string? path, string appFolder, string mirrorFolder)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var full = path.Trim();
        if (string.Equals(full, appFolder, comparison))
        {
            return mirrorFolder;
        }

        var prefix = appFolder.EndsWith(System.IO.Path.DirectorySeparatorChar) ? appFolder : appFolder + System.IO.Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, comparison)
            ? System.IO.Path.Combine(mirrorFolder, full[prefix.Length..])
            : full;
    }

    /// <summary>Đổi mọi đường dẫn tương đối trong dự án sang tuyệt đối, tính từ thư mục chứa tệp .gp5 gốc.</summary>
    private static void Absolutise(Gp5Project project, string projectDirectory)
    {
        if (project.RootDir is { } root)
        {
            root.SourcePath = Resolve(root.SourcePath, projectDirectory) ?? projectDirectory;
            Absolutise(root.Files, projectDirectory);
            Absolutise(root.Directories, projectDirectory);
        }

        Absolutise(project.Files, projectDirectory);
        Absolutise(project.Folders, projectDirectory);
    }

    private static void Absolutise(List<Gp5File>? files, string projectDirectory)
    {
        foreach (var file in files ?? [])
        {
            // Mục thiếu src_path lấy theo đường dẫn đích, nên phải ghi rõ ra trước khi dự án đổi chỗ.
            file.SourcePath = Resolve(file.SourcePath, projectDirectory) ?? Resolve(file.DestinationPath, projectDirectory);
        }
    }

    private static void Absolutise(List<Gp5Dir>? directories, string projectDirectory)
    {
        foreach (var directory in directories ?? [])
        {
            if (!directory.Virtual && !string.IsNullOrWhiteSpace(directory.SourcePath))
            {
                directory.SourcePath = Resolve(directory.SourcePath, projectDirectory);
            }

            Absolutise(directory.Files, projectDirectory);
            Absolutise(directory.Directories, projectDirectory);
        }
    }

    private static string? Resolve(string? path, string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim();

        // Dự án .gp5 thường được tạo trên Windows. Đường dẫn kiểu "C:\\game" hay "\\\\máy\\thư mục" là tuyệt đối, nhưng
        // Path.IsPathRooted trên macOS/Linux trả về false và sẽ ghép nhầm vào thư mục dự án.
        var windowsRooted = trimmed.Length >= 2 && ((char.IsLetter(trimmed[0]) && trimmed[1] == ':') || trimmed.StartsWith(@"\\", StringComparison.Ordinal));
        var normalized = trimmed.Replace('\\', System.IO.Path.DirectorySeparatorChar).Replace('/', System.IO.Path.DirectorySeparatorChar);
        try
        {
            if (windowsRooted && !OperatingSystem.IsWindows())
            {
                // Giữ nguyên đường dẫn Windows: không ghép vào thư mục dự án để khỏi tạo ra đường dẫn vô nghĩa.
                return trimmed;
            }

            return System.IO.Path.IsPathRooted(normalized)
                ? System.IO.Path.GetFullPath(normalized)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDirectory, normalized));
        }
        catch (Exception)
        {
            return normalized;
        }
    }

    public void Dispose()
    {
        if (_removed)
        {
            return;
        }

        _removed = true;
        BuildEngine.TryDeleteDirectory(_folder);
    }
}
