using System.Text.RegularExpressions;
using LibProsperoPkg.GP5;
using PsViethoa.FpkgBuilder.Core.Localization;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Một tệp mà dự án GP5 sẽ đóng vào gói: đường dẫn trên đĩa, đường dẫn đích trong gói (dùng '/') và kích thước.</summary>
public sealed record Gp5Entry(string SourcePath, string DestinationPath, long Length);

/// <summary>
/// Đọc tệp dự án GP5 (*.gp5) qua LibProsperoPkg và phân giải các đường dẫn quan trọng (thư mục gốc, param.json, icon,
/// PlayGo) cùng danh sách tệp sẽ được đóng gói — phục vụ metadata, thống kê và kiểm tra trước khi tạo gói.
/// Bố cục Normal: đi đệ quy thư mục rootdir (áp dụng global_exclude, dir_exclude, file_exclude) + các mục overlay.
/// Bố cục Flat: chỉ những tệp/thư mục được liệt kê tường minh. Đường dẫn tương đối tính từ thư mục chứa tệp .gp5.
/// Mặt nạ (mask) là danh sách wildcard cách nhau bằng ';' (* và ?), so với TÊN mục, không phân biệt hoa thường.
/// </summary>
public sealed class Gp5ProjectInfo
{
    private const string SceSys = "sce_sys";
    private const string ParamJsonDestination = "sce_sys/param.json";
    private const string IconDestination = "sce_sys/icon0.png";
    private const string EbootDestination = "eboot.bin";

    private static readonly EnumerationOptions ShallowOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private readonly Gp5Project _project;
    private readonly MaskSet _globalExclude;

    private Gp5ProjectInfo(string projectPath, Gp5Project project)
    {
        _project = project;
        ProjectPath = projectPath;
        ProjectDirectory = Path.GetDirectoryName(projectPath) ?? projectPath;
        Layout = project.Layout.ToString();
        VolumeType = project.Volume?.Type.ToString() ?? string.Empty;
        Passcode = string.IsNullOrWhiteSpace(project.Volume?.Package?.Passcode) ? null : project.Volume!.Package!.Passcode;
        _globalExclude = new MaskSet(project.GlobalExclude);

        if (project.Layout == Gp5Layout.Flat)
        {
            var param = FindFlatFile(ParamJsonDestination);
            ParamJsonPath = param;
            RootFolder = param != null ? Path.GetDirectoryName(Path.GetDirectoryName(param)) : null;
            IconPath = FindFlatFile(IconDestination);
            HasEboot = Exists(FindFlatFile(EbootDestination));
            HasPlayGoChunk = Exists(FindFlatFile("sce_sys/playgo-chunk.dat"));
            HasPlayGoHashTable = Exists(FindFlatFile("sce_sys/playgo-hash-table.dat"));
            HasPlayGoFicm = Exists(FindFlatFile("sce_sys/playgo-ficm.dat"));
            HasPlayGoScenario = Exists(FindFlatFile("sce_sys/playgo-scenario.json"));
            MissingFileCount = CountMissing(project.Files) + CountMissing(project.Folders);
        }
        else
        {
            var rootSource = project.RootDir?.SourcePath;
            RootFolder = string.IsNullOrWhiteSpace(rootSource) ? ProjectDirectory : ResolvePath(rootSource);
            var sceSys = Path.Combine(RootFolder, SceSys);
            ParamJsonPath = Path.Combine(sceSys, "param.json");
            IconPath = Path.Combine(sceSys, "icon0.png");
            HasEboot = File.Exists(Path.Combine(RootFolder, EbootDestination));
            HasPlayGoChunk = File.Exists(Path.Combine(sceSys, "playgo-chunk.dat"));
            HasPlayGoHashTable = File.Exists(Path.Combine(sceSys, "playgo-hash-table.dat"));
            HasPlayGoFicm = File.Exists(Path.Combine(sceSys, "playgo-ficm.dat"));
            HasPlayGoScenario = File.Exists(Path.Combine(sceSys, "playgo-scenario.json"));
            MissingFileCount = CountMissing(project.RootDir?.Files) + CountMissing(project.RootDir?.Directories);
        }
    }

    /// <summary>Đường dẫn đầy đủ của tệp .gp5.</summary>
    public string ProjectPath { get; }

    /// <summary>Thư mục chứa tệp .gp5 — gốc để phân giải mọi đường dẫn tương đối trong dự án.</summary>
    public string ProjectDirectory { get; }

    /// <summary>"Normal" (rootdir đi đệ quy) hoặc "Flat" (liệt kê tường minh).</summary>
    public string Layout { get; }

    public bool IsFlat => _project.Layout == Gp5Layout.Flat;

