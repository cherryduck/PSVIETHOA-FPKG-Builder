using System.Diagnostics;
using System.Xml.Linq;

namespace PsViethoa.FpkgBuilder.Core.ExFat;

/// <summary>Một ảnh exFAT đã gắn (chỉ đọc) qua hdiutil trên macOS; Dispose để tháo.</summary>
public sealed class ExFatMount : IDisposable
{
    private bool _disposed;

    internal ExFatMount(string mountPoint, string device)
    {
        MountPoint = mountPoint;
        Device = device;
    }

    public string MountPoint { get; }

    public string Device { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ExFatMounter.Detach(MountPoint, Device);
    }
}

/// <summary>Gắn ảnh exFAT thô bằng hdiutil (macOS) — không cần sao chép dữ liệu.</summary>
public static class ExFatMounter
{
    private const string HdiutilPath = "/usr/bin/hdiutil";

    public static bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(HdiutilPath);

    public static ExFatMount Mount(string imagePath, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("hdiutil is only available on macOS.");
        }

        var (exitCode, stdout, stderr) = Run(
            [
                "attach", "-readonly", "-nobrowse", "-noverify", "-noautofsck", "-plist",
                "-imagekey", "diskimage-class=CRawDiskImage", imagePath,
            ],
            TimeSpan.FromMinutes(3),
            cancellationToken);

        if (exitCode != 0)
        {
            throw new IOException($"hdiutil attach exit {exitCode}: {stderr.Trim()}");
        }

        var (mountPoint, device) = ParseAttachOutput(stdout);
        if (mountPoint == null || !Directory.Exists(mountPoint))
        {
            if (device != null)
            {
                Detach(null, device);
            }

            throw new IOException("hdiutil attach did not report a mount point: " + stderr.Trim());
        }

        return new ExFatMount(mountPoint, device ?? mountPoint);
    }

    internal static void Detach(string? mountPoint, string device)
    {
        foreach (var attempt in new[] { (Target: mountPoint ?? device, Force: false), (Target: device, Force: false), (Target: device, Force: true) })
        {
            if (string.IsNullOrEmpty(attempt.Target))
            {
                continue;
            }

            try
            {
                var arguments = attempt.Force ? new[] { "detach", attempt.Target, "-force" } : new[] { "detach", attempt.Target };
                var (exitCode, _, _) = Run(arguments, TimeSpan.FromSeconds(60), CancellationToken.None);
                if (exitCode == 0)
                {
                    return;
                }
            }
            catch (Exception)
            {
            }

            Thread.Sleep(800);
        }
    }

    private static (string? MountPoint, string? Device) ParseAttachOutput(string plist)
    {
        try
        {
            var document = XDocument.Parse(plist);
            string? mountPoint = null;
            string? mountDevice = null;
            string? firstDevice = null;

            foreach (var dict in document.Descendants("dict"))
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                string? pendingKey = null;
                foreach (var element in dict.Elements())
                {
                    if (element.Name.LocalName == "key")
                    {
                        pendingKey = element.Value;
                    }
                    else if (pendingKey != null)
                    {
                        map[pendingKey] = element.Value;
                        pendingKey = null;
                    }
                }

                if (map.TryGetValue("dev-entry", out var device))
                {
                    firstDevice ??= device;
                    if (map.TryGetValue("mount-point", out var mp) && !string.IsNullOrWhiteSpace(mp))
                    {
                        mountPoint = mp;
                        mountDevice = device;
                    }
                }
            }

            return (mountPoint, mountDevice ?? firstDevice);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(string[] arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(HdiutilPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new IOException("Cannot start hdiutil.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
            }

            throw new TimeoutException("hdiutil timed out.");
        }

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }
}
