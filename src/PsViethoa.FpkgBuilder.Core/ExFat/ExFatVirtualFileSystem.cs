using System.Collections.Concurrent;
using System.Security.AccessControl;
using DokanNet;
using PsViethoa.FpkgBuilder.Core.Services;
using DokanFileAccess = DokanNet.FileAccess;

namespace PsViethoa.FpkgBuilder.Core.ExFat;

/// <summary>
/// Hệ thống tệp ảo chỉ-đọc (Dokan, Windows) phơi thư mục ứng dụng trong ảnh exFAT (.exfat hoặc .ffpfsc) thành một ổ đĩa:
/// thư viện đọc thẳng từ ảnh, không sao chép. Tệp rác hệ điều hành được ẩn, và có thể "đè" vài tệp bằng dữ liệu trong bộ nhớ
/// (ép DRM trong sce_sys/param.json) mà không đụng vào ảnh gốc. Gốc ổ ảo chứa một thư mục bọc trỏ tới thư mục ứng dụng, nên
/// thư mục nguồn đưa cho thư viện không bao giờ là gốc ổ đĩa. Lớp này thuần .NET nên kiểm thử được ở mọi hệ điều hành; chỉ
/// việc gắn ổ (<see cref="DokanImageMounter"/>) cần driver Dokan.
/// </summary>
public sealed class ExFatVirtualFileSystem : IDokanOperations
{
    public const string FileSystemName = "exFAT";

    private const int MaxLabelLength = 32;
    private const uint MaxComponentLength = 255;

    /// <summary>STATUS_FILE_IS_A_DIRECTORY — không có tên trong enum NtStatus của DokanNet 2.3.</summary>
    private const NtStatus FileIsADirectoryStatus = (NtStatus)0xC00000BA;

    private const DokanFileAccess WriteAccessMask =
        DokanFileAccess.WriteData | DokanFileAccess.AppendData | DokanFileAccess.Delete | DokanFileAccess.DeleteChild |
        DokanFileAccess.WriteExtendedAttributes | DokanFileAccess.ChangePermissions | DokanFileAccess.SetOwnership |
        DokanFileAccess.GenericWrite | DokanFileAccess.GenericAll;

    private readonly ExFatImage _image;
    private readonly ExFatEntry _appRoot;
    private readonly string? _wrapper;
    private readonly bool _hideJunk;
    private readonly Dictionary<string, byte[]> _overlays;
    private readonly HashSet<string> _hidden;
    private readonly ConcurrentDictionary<string, Listing> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Node?> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Node _root;
    private readonly DateTime _fallbackTime;
    private readonly string _label;
    private readonly ManualResetEventSlim _mounted = new(false);

    /// <param name="image">Ảnh exFAT đã mở (không thuộc sở hữu của lớp này).</param>
    /// <param name="appRoot">Thư mục ứng dụng trong ảnh (chứa sce_sys/).</param>
    /// <param name="wrapperName">Tên thư mục bọc ở gốc ổ ảo; null/rỗng = phơi thẳng thư mục ứng dụng ở gốc.</param>
    /// <param name="hideJunk">Ẩn tệp/thư mục rác hệ điều hành (.DS_Store, ._*, Thumbs.db…).</param>
    /// <param name="overlays">Tệp đè: khoá là đường dẫn tương đối so với thư mục ứng dụng ("sce_sys/param.json"), giá trị là nội dung.</param>
    /// <param name="volumeLabel">Nhãn ổ ảo.</param>
    /// <param name="hiddenPaths">Đường dẫn (tương đối thư mục ứng dụng) bị ẩn hẳn khỏi ổ ảo, ví dụ tàn dư AMPR emu.</param>
    public ExFatVirtualFileSystem(
        ExFatImage image,
        ExFatEntry appRoot,
        string? wrapperName,
        bool hideJunk,
        IReadOnlyDictionary<string, byte[]>? overlays,
        string? volumeLabel,
        IReadOnlyCollection<string>? hiddenPaths = null)
    {
        _image = image;
        _appRoot = appRoot;
        _wrapper = string.IsNullOrWhiteSpace(wrapperName) ? null : wrapperName.Trim().Trim('/', '\\');
        _hideJunk = hideJunk;
        _overlays = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (overlays != null)
        {
            foreach (var (key, bytes) in overlays)
            {
                var normalized = NormalizePath(key);
                if (normalized.Length > 0)
                {
                    _overlays[normalized] = bytes;
                }
            }
        }

        _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hiddenPaths != null)
        {
            foreach (var path in hiddenPaths)
            {
                var normalized = NormalizePath(path);
                if (normalized.Length > 0)
                {
                    _hidden.Add(normalized);
                }
            }
        }