    /// <summary>Loại volume: prospero_app, prospero_patch, prospero_ac, prospero_al (rỗng nếu thiếu).</summary>
    public string VolumeType { get; }

    /// <summary>Passcode ghi trong dự án (null nếu không có).</summary>
    public string? Passcode { get; }

    /// <summary>
    /// Thư mục ứng dụng: Normal = rootdir/src_path đã phân giải; Flat = thư mục chứa sce_sys của mục param.json
    /// (null khi dự án Flat không liệt kê sce_sys/param.json).
    /// </summary>
    public string? RootFolder { get; }

    /// <summary>Đường dẫn param.json mong đợi (Normal) hoặc được liệt kê (Flat, null nếu không có). Có thể chưa tồn tại trên đĩa.</summary>
    public string? ParamJsonPath { get; }

    /// <summary>Đường dẫn icon0.png mong đợi/được liệt kê (null nếu dự án Flat không có). Có thể chưa tồn tại trên đĩa.</summary>
    public string? IconPath { get; }

    public bool HasEboot { get; }

    public bool HasPlayGoChunk { get; }

    public bool HasPlayGoHashTable { get; }

    public bool HasPlayGoFicm { get; }

    public bool HasPlayGoScenario { get; }

    /// <summary>Số mục được liệt kê tường minh (tệp, hoặc thư mục nguồn) nhưng không tồn tại trên đĩa; các mục này bị bỏ qua khi liệt kê.</summary>
    public int MissingFileCount { get; }

    /// <summary>Đọc và phân giải tệp dự án. Ném InvalidDataException (Val.Gp5Invalid + chi tiết) nếu tệp không phải GP5 hợp lệ.</summary>
    public static Gp5ProjectInfo Load(string gp5Path)
    {
        var fullPath = Path.GetFullPath(gp5Path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(Loc.T("Val.SourceMissing"), fullPath);
        }

        Gp5Project project;
        try
        {
            project = Gp5Project.ReadFrom(fullPath) ?? throw new InvalidDataException("empty project");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var detail = ex.InnerException != null ? ex.Message + " " + ex.InnerException.Message : ex.Message;
            throw new InvalidDataException(Loc.T("Val.Gp5Invalid") + " " + detail, ex);
        }

        return new Gp5ProjectInfo(fullPath, project);
    }

