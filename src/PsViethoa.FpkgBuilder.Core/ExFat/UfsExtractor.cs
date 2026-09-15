using PsViethoa.FpkgBuilder.Core.Services;

namespace PsViethoa.FpkgBuilder.Core.ExFat;

/// <summary>
/// Giải nén một thư mục bên trong ảnh UFS2 (.ffpkg) ra đĩa — song song, bộ đệm lớn, bỏ qua tệp rác.
/// Cùng hình dạng với <see cref="ExFatExtractor"/> để phần còn lại của công cụ đối xử với hai loại ảnh như nhau.
/// </summary>
public static class UfsExtractor
{
    private const int BufferSize = 4 * 1024 * 1024;

    public sealed record Plan(
        UfsEntry AppRoot,
        IReadOnlyList<UfsEntry> Directories,
        IReadOnlyList<UfsEntry> Files,
        long TotalBytes,
        int SkippedJunk);

    /// <summary>Lập danh sách tệp/thư mục cần chép; tệp rác hệ điều hành được bỏ qua khi skipJunk = true.</summary>
    public static Plan CreatePlan(UfsImage image, UfsEntry appRoot, bool skipJunk, CancellationToken cancellationToken)
    {
        var directories = new List<UfsEntry>();
        var files = new List<UfsEntry>();
        long total = 0;
        var skipped = 0;

        foreach (var entry in image.Walk(appRoot, child => !(skipJunk && JunkFileFinder.IsJunkDirectoryName(child.Name))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                if (skipJunk && JunkFileFinder.IsJunkDirectoryName(entry.Name))
                {
                    skipped++;
                    continue;
                }

                directories.Add(entry);
            }
            else
            {
                if (skipJunk && JunkFileFinder.IsJunkFileName(entry.Name))
                {
                    skipped++;
                    continue;
                }

                files.Add(entry);
                total += entry.Length;
            }
        }

        return new Plan(appRoot, directories, files, total, skipped);
    }

    /// <summary>Chép toàn bộ theo kế hoạch vào thư mục đích; báo tiến độ theo byte.</summary>
    public static void Extract(
        UfsImage image,
        Plan plan,
        string destination,
        Action<long, long>? progress,
        CancellationToken cancellationToken,
        int parallelism = 3)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in plan.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Ảnh có thể đến từ nguồn không tin cậy: chặn tên đi ngược ra ngoài thư mục đích ("../..").
            Directory.CreateDirectory(PackageReader.SafeTarget(destination, directory.RelativeTo(plan.AppRoot)));
        }

        long done = 0;
        var total = Math.Max(1, plan.TotalBytes);
        var lastReport = 0L;
        var reportGate = new object();

        void Report(long delta)
        {
            var now = Interlocked.Add(ref done, delta);
            if (progress == null)
            {
                return;
            }

            // Báo tối đa mỗi ~16 MB để không làm nghẽn giao diện.
            lock (reportGate)
            {
                if (now - lastReport >= 16L * 1024 * 1024 || now >= total)
                {
                    lastReport = now;
                    progress(now, total);
                }
            }
        }

        // Tệp lớn trước để cân bằng tải giữa các luồng.
        var ordered = plan.Files.OrderByDescending(f => f.Length).ToList();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(parallelism, 1, 8),
            CancellationToken = cancellationToken,
        };

        Parallel.ForEach(ordered, options, file =>
        {
            var target = PackageReader.SafeTarget(destination, file.RelativeTo(plan.AppRoot));
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            using var source = image.OpenRead(file);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.SequentialScan);
            if (file.Length > 0)
            {
                output.SetLength(file.Length);
            }

            var buffer = GC.AllocateUninitializedArray<byte>((int)Math.Min(BufferSize, Math.Max(4096, file.Length)));
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = source.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    break;
                }

                output.Write(buffer, 0, read);
                Report(read);
            }

            output.Flush();
            output.Dispose();
            if (file.Modified is { } modified)
            {
                try
                {
                    File.SetLastWriteTimeUtc(target, modified);
                }
                catch (Exception)
                {
                    // Dấu thời gian không đặt được thì bỏ qua: nội dung mới là thứ quan trọng.
                }
            }
        });

        progress?.Invoke(total, total);
    }
}
