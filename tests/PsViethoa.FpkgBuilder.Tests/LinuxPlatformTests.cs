using PsViethoa.FpkgBuilder.Core.Services;
using Xunit;

namespace PsViethoa.FpkgBuilder.Tests;

/// <summary>Kiểm thử các nhánh riêng của Linux: ngăn ngủ, thư mục tạm, gợi ý tệp cập nhật.</summary>
public sealed class LinuxPlatformTests
{
    [Fact]
    public void SleepInhibitor_UsesSystemdInhibitOnLinux()
    {
        using var inhibitor = SleepInhibitor.TryAcquire();

        if (OperatingSystem.IsLinux() && SleepInhibitor.FindSystemdInhibit() != null)
        {
            Assert.NotNull(inhibitor);
            Assert.Equal("systemd-inhibit", inhibitor!.Mechanism);
        }
        else if (!OperatingSystem.IsLinux())
        {
            // macOS/Windows giữ nguyên cơ chế cũ.
            Assert.True(inhibitor == null || inhibitor.Mechanism != "systemd-inhibit");
        }
    }

    [Fact]
    public void SleepInhibitor_DisposeIsIdempotent()
    {
        var inhibitor = SleepInhibitor.TryAcquire();
        inhibitor?.Dispose();
        inhibitor?.Dispose();
    }

    [Fact]
    public void FindSystemdInhibit_IsNullOffLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Null(SleepInhibitor.FindSystemdInhibit());
        }
    }

    /// <summary>
    /// /tmp thường là tmpfs (RAM) trên Linux — thư mục tạm mặc định phải nằm trong cache XDG trên đĩa thật,
    /// nếu không ảnh giải nén hàng chục GB sẽ ăn hết bộ nhớ.
    /// </summary>
    [Fact]
    public void SuggestTemporaryFolder_AvoidsTmpfsOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Thư mục xuất nằm trên ổ hệ thống → rơi vào nhánh dự phòng theo nền tảng.
        var suggestion = BuildPreparer.SuggestTemporaryFolder("/");

        Assert.False(
            suggestion.StartsWith("/tmp/", StringComparison.Ordinal),
            $"The default temp folder must not live under /tmp (usually tmpfs): {suggestion}");
        Assert.Contains("psviethoa-fpkg-builder", suggestion, StringComparison.Ordinal);
    }

    /// <summary>
    /// Điểm gắn của ổ chứa thư mục xuất thường thuộc root (ví dụ /home): gợi ý phải là nơi ghi được thật,
    /// không phải gốc ổ — trước đây sinh ra "/home/fpkg-temp" rồi hỏng với "Access to the path is denied".
    /// </summary>
    [Fact]
    public void SuggestTemporaryFolder_IsActuallyWritable()
    {
        var output = Path.Combine(Path.GetTempPath(), "psviethoa-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        try
        {
            var suggestion = BuildPreparer.SuggestTemporaryFolder(output);
            Assert.False(string.IsNullOrWhiteSpace(suggestion));

            // Phải tạo được thật, không chỉ là một chuỗi đường dẫn hợp lệ.
            Directory.CreateDirectory(suggestion);
            var probe = Path.Combine(suggestion, "probe.txt");
            File.WriteAllText(probe, "ok");
            Assert.Equal("ok", File.ReadAllText(probe));
            File.Delete(probe);
        }
        finally
        {
            try
            {
                Directory.Delete(output, recursive: true);
            }
            catch (Exception)
            {
            }
        }
    }

    [Fact]
    public void PlatformAssetHint_IsSetOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // Rỗng nghĩa là bộ kiểm tra cập nhật không khớp được tệp phát hành nào.
        var hint = UpdateChecker.PlatformAssetHint();
        Assert.False(string.IsNullOrWhiteSpace(hint));
        Assert.StartsWith("linux-", hint, StringComparison.Ordinal);

        var token = UpdateChecker.PreferredAssetToken();
        Assert.Contains(token, new[] { ".tar.gz", ".AppImage" });
        Assert.Equal(UpdateChecker.IsRunningFromAppImage ? ".AppImage" : ".tar.gz", token);
    }
}
