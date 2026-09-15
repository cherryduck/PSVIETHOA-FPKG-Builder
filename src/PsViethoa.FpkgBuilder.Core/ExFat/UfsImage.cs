using System.Buffers.Binary;

namespace PsViethoa.FpkgBuilder.Core.ExFat;

/// <summary>Một mục (tệp hoặc thư mục) trong ảnh UFS2.</summary>
public sealed record UfsEntry(
    string Path,
    string Name,
    bool IsDirectory,
    long Length,
    uint Inode,
    DateTime? Modified)
{
    public bool IsEmpty => !IsDirectory && Length == 0;

    /// <summary>Đường dẫn tương đối so với một thư mục gốc trong ảnh (dấu "/" phân cách).</summary>
    public string RelativeTo(UfsEntry root)
    {
        if (root.Path.Length == 0)
        {
            return Path.TrimStart('/');
        }

        return Path.Length > root.Path.Length && Path.StartsWith(root.Path, StringComparison.OrdinalIgnoreCase)
            ? Path[(root.Path.Length + 1)..]
            : Path.TrimStart('/');
    }
}

/// <summary>
/// Bộ đọc ảnh UFS2 (FreeBSD Fast File System phiên bản 2) — định dạng của tệp <c>.ffpkg</c>.
/// <para>
/// Bản dump kiểu này là một hệ tệp UFS2 nguyên vẹn mà thư mục gốc chính là thư mục ứng dụng của game: <c>eboot.bin</c>,
/// <c>sce_sys/</c>, <c>sce_module/</c>, <c>fakelib/</c>… Nhân PS5 vốn dựa trên FreeBSD nên hệ tệp này xuất hiện tự nhiên.
/// macOS bỏ hỗ trợ UFS từ 10.7 và Windows chưa bao giờ có, nên không hệ điều hành nào gắn được; muốn đọc phải tự phân giải,
/// đúng như đã làm với exFAT.
/// </para>
/// <para>
/// Bố cục: siêu khối ở byte 65536 với magic <c>0x19540119</c> tại offset 1372. Đĩa chia thành nhóm trụ, mỗi nhóm giữ một dải
/// inode. Inode UFS2 dài 256 byte, mang 12 con trỏ khối trực tiếp và 3 mức gián tiếp. Thư mục là chuỗi bản ghi
/// (inode, độ dài bản ghi, kiểu, độ dài tên, tên).
/// </para>
/// Chỉ đọc; không bao giờ mở tệp ảnh để ghi.
/// </summary>
public sealed class UfsImage : IDisposable
{
    public const string Extension = ".ffpkg";

    /// <summary>Siêu khối UFS2 nằm ở byte 65536 (SBLOCK_UFS2 của FreeBSD).</summary>
    public const long SuperBlockOffset = 65536;

    /// <summary>Magic <c>0x19540119</c> nằm ở offset 1372 trong siêu khối.</summary>
    public const int MagicOffset = 1372;

    public const uint Magic = 0x19540119;

    /// <summary>Inode gốc của mọi hệ tệp UFS.</summary>
    public const uint RootInode = 2;

    private const int InodeSize = 256;

    private const int MaxDirectorySize = 8 << 20;

    private const int DirectBlocks = 12;

    private readonly FileStream _stream;
    private readonly object _lock = new();
    private readonly byte[] _superBlock;

    private UfsImage(string path, FileStream stream, byte[] superBlock)
    {
        Path = path;
        _stream = stream;
        _superBlock = superBlock;

        InodeBlockOffset = ReadInt32(16);
        CylinderGroupCount = ReadInt32(44);
        BlockSize = ReadInt32(48);
        FragmentSize = ReadInt32(52);
        FragmentsPerBlock = ReadInt32(56);
        IndirectsPerBlock = ReadInt32(116);
        InodesPerBlock = ReadInt32(120);
        InodesPerGroup = ReadInt32(184);
        FragmentsPerGroup = ReadInt32(188);
        TotalFragments = ReadInt64(1080);
        Root = new UfsEntry(string.Empty, string.Empty, true, 0, RootInode, null);
    }

