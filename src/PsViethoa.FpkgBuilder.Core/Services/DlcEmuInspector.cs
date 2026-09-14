using PsViethoa.FpkgBuilder.Core.ExFat;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Dấu vết DLC emu (drakmor/dlc_emu) trong nguồn: tệp cấu hình dlc_emu.ini và các module thay thế trong fakelib.</summary>
public sealed record DlcEmuInfo(bool Config, IReadOnlyList<string> Paths)
{
    public static readonly DlcEmuInfo Empty = new(false, Array.Empty<string>());

    public bool Present => Paths.Count > 0;
}

/// <summary>
/// Tìm bộ giả lập DLC của Drakmor trong nguồn: <c>dlc_emu.ini</c> ở gốc ứng dụng và các module thay thế
/// (<c>libSceAppContent</c>, <c>libSceNpEntitlementAccess</c>, <c>libSceGameUpdate</c>) trong <c>fakelib</c>/<c>fakelib2</c>.
/// <para>
/// Bộ này mở khoá DLC cho bản dump chạy qua ShadowMount+ (SM+ gắn fakelib vào common/lib của sandbox). Khi cài từ gói thì
/// không có SM+ làm việc đó; cộng đồng báo một số game (Stellar Blade) đứng ở màn hình splash cho tới khi xoá các tệp này.
/// Ứng dụng phát hiện để cảnh báo và cho phép loại khỏi gói khi người dùng chọn.
/// </para>
/// </summary>
public static class DlcEmuInspector
{
    public const string ConfigName = "dlc_emu.ini";

    private static readonly string[] FakeLibFolders = { "fakelib", "fakelib2" };

    private static readonly string[] ModuleNames =
    {
        "libSceAppContent",
        "libSceNpEntitlementAccess",
        "libSceGameUpdate",
    };

    private static readonly string[] ModuleExtensions = { ".sprx", ".prx" };

    /// <summary>Các đường dẫn (tương đối thư mục ứng dụng) bị loại khỏi gói khi bỏ DLC emu.</summary>
    public static IEnumerable<string> Candidates()
    {
        yield return ConfigName;
        foreach (var folder in FakeLibFolders)
        {
            foreach (var module in ModuleNames)
            {
                foreach (var extension in ModuleExtensions)
                {
                    yield return folder + "/" + module + extension;
                }
            }
        }
    }

    public static DlcEmuInfo ScanFolder(string sourceFolder, CancellationToken cancellationToken)
    {
        try
        {
            var found = new List<string>();
            var config = false;
            foreach (var relative in Candidates())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(Path.Combine(sourceFolder, relative.Replace('/', Path.DirectorySeparatorChar))))
                {
                    continue;
                }

                found.Add(relative);
                config |= relative == ConfigName;
            }

            return found.Count == 0 ? DlcEmuInfo.Empty : new DlcEmuInfo(config, found);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DlcEmuInfo.Empty;
        }
    }

    public static DlcEmuInfo ScanImage(ExFatImage image, ExFatEntry appRoot, CancellationToken cancellationToken)
    {
        try
        {
            var found = new List<string>();
            var config = false;
            foreach (var relative in Candidates())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = image.Find(relative, appRoot);
                if (entry == null || entry.IsDirectory)
                {
                    continue;
                }

                found.Add(relative);
                config |= relative == ConfigName;
            }

            return found.Count == 0 ? DlcEmuInfo.Empty : new DlcEmuInfo(config, found);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return DlcEmuInfo.Empty;
        }
    }

    public static string Describe(DlcEmuInfo info) => info.Present ? string.Join(", ", info.Paths) : "—";
}
