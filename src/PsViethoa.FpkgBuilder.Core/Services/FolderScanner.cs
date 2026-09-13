using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Quét nhanh thống kê thư mục nguồn (số tệp, tổng dung lượng) bằng một lượt duyệt
/// FileSystemEnumerable, chạy song song theo từng thư mục con cấp 1.
/// </summary>
public static class FolderScanner
{
    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private static readonly EnumerationOptions ShallowOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    private readonly record struct Entry(bool IsDirectory, long Length, string? Path);

    /// <summary>
    /// Thống kê nguồn: thư mục (quét song song), ảnh exFAT (duyệt bảng thư mục, không đọc dữ liệu)
    /// hoặc dự án GP5 (chỉ những tệp dự án sẽ đóng gói).
    /// </summary>
    public static FolderStats Scan(string sourcePath, CancellationToken cancellationToken)
    {
        switch (SourceLocator.Detect(sourcePath))
        {
            case SourceKind.ExFatImage:
                return ScanImage(sourcePath, cancellationToken);
            case SourceKind.Gp5Project:
                return ScanGp5(sourcePath, cancellationToken);
            default:
                return ScanFolder(sourcePath, cancellationToken);
        }
    }

    /// <summary>Thống kê dự án GP5: tệp/dung lượng theo danh sách đóng gói; số thư mục = số thư mục đích khác nhau.</summary>
    private static FolderStats ScanGp5(string projectPath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long files = 0, bytes = 0, largest = 0;
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var project = Gp5ProjectInfo.Load(projectPath);
        foreach (var entry in project.EnumerateFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            files++;
            bytes += entry.Length;
            largest = Math.Max(largest, entry.Length);

            var slash = entry.DestinationPath.LastIndexOf('/');
            while (slash > 0)
            {
                directories.Add(entry.DestinationPath[..slash]);
                slash = entry.DestinationPath.LastIndexOf('/', slash - 1);
            }
        }

        return new FolderStats(files, directories.Count, bytes, largest, stopwatch.Elapsed);
    }

    private static FolderStats ScanImage(string imagePath, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long files = 0, directories = 0, bytes = 0, largest = 0;
        using var image = PsViethoa.FpkgBuilder.Core.ExFat.ExFatImage.Open(imagePath);
        var root = SourceLocator.FindAppRoot(image) ?? image.Root;
        foreach (var entry in image.Walk(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                directories++;
            }
            else
            {
                files++;
                bytes += entry.Length;
                largest = Math.Max(largest, entry.Length);
            }
        }

        return new FolderStats(files, directories, bytes, largest, stopwatch.Elapsed);
    }

    public static FolderStats ScanFolder(string root, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long files = 0, directories = 0, bytes = 0, largest = 0;
        var subdirectories = new List<string>();

        foreach (var entry in Enumerate(root, ShallowOptions, includePath: true))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                directories++;
                subdirectories.Add(entry.Path!);
            }
            else
            {
                files++;
                bytes += entry.Length;
                largest = Math.Max(largest, entry.Length);
            }
        }

        if (subdirectories.Count > 0)
        {
            var partials = new ConcurrentBag<(long Files, long Directories, long Bytes, long Largest)>();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
                CancellationToken = cancellationToken,
            };

            Parallel.ForEach(subdirectories, options, directory =>
            {
                long f = 0, d = 0, b = 0, l = 0;
                foreach (var entry in Enumerate(directory, RecursiveOptions, includePath: false))
                {
                    if (entry.IsDirectory)
                    {
                        d++;
                    }
                    else
                    {
                        f++;
                        b += entry.Length;
                        l = Math.Max(l, entry.Length);
                    }
                }

                partials.Add((f, d, b, l));
            });

            foreach (var (f, d, b, l) in partials)
            {
                files += f;
                directories += d;
                bytes += b;
                largest = Math.Max(largest, l);
            }
        }

        return new FolderStats(files, directories, bytes, largest, stopwatch.Elapsed);
    }

    private static IEnumerable<Entry> Enumerate(string directory, EnumerationOptions options, bool includePath)
    {
        return new FileSystemEnumerable<Entry>(
            directory,
            (ref FileSystemEntry entry) => new Entry(
                entry.IsDirectory,
                entry.IsDirectory ? 0 : entry.Length,
                includePath && entry.IsDirectory ? entry.ToFullPath() : null),
            options);
    }
}