    public string Path { get; }

    public int BlockSize { get; }

    public int FragmentSize { get; }

    public int FragmentsPerBlock { get; }

    public int CylinderGroupCount { get; }

    public int InodesPerGroup { get; }

    public int FragmentsPerGroup { get; }

    public int InodesPerBlock { get; }

    public int IndirectsPerBlock { get; }

    public int InodeBlockOffset { get; }

    /// <summary>Kích thước hệ tệp tính bằng mảnh (fragment).</summary>
    public long TotalFragments { get; }

    /// <summary>Kích thước hệ tệp tính bằng byte.</summary>
    public long VolumeLengthBytes => TotalFragments * FragmentSize;

    public UfsEntry Root { get; }

    /// <summary>Tệp có phải ảnh UFS2 không — kiểm tra magic ở đúng vị trí trong siêu khối.</summary>
    public static bool IsUfsFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < SuperBlockOffset + MagicOffset + 4)
            {
                return false;
            }

            stream.Seek(SuperBlockOffset + MagicOffset, SeekOrigin.Begin);
            Span<byte> magic = stackalloc byte[4];
            return stream.ReadAtLeast(magic, 4, throwOnEndOfStream: false) == 4
                   && BinaryPrimitives.ReadUInt32LittleEndian(magic) == Magic;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool HasExtension(string path) =>
        System.IO.Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Mở ảnh. Ném InvalidDataException khi siêu khối không hợp lệ.</summary>
    public static UfsImage Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess);
        try
        {
            var superBlock = new byte[1536];
            stream.Seek(SuperBlockOffset, SeekOrigin.Begin);
            if (stream.ReadAtLeast(superBlock, superBlock.Length, throwOnEndOfStream: false) != superBlock.Length)
            {
                throw new InvalidDataException("UFS2: file is too small to hold a superblock.");
            }

            if (BinaryPrimitives.ReadUInt32LittleEndian(superBlock.AsSpan(MagicOffset)) != Magic)
            {
                throw new InvalidDataException("UFS2: superblock magic not found at offset 65536+1372.");
            }

            var image = new UfsImage(path, stream, superBlock);
            image.Validate();
            return image;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private void Validate()
    {
        if (BlockSize is < 4096 or > (1 << 20) || (BlockSize & (BlockSize - 1)) != 0)
        {
            throw new InvalidDataException($"UFS2: block size {BlockSize} is not a sane power of two.");
        }

        if (FragmentSize <= 0 || BlockSize % FragmentSize != 0)
        {
            throw new InvalidDataException($"UFS2: fragment size {FragmentSize} does not divide the block size.");
        }

        if (InodesPerGroup <= 0 || FragmentsPerGroup <= 0 || CylinderGroupCount <= 0)
        {
            throw new InvalidDataException("UFS2: cylinder group geometry is invalid.");
        }

        if (InodesPerBlock <= 0 || InodesPerBlock != BlockSize / InodeSize)
        {
            throw new InvalidDataException("UFS2: inodes per block does not match the block size.");
        }
    }

    // ===================== Inode =====================

    private readonly record struct Inode(ushort Mode, long Size, long[] Direct, long[] Indirect, DateTime? Modified)
    {
        public bool IsDirectory => (Mode & 0xF000) == 0x4000;

        public bool IsRegularFile => (Mode & 0xF000) == 0x8000;
    }

    /// <summary>Byte offset của inode: xác định nhóm trụ rồi tra trong dải inode của nhóm đó.</summary>
    private long InodeOffset(uint ino)
    {
        var group = ino / (uint)InodesPerGroup;
        var index = ino % (uint)InodesPerGroup;
        if (group >= (uint)CylinderGroupCount)
        {
            throw new InvalidDataException($"UFS2: inode {ino} falls outside the {CylinderGroupCount} cylinder groups.");
        }

        var block = (long)FragmentsPerGroup * group + InodeBlockOffset + index / (uint)InodesPerBlock;
        return block * FragmentSize + (long)(index % (uint)InodesPerBlock) * InodeSize;
    }

    private Inode ReadInode(uint ino)
    {
        var buffer = new byte[InodeSize];
        ReadExact(InodeOffset(ino), buffer);

        var mode = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(0));
        var size = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(16));
        var mtime = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(40));

        var direct = new long[DirectBlocks];
        for (var i = 0; i < DirectBlocks; i++)
        {
            direct[i] = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(112 + i * 8));
        }

        var indirect = new long[3];
        for (var i = 0; i < 3; i++)
        {
            indirect[i] = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(208 + i * 8));
        }

        DateTime? modified = mtime is > 0 and < 253402300799
            ? DateTimeOffset.FromUnixTimeSeconds(mtime).UtcDateTime
            : null;
        return new Inode(mode, size, direct, indirect, modified);
    }

    // ===================== Khối dữ liệu =====================

    /// <summary>
    /// Liệt kê số hiệu mảnh của từng khối logic trong tệp, đi qua cả ba mức con trỏ gián tiếp.
    /// Khối bằng 0 là lỗ hổng (sparse) và được trả về nguyên giá trị 0 để người gọi ghi số 0 vào đó.
    /// </summary>
    private IEnumerable<long> EnumerateBlocks(Inode inode)
    {
        var blocks = (inode.Size + BlockSize - 1) / BlockSize;
        var emitted = 0L;

        foreach (var block in inode.Direct)
        {
            if (emitted >= blocks)
            {
                yield break;
            }

            emitted++;
            yield return block;
        }

        for (var level = 0; level < inode.Indirect.Length; level++)
        {
            if (emitted >= blocks)
            {
                yield break;
            }

            foreach (var block in EnumerateIndirect(inode.Indirect[level], level))
            {
                if (emitted >= blocks)
                {
                    yield break;
                }

                emitted++;
                yield return block;
            }
        }
    }

    private IEnumerable<long> EnumerateIndirect(long block, int level)
    {
        if (block == 0)
        {
            yield break;
        }

        var pointers = new byte[BlockSize];
        ReadExact(block * FragmentSize, pointers);
        for (var i = 0; i < IndirectsPerBlock; i++)
        {
            var next = BinaryPrimitives.ReadInt64LittleEndian(pointers.AsSpan(i * 8));
            if (level == 0)
            {
                yield return next;
                continue;
            }

            if (next == 0)
            {
                yield break;
            }

            foreach (var child in EnumerateIndirect(next, level - 1))
            {
                yield return child;
            }
        }
    }

    private byte[] ReadFile(Inode inode, long maxBytes)
    {
        if (inode.Size > maxBytes)
        {
            throw new InvalidDataException($"UFS2: file is {inode.Size} bytes, over the {maxBytes} byte limit.");
        }

        var data = new byte[inode.Size];
        var written = 0;
        foreach (var block in EnumerateBlocks(inode))
        {
            if (written >= data.Length)
            {
                break;
            }

            var count = Math.Min(BlockSize, data.Length - written);
            if (block != 0)
            {
                ReadExact(block * FragmentSize, data.AsSpan(written, count));
            }

            written += count;
        }

        return data;
    }

    // ===================== Thư mục =====================

    /// <summary>Liệt kê các mục con của một thư mục ("." và ".." được bỏ qua).</summary>
    public IEnumerable<UfsEntry> Enumerate(UfsEntry directory)
    {
        if (!directory.IsDirectory)
        {
            yield break;
        }

        var inode = ReadInode(directory.Inode);
        if (!inode.IsDirectory)
        {
            yield break;
        }

        var data = ReadFile(inode, MaxDirectorySize);
        var offset = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (offset + 8 <= data.Length)
        {
            var ino = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4));
            var type = data[offset + 6];
            var nameLength = data[offset + 7];
            if (recordLength < 8 || offset + recordLength > data.Length)
            {
                yield break;
            }

            if (ino != 0 && nameLength > 0 && offset + 8 + nameLength <= data.Length)
            {
                var name = System.Text.Encoding.UTF8.GetString(data, offset + 8, nameLength);
                if (name != "." && name != ".." && seen.Add(name))
                {
                    var child = ReadInode(ino);
                    var isDirectory = type == 4 || (type == 0 && child.IsDirectory);
                    yield return new UfsEntry(
                        directory.Path.Length == 0 ? name : directory.Path + "/" + name,
                        name,
                        isDirectory,
                        isDirectory ? 0 : child.Size,
                        ino,
                        child.Modified);
                }
            }

            offset += recordLength;
        }
    }

    /// <summary>Đi đệ quy toàn bộ cây bên dưới một thư mục.</summary>
    public IEnumerable<UfsEntry> Walk(UfsEntry root, Func<UfsEntry, bool>? shouldRecurse = null)
    {
        var pending = new Stack<UfsEntry>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Enumerate(directory))
            {
                yield return entry;
                if (entry.IsDirectory && (shouldRecurse?.Invoke(entry) ?? true))
                {
                    pending.Push(entry);
                }
            }
        }
    }

    /// <summary>Tìm một mục theo đường dẫn tương đối ("sce_sys/param.json"); null nếu không có.</summary>
    public UfsEntry? Find(string relativePath, UfsEntry? start = null)
    {
        var current = start ?? Root;
        foreach (var segment in relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.IsDirectory)
            {
                return null;
            }

            UfsEntry? next = null;
            foreach (var child in Enumerate(current))
            {
                if (string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next == null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // ===================== Đọc tệp =====================

    public byte[] ReadAllBytes(UfsEntry file, long maxBytes = 64L * 1024 * 1024)
    {
        if (file.IsDirectory)
        {
            throw new InvalidOperationException("UFS2: cannot read a directory as a file.");
        }

        return ReadFile(ReadInode(file.Inode), maxBytes);
    }

    public Stream OpenRead(UfsEntry file)
    {
        if (file.IsDirectory)
        {
            throw new InvalidOperationException("UFS2: cannot read a directory as a file.");
        }

        var inode = ReadInode(file.Inode);
        return new UfsFileStream(this, inode.Size, EnumerateBlocks(inode).ToArray());
    }

    // ===================== Đọc thô =====================

    internal void ReadExact(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            _stream.Seek(offset, SeekOrigin.Begin);
            if (_stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) != buffer.Length)
            {
                throw new EndOfStreamException($"UFS2: image ends before offset {offset + buffer.Length}.");
            }
        }
    }

    private int ReadInt32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_superBlock.AsSpan(offset));

    private long ReadInt64(int offset) => BinaryPrimitives.ReadInt64LittleEndian(_superBlock.AsSpan(offset));

    public void Dispose() => _stream.Dispose();
}

/// <summary>Luồng đọc một tệp trong ảnh UFS2: ánh xạ vị trí đọc sang khối tương ứng, không nạp cả tệp vào bộ nhớ.</summary>
internal sealed class UfsFileStream(UfsImage image, long length, long[] blocks) : Stream
{
    private long _position;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, length);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= length || buffer.Length == 0)
        {
            return 0;
        }

        var blockSize = image.BlockSize;
        var index = (int)(_position / blockSize);
        if (index >= blocks.Length)
        {
            return 0;
        }

        var inBlock = (int)(_position % blockSize);
        var count = (int)Math.Min(Math.Min(buffer.Length, blockSize - inBlock), length - _position);
        var block = blocks[index];
        if (block == 0)
        {
            // Lỗ hổng trong tệp: UFS trả về số 0 chứ không có khối nào trên đĩa.
            buffer[..count].Clear();
        }
        else
        {
            image.ReadExact(block * image.FragmentSize + inBlock, buffer[..count]);
        }

        _position += count;
        return count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => length + offset,
        };
        _position = Math.Clamp(target, 0, length);
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
