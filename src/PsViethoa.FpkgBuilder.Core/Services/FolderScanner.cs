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

    /// <summary>Thống kê nguồn: thư mục (quét song song) hoặc ảnh exFAT (duyệt bảng thư mục, không đọc dữ liệu).</summary>
    public static FolderStats Scan(string sourcePath, CancellationToken cancellationToken)
    {
        if (SourceLocator.Detect(sourcePath) == SourceKind.ExFatImage)
        {
            return ScanImage(sourcePath, cancellationToken);
        }

        return ScanFolder(sourcePath, cancellationToken);
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