        _fallbackTime = ReadImageTime(image.Path);
        var label = string.IsNullOrWhiteSpace(volumeLabel) ? "EXFAT" : volumeLabel.Trim();
        _label = label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;
        _root = new Node
        {
            Name = string.Empty,
            VirtualPath = string.Empty,
            IsDirectory = true,
            Entry = _wrapper == null ? appRoot : null,
            Modified = appRoot.Modified,
        };
    }

    /// <summary>Tên thư mục bọc (null khi thư mục ứng dụng nằm ngay gốc ổ ảo).</summary>
    public string? WrapperName => _wrapper;

    /// <summary>Đường dẫn (trong ổ ảo) tới thư mục ứng dụng: tên thư mục bọc, hoặc rỗng.</summary>
    public string SourceRelativePath => _wrapper ?? string.Empty;

    public bool IsMounted => _mounted.IsSet;

    public bool WaitForMount(TimeSpan timeout, CancellationToken cancellationToken) => _mounted.Wait(timeout, cancellationToken);

    // ===================== Đường dẫn =====================

    /// <summary>Chuẩn hoá đường dẫn Dokan ("\a\b" hoặc "a/b") thành "a/b"; gốc = chuỗi rỗng.</summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var unified = path.Replace('\\', '/');
        var parts = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : string.Join('/', parts);
    }

    private static string ParentOf(string normalized)
    {
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    private static string NameOf(string normalized)
    {
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? normalized : normalized[(slash + 1)..];
    }

    private static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "/" + name;

    /// <summary>Đường dẫn tương đối so với thư mục ứng dụng (bỏ thư mục bọc).</summary>
    private string ToAppRelative(string virtualPath)
    {
        if (_wrapper == null)
        {
            return virtualPath;
        }

        if (virtualPath.Length == _wrapper.Length)
        {
            return string.Empty;
        }

        return virtualPath.Length > _wrapper.Length ? virtualPath[(_wrapper.Length + 1)..] : virtualPath;
    }

    // ===================== Cây thư mục =====================

    private sealed class Node
    {
        public required string Name { get; init; }

        public required string VirtualPath { get; init; }

        public required bool IsDirectory { get; init; }

        public long Length { get; init; }

        public DateTime? Modified { get; init; }

        /// <summary>Mục trong ảnh (null với thư mục bọc hoặc tệp đè chưa có trong ảnh).</summary>
        public ExFatEntry? Entry { get; init; }

        /// <summary>Nội dung đè (null = đọc từ ảnh).</summary>
        public byte[]? Overlay { get; init; }
    }

    private sealed class Listing
    {
        public required Dictionary<string, Node> ByName { get; init; }

        public required List<Node> Ordered { get; init; }
    }

    private Node? Lookup(string virtualPath)
    {
        if (virtualPath.Length == 0)
        {
            return _root;
        }

        return _nodes.GetOrAdd(virtualPath, path =>
        {
            var parent = Lookup(ParentOf(path));
            if (parent == null || !parent.IsDirectory)
            {
                return null;
            }

            return GetListing(parent).ByName.TryGetValue(NameOf(path), out var node) ? node : null;
        });
    }

    private Listing GetListing(Node directory) => _listings.GetOrAdd(directory.VirtualPath, _ => BuildListing(directory));

    private Listing BuildListing(Node directory)
    {
        var byName = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Node>();

        if (directory.VirtualPath.Length == 0 && _wrapper != null)
        {
            var wrapper = new Node
            {
                Name = _wrapper,
                VirtualPath = _wrapper,
                IsDirectory = true,
                Entry = _appRoot,
                Modified = _appRoot.Modified ?? _fallbackTime,
            };
            byName[wrapper.Name] = wrapper;
            ordered.Add(wrapper);
            return new Listing { ByName = byName, Ordered = ordered };
        }

        if (directory.Entry == null)
        {
            return new Listing { ByName = byName, Ordered = ordered };
        }

        var appRelative = ToAppRelative(directory.VirtualPath);
        foreach (var child in _image.Enumerate(directory.Entry))
        {
            if (_hideJunk && (child.IsDirectory ? JunkFileFinder.IsJunkDirectoryName(child.Name) : JunkFileFinder.IsJunkFileName(child.Name)))
            {
                continue;
            }

            if (_hidden.Count > 0 && _hidden.Contains(Join(appRelative, child.Name)))
            {
                continue;
            }

            var virtualPath = Join(directory.VirtualPath, child.Name);
            var overlay = !child.IsDirectory && _overlays.TryGetValue(Join(appRelative, child.Name), out var bytes) ? bytes : null;
            var node = new Node
            {
                Name = child.Name,
                VirtualPath = virtualPath,
                IsDirectory = child.IsDirectory,
                Length = child.IsDirectory ? 0 : overlay?.Length ?? child.Length,
                Modified = child.Modified ?? _fallbackTime,
                Entry = child,
                Overlay = overlay,
            };

            // exFAT không phân biệt hoa/thường; nếu ảnh hỏng chứa hai tên chỉ khác hoa/thường thì giữ mục đầu.
            if (byName.TryAdd(node.Name, node))
            {
                ordered.Add(node);
            }
        }

        // Tệp đè chưa tồn tại trong thư mục này (ví dụ param.json thiếu) — thêm như tệp mới.
        foreach (var (key, bytes) in _overlays)
        {
            if (!string.Equals(ParentOf(key), appRelative, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = NameOf(key);
            if (byName.ContainsKey(name))
            {
                continue;
            }

            var node = new Node
            {
                Name = name,
                VirtualPath = Join(directory.VirtualPath, name),
                IsDirectory = false,
                Length = bytes.Length,
                Modified = _fallbackTime,
                Overlay = bytes,
            };
            byName[name] = node;
            ordered.Add(node);
        }

        return new Listing { ByName = byName, Ordered = ordered };
    }

    private FileInformation ToFileInformation(Node node)
    {
        var time = node.Modified ?? _fallbackTime;
        return new FileInformation
        {
            FileName = node.Name,
            Attributes = node.IsDirectory ? FileAttributes.Directory : FileAttributes.ReadOnly,
            Length = node.Length,
            CreationTime = time,
            LastAccessTime = time,
            LastWriteTime = time,
        };
    }

    private static DateTime ReadImageTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch (Exception)
        {
            return DateTime.UtcNow;
        }
    }

    // ===================== Tệp đang mở =====================

    private sealed class OpenFile : IDisposable
    {
        public OpenFile(Node node)
        {
            Node = node;
        }

        public Node Node { get; }

        public object Gate { get; } = new();

        public Stream? Stream { get; set; }

        public void Dispose()
        {
            lock (Gate)
            {
                Stream?.Dispose();
                Stream = null;
            }
        }
    }

    private NtStatus Read(OpenFile open, byte[] buffer, long offset, out int bytesRead)
    {
        bytesRead = 0;
        if (offset < 0)
        {
            return NtStatus.InvalidParameter;
        }

        var node = open.Node;
        if (node.Overlay is { } bytes)
        {
            if (offset >= bytes.Length)
            {
                return NtStatus.Success;
            }

            bytesRead = (int)Math.Min(buffer.Length, bytes.Length - offset);
            Buffer.BlockCopy(bytes, (int)offset, buffer, 0, bytesRead);
            return NtStatus.Success;
        }

        if (node.Entry == null || node.IsDirectory)
        {
            return FileIsADirectoryStatus;
        }

        if (offset >= node.Length)
        {
            return NtStatus.Success;
        }

        try
        {
            lock (open.Gate)
            {
                open.Stream ??= _image.OpenRead(node.Entry);
                open.Stream.Position = offset;
                var total = 0;
                while (total < buffer.Length)
                {
                    var read = open.Stream.Read(buffer, total, buffer.Length - total);
                    if (read <= 0)
                    {
                        break;
                    }

                    total += read;
                }

                bytesRead = total;
            }

            return NtStatus.Success;
        }
        catch (Exception)
        {
            bytesRead = 0;
            return NtStatus.Unsuccessful;
        }
    }

    // ===================== IDokanOperations =====================

    public NtStatus CreateFile(
        string fileName,
        DokanFileAccess access,
        FileShare share,
        FileMode mode,
        FileOptions options,
        FileAttributes attributes,
        IDokanFileInfo info)
    {
        var node = Lookup(NormalizePath(fileName));
        if (node == null)
        {
            // Ổ chỉ đọc: không tạo mới được; mở tệp không tồn tại thì báo thiếu.
            return mode is FileMode.Open or FileMode.Truncate ? NtStatus.ObjectNameNotFound : NtStatus.AccessDenied;
        }

        if (node.IsDirectory)
        {
            if (mode == FileMode.CreateNew)
            {
                return NtStatus.ObjectNameCollision;
            }

            if ((access & (DokanFileAccess.Delete | DokanFileAccess.DeleteChild)) != 0)
            {
                return NtStatus.AccessDenied;
            }

            info.IsDirectory = true;
            return NtStatus.Success;
        }

        if (info.IsDirectory)
        {
            return NtStatus.NotADirectory;
        }

        if (mode == FileMode.CreateNew)
        {
            return NtStatus.ObjectNameCollision;
        }

        if (mode is FileMode.Create or FileMode.Truncate or FileMode.Append || (access & WriteAccessMask) != 0)
        {
            return NtStatus.AccessDenied;
        }

        info.Context = new OpenFile(node);
        return NtStatus.Success;
    }

    public void Cleanup(string fileName, IDokanFileInfo info)
    {
        if (info.Context is OpenFile open)
        {
            open.Dispose();
        }
    }

    public void CloseFile(string fileName, IDokanFileInfo info)
    {
        if (info.Context is OpenFile open)
        {
            open.Dispose();
        }

        info.Context = null;
    }

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
    {
        if (info.Context is OpenFile open)
        {
            return Read(open, buffer, offset, out bytesRead);
        }

        bytesRead = 0;
        var node = Lookup(NormalizePath(fileName));
        if (node == null)
        {
            return NtStatus.ObjectNameNotFound;
        }

        if (node.IsDirectory)
        {
            return FileIsADirectoryStatus;
        }

        using var temporary = new OpenFile(node);
        return Read(temporary, buffer, offset, out bytesRead);
    }

    public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
    {
        bytesWritten = 0;
        return NtStatus.AccessDenied;
    }

    public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
    {
        var node = (info.Context as OpenFile)?.Node ?? Lookup(NormalizePath(fileName));
        if (node == null)
        {
            fileInfo = default;
            return NtStatus.ObjectNameNotFound;
        }

        fileInfo = ToFileInformation(node);
        return NtStatus.Success;
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info) =>
        FindFilesWithPattern(fileName, "*", out files, info);

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
    {
        files = new List<FileInformation>();
        var node = Lookup(NormalizePath(fileName));
        if (node == null)
        {
            return NtStatus.ObjectPathNotFound;
        }

        if (!node.IsDirectory)
        {
            return NtStatus.NotADirectory;
        }

        var all = string.IsNullOrEmpty(searchPattern) || searchPattern == "*";
        foreach (var child in GetListing(node).Ordered)
        {
            if (all || DokanHelper.DokanIsNameInExpression(searchPattern, child.Name, true))
            {
                files.Add(ToFileInformation(child));
            }
        }

        return NtStatus.Success;
    }

    public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info) =>
        NtStatus.Success;

    public NtStatus DeleteFile(string fileName, IDokanFileInfo info) => NtStatus.AccessDenied;

    public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info) => NtStatus.AccessDenied;

    public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info) => NtStatus.AccessDenied;

    public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info) => NtStatus.AccessDenied;

    public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info) => NtStatus.AccessDenied;

    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info) => NtStatus.Success;

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        freeBytesAvailable = 0;
        totalNumberOfBytes = Math.Max(_image.VolumeLengthBytes, 0);
        totalNumberOfFreeBytes = 0;
        return NtStatus.Success;
    }

    public NtStatus GetVolumeInformation(
        out string volumeLabel,
        out FileSystemFeatures features,
        out string fileSystemName,
        out uint maximumComponentLength,
        IDokanFileInfo info)
    {
        volumeLabel = _label;
        features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.UnicodeOnDisk | FileSystemFeatures.ReadOnlyVolume;
        fileSystemName = FileSystemName;
        maximumComponentLength = MaxComponentLength;
        return NtStatus.Success;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
    {
        security = null;
        return NtStatus.NotImplemented;
    }

    public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info) =>
        NtStatus.AccessDenied;

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
    {
        _mounted.Set();
        return NtStatus.Success;
    }

    public NtStatus Unmounted(IDokanFileInfo info) => NtStatus.Success;

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
    {
        streams = new List<FileInformation>();
        return NtStatus.NotImplemented;
    }
}