    /// <summary>
    /// Liệt kê các tệp sẽ được đóng gói (đích dùng '/'). Tệp/thư mục được liệt kê nhưng thiếu trên đĩa bị bỏ qua;
    /// mục trùng đường dẫn đích chỉ trả về một lần (mục tường minh được ưu tiên trước cây thư mục đi đệ quy).
    /// </summary>
    public IEnumerable<Gp5Entry> EnumerateFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsFlat)
        {
            foreach (var entry in EnumerateExplicitFiles(_project.Files, string.Empty, seen))
            {
                yield return entry;
            }

            foreach (var directory in _project.Folders ?? [])
            {
                foreach (var entry in EnumerateDirectory(directory, string.Empty, seen))
                {
                    yield return entry;
                }
            }

            yield break;
        }

        var root = _project.RootDir;
        if (root != null)
        {
            foreach (var entry in EnumerateExplicitFiles(root.Files, string.Empty, seen))
            {
                yield return entry;
            }

            foreach (var directory in root.Directories ?? [])
            {
                foreach (var entry in EnumerateDirectory(directory, string.Empty, seen))
                {
                    yield return entry;
                }
            }
        }

        if (RootFolder != null && Directory.Exists(RootFolder))
        {
            foreach (var entry in Walk(RootFolder, string.Empty, new MaskSet(root?.DirExclude), new MaskSet(root?.FileExclude), MaskSet.Empty, seen))
            {
                yield return entry;
            }
        }
    }

    // ===================== Liệt kê =====================

    private IEnumerable<Gp5Entry> EnumerateExplicitFiles(IEnumerable<Gp5File>? files, string prefix, HashSet<string> seen)
    {
        foreach (var file in files ?? [])
        {
            var destination = JoinDestination(prefix, file.DestinationPath);
            var source = SourceOf(file, destination);
            if (string.IsNullOrEmpty(destination))
            {
                destination = Path.GetFileName(source);
            }

            long length;
            try
            {
                var info = new FileInfo(source);
                if (!info.Exists)
                {
                    continue;
                }

                length = info.Length;
            }
            catch (Exception)
            {
                continue;
            }

            if (seen.Add(destination))
            {
                yield return new Gp5Entry(source, destination, length);
            }
        }
    }

    private IEnumerable<Gp5Entry> EnumerateDirectory(Gp5Dir directory, string parentPrefix, HashSet<string> seen)
    {
        var prefix = JoinDestination(parentPrefix, directory.DestinationPath);
        foreach (var entry in EnumerateExplicitFiles(directory.Files, prefix, seen))
        {
            yield return entry;
        }

        foreach (var child in directory.Directories ?? [])
        {
            foreach (var entry in EnumerateDirectory(child, prefix, seen))
            {
                yield return entry;
            }
        }

        if (directory.Virtual || string.IsNullOrWhiteSpace(directory.SourcePath))
        {
            yield break;
        }

        var source = ResolvePath(directory.SourcePath);
        if (!Directory.Exists(source))
        {
            yield break;
        }

        foreach (var entry in Walk(source, prefix, new MaskSet(directory.DirExclude), new MaskSet(directory.FileExclude), new MaskSet(directory.FileInclude), seen))
        {
            yield return entry;
        }
    }

    /// <summary>Đi đệ quy một thư mục nguồn, ánh xạ lên tiền tố đích, áp dụng mặt nạ theo tên mục.</summary>
    private IEnumerable<Gp5Entry> Walk(string directory, string prefix, MaskSet dirExclude, MaskSet fileExclude, MaskSet fileInclude, HashSet<string> seen)
    {
        var pending = new Stack<(string Directory, string Prefix)>();
        pending.Push((directory, prefix));
        while (pending.Count > 0)
        {
            var (current, currentPrefix) = pending.Pop();
            List<FileSystemInfo> children;
            try
            {
                children = new DirectoryInfo(current)
                    .EnumerateFileSystemInfos("*", ShallowOptions)
                    .OrderBy(c => c.Name, StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception)
            {
                continue;
            }

            // Đẩy thư mục con theo thứ tự ngược để pop ra vẫn theo thứ tự tên.
            for (var i = children.Count - 1; i >= 0; i--)
            {
                var child = children[i];
                if ((child.Attributes & FileAttributes.Directory) == 0)
                {
                    continue;
                }

                if (dirExclude.Matches(child.Name) || _globalExclude.Matches(child.Name))
                {
                    continue;
                }

                pending.Push((child.FullName, JoinDestination(currentPrefix, child.Name)));
            }

            foreach (var child in children)
            {
                if ((child.Attributes & FileAttributes.Directory) != 0 || child is not FileInfo file)
                {
                    continue;
                }

                if (!fileInclude.IsEmpty && !fileInclude.Matches(file.Name))
                {
                    continue;
                }

                if (fileExclude.Matches(file.Name) || _globalExclude.Matches(file.Name))
                {
                    continue;
                }

                long length;
                try
                {
                    length = file.Length;
                }
                catch (Exception)
                {
                    continue;
                }

                var destination = JoinDestination(currentPrefix, file.Name);
                if (seen.Add(destination))
                {
                    yield return new Gp5Entry(file.FullName, destination, length);
                }
            }
        }
    }

    // ===================== Tra cứu bố cục Flat =====================

    /// <summary>Tìm đường dẫn nguồn của một tệp đích (vd. "sce_sys/param.json") trong dự án Flat: mục file tường minh trước, rồi các mục folder.</summary>
    private string? FindFlatFile(string destination)
    {
        foreach (var file in _project.Files ?? [])
        {
            var fileDestination = NormalizeDestination(file.DestinationPath);
            if (DestinationEquals(fileDestination, destination))
            {
                return SourceOf(file, fileDestination);
            }
        }

        foreach (var directory in _project.Folders ?? [])
        {
            if (FindInDirectory(directory, string.Empty, destination) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private string? FindInDirectory(Gp5Dir directory, string parentPrefix, string destination)
    {
        var prefix = JoinDestination(parentPrefix, directory.DestinationPath);
        foreach (var file in directory.Files ?? [])
        {
            var fileDestination = JoinDestination(prefix, file.DestinationPath);
            if (DestinationEquals(fileDestination, destination))
            {
                return SourceOf(file, fileDestination);
            }
        }

        foreach (var child in directory.Directories ?? [])
        {
            if (FindInDirectory(child, prefix, destination) is { } found)
            {
                return found;
            }
        }

        if (directory.Virtual || string.IsNullOrWhiteSpace(directory.SourcePath) || !TryGetRemainder(destination, prefix, out var remainder))
        {
            return null;
        }

        var candidate = Path.Combine(ResolvePath(directory.SourcePath), remainder.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(candidate) && PassesMasks(directory, remainder) ? candidate : null;
    }

    /// <summary>Kiểm tra một đường dẫn tương đối bên trong thư mục nguồn có vượt qua các mặt nạ của mục folder không.</summary>
    private bool PassesMasks(Gp5Dir directory, string relativePath)
    {
        var dirExclude = new MaskSet(directory.DirExclude);
        var fileExclude = new MaskSet(directory.FileExclude);
        var fileInclude = new MaskSet(directory.FileInclude);
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (dirExclude.Matches(segments[i]) || _globalExclude.Matches(segments[i]))
            {
                return false;
            }
        }

        var name = segments.Length > 0 ? segments[^1] : relativePath;
        if (!fileInclude.IsEmpty && !fileInclude.Matches(name))
        {
            return false;
        }

        return !fileExclude.Matches(name) && !_globalExclude.Matches(name);
    }

    // ===================== Đếm mục thiếu =====================

    private int CountMissing(IEnumerable<Gp5File>? files)
    {
        var missing = 0;
        foreach (var file in files ?? [])
        {
            var destination = NormalizeDestination(file.DestinationPath);
            if (!File.Exists(SourceOf(file, destination)))
            {
                missing++;
            }
        }

        return missing;
    }

    private int CountMissing(IEnumerable<Gp5Dir>? directories)
    {
        var missing = 0;
        foreach (var directory in directories ?? [])
        {
            missing += CountMissing(directory.Files) + CountMissing(directory.Directories);
            if (!directory.Virtual && !string.IsNullOrWhiteSpace(directory.SourcePath) && !Directory.Exists(ResolvePath(directory.SourcePath)))
            {
                missing++;
            }
        }

        return missing;
    }

    // ===================== Đường dẫn =====================

    /// <summary>Nguồn của một mục file: src_path (tương đối tính từ thư mục .gp5); thiếu src_path thì lấy theo đường dẫn đích.</summary>
    private string SourceOf(Gp5File file, string destination) =>
        ResolvePath(string.IsNullOrWhiteSpace(file.SourcePath) ? destination : file.SourcePath);

    /// <summary>Phân giải đường dẫn trong dự án: tuyệt đối giữ nguyên, tương đối tính từ thư mục chứa tệp .gp5.</summary>
    private string ResolvePath(string path)
    {
        var normalized = path.Trim().Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        try
        {
            return Path.IsPathRooted(normalized)
                ? Path.GetFullPath(normalized)
                : Path.GetFullPath(Path.Combine(ProjectDirectory, normalized));
        }
        catch (Exception)
        {
            return normalized;
        }
    }

    private static bool Exists(string? path) => path != null && File.Exists(path);

    /// <summary>Chuẩn hoá đường dẫn đích: '\' → '/', bỏ "./" và '/' ở đầu, bỏ '/' ở cuối.</summary>
    private static string NormalizeDestination(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return string.Empty;
        }

        var text = destination.Trim().Replace('\\', '/');
        while (text.Contains("//", StringComparison.Ordinal))
        {
            text = text.Replace("//", "/", StringComparison.Ordinal);
        }

        while (text.StartsWith("./", StringComparison.Ordinal))
        {
            text = text[2..];
        }

        return text.Trim('/');
    }

    /// <summary>Ghép tiền tố đích với đường dẫn con; nếu con đã mang sẵn tiền tố thì giữ nguyên.</summary>
    private static string JoinDestination(string prefix, string? child)
    {
        var normalized = NormalizeDestination(child);
        if (string.IsNullOrEmpty(prefix))
        {
            return normalized;
        }

        if (string.IsNullOrEmpty(normalized) || normalized == ".")
        {
            return prefix;
        }

        if (normalized.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return prefix + "/" + normalized;
    }

    private static bool DestinationEquals(string a, string b) =>
        string.Equals(NormalizeDestination(a), NormalizeDestination(b), StringComparison.OrdinalIgnoreCase);

    private static bool TryGetRemainder(string destination, string prefix, out string remainder)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            remainder = destination;
            return true;
        }

        if (destination.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            remainder = destination[(prefix.Length + 1)..];
            return true;
        }

        remainder = string.Empty;
        return false;
    }

    // ===================== Mặt nạ =====================

    /// <summary>Danh sách wildcard cách nhau bằng ';' (* và ?), so với tên mục, không phân biệt hoa thường.</summary>
    private sealed class MaskSet
    {
        public static readonly MaskSet Empty = new(null);

        private readonly Regex[] _patterns;

        public MaskSet(string? masks)
        {
            _patterns = (masks ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ToRegex)
                .ToArray();
        }

        public bool IsEmpty => _patterns.Length == 0;

        public bool Matches(string name)
        {
            foreach (var pattern in _patterns)
            {
                if (pattern.IsMatch(name))
                {
                    return true;
                }
            }

            return false;
        }

        private static Regex ToRegex(string mask) =>
            new("^" + Regex.Escape(mask).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}
