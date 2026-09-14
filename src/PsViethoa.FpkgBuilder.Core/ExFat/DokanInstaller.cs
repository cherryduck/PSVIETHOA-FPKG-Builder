using System.ComponentModel;
using System.Diagnostics;

namespace PsViethoa.FpkgBuilder.Core.ExFat;

public enum DokanInstallOutcome
{
    /// <summary>Đã cài và dokan2.dll đã có — gắn ảnh dùng được ngay.</summary>
    Installed,

    /// <summary>Đã cài nhưng Windows yêu cầu khởi động lại trước khi driver chạy.</summary>
    RebootRequired,

    /// <summary>Driver đã có sẵn từ trước.</summary>
    AlreadyInstalled,

    /// <summary>Người dùng từ chối quyền quản trị hoặc huỷ.</summary>
    Cancelled,

    /// <summary>Gói ứng dụng không kèm bộ cài (bản build tay / macOS).</summary>
    NotBundled,

    Failed,
}

public sealed record DokanInstallResult(DokanInstallOutcome Outcome, int ExitCode, string? Detail);

/// <summary>
/// Cài driver Dokan đóng gói kèm ứng dụng (<c>redist/Dokan_x64.msi</c>, bản LGPL nguyên vẹn từ dự án Dokany) bằng msiexec ở chế
/// độ im lặng — người dùng chỉ bấm một nút và xác nhận quyền quản trị một lần, không phải tải hay cài gì thêm. Windows bắt
/// buộc phải có driver mới gắn được ổ ảo, nên đây là cách gần nhất với "không cần cài".
/// </summary>
public static class DokanInstaller
{
    public const string BundledFileName = "Dokan_x64.msi";
    public const string BundledVersion = "2.3.1.1000";
    public const string BundledLicenseFileName = "DOKAN-LICENSE.md";

    private const int MsiSuccess = 0;
    private const int MsiUserCancelled = 1602;
    private const int MsiFatal = 1603;
    private const int MsiAnotherVersion = 1638;
    private const int MsiRebootInitiated = 1641;
    private const int MsiRebootRequired = 3010;
    private const int Win32ErrorCancelled = 1223;

    /// <summary>Đường dẫn bộ cài kèm theo (cạnh ứng dụng trong thư mục redist/), null nếu gói này không kèm.</summary>
    public static string? BundledInstallerPath
    {
        get
        {
            foreach (var directory in CandidateDirectories())
            {
                foreach (var candidate in new[] { Path.Combine(directory, "redist", BundledFileName), Path.Combine(directory, BundledFileName) })
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }

    public static bool IsBundled => OperatingSystem.IsWindows() && BundledInstallerPath != null;

    /// <summary>Có thể cài ngay từ ứng dụng: Windows, chưa có driver, có bộ cài kèm theo.</summary>
    public static bool CanInstall => OperatingSystem.IsWindows() && !DokanImageMounter.IsDriverInstalled && BundledInstallerPath != null;

    /// <summary>
    /// Chạy <c>msiexec /i Dokan_x64.msi /qn /norestart</c> với quyền quản trị (Windows hiện hộp UAC) và chờ xong.
    /// Không ném ngoại lệ — mọi tình huống được trả về trong <see cref="DokanInstallResult"/>.
    /// </summary>
    public static DokanInstallResult Install(TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new DokanInstallResult(DokanInstallOutcome.Failed, -1, "Windows only");
        }

        if (DokanImageMounter.IsDriverInstalled)
        {
            return new DokanInstallResult(DokanInstallOutcome.AlreadyInstalled, MsiSuccess, DokanImageMounter.DriverVersion);
        }

        var installer = BundledInstallerPath;
        if (installer == null)
        {
            return new DokanInstallResult(DokanInstallOutcome.NotBundled, -1, null);
        }

        var info = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"/i \"{installer}\" /qn /norestart",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Win32ErrorCancelled)
        {
            return new DokanInstallResult(DokanInstallOutcome.Cancelled, Win32ErrorCancelled, null);
        }
        catch (Exception ex)
        {
            return new DokanInstallResult(DokanInstallOutcome.Failed, -1, ex.Message);
        }

        if (process == null)
        {
            return new DokanInstallResult(DokanInstallOutcome.Failed, -1, "msiexec did not start");
        }

        using (process)
        {
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                }

                return new DokanInstallResult(DokanInstallOutcome.Failed, -2, "timeout");
            }

            var code = process.ExitCode;
            var installed = DokanImageMounter.IsDriverInstalled;
            return code switch
            {
                MsiSuccess => installed
                    ? new DokanInstallResult(DokanInstallOutcome.Installed, code, DokanImageMounter.DriverVersion)
                    : new DokanInstallResult(DokanInstallOutcome.Failed, code, "dokan2.dll missing after install"),
                MsiRebootRequired or MsiRebootInitiated => new DokanInstallResult(DokanInstallOutcome.RebootRequired, code, null),
                MsiUserCancelled or Win32ErrorCancelled => new DokanInstallResult(DokanInstallOutcome.Cancelled, code, null),
                MsiAnotherVersion => installed
                    ? new DokanInstallResult(DokanInstallOutcome.AlreadyInstalled, code, DokanImageMounter.DriverVersion)
                    : new DokanInstallResult(DokanInstallOutcome.Failed, code, "another Dokan version is installed"),
                MsiFatal => new DokanInstallResult(DokanInstallOutcome.Failed, code, "msiexec 1603"),
                _ => new DokanInstallResult(DokanInstallOutcome.Failed, code, null),
            };
        }
    }

    /// <summary>Thư mục ứng dụng, thư mục tệp thực thi, thư mục cha và app/ cạnh nó (fpkg-cli nằm cạnh app/ trong gói zip).</summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) })
        {
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory));
            foreach (var candidate in new[] { directory, parent, parent == null ? null : Path.Combine(parent, "app") })
            {
                if (!string.IsNullOrEmpty(candidate) && seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }
}
