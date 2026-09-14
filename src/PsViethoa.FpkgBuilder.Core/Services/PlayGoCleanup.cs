using PsViethoa.FpkgBuilder.Core.ExFat;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Bộ playgo* của bản dump (sce_sys/playgo-chunk.dat, playgo-hash-table.dat, playgo-ficm.dat, playgo-scenario.json…) mô tả bố cục
/// khối của gói gốc. Công cụ tạo ra một ảnh liền khối và thư viện tự sinh bộ PlayGo riêng, nên bộ cũ còn trong gói làm PS5 đi tìm
/// những khối không tồn tại: mở game là bật lỗi ứng dụng ("application error"), log báo lỗi "playgo". Drakmor (2026-09-14): "if the
/// games don't even launch and the log shows a playgo error, you need to delete the /sce_sys/playgo* files before packaging" và
/// "delete playgo solves the application error when starting the game, no black screen".
/// <para>
/// KHÁC với bệnh đứng ở màn hình splash mà không báo lỗi (Stellar Blade, Ghost of Yōtei): bệnh đó nằm ở kernel
/// (prevent_flat_search/APR), không sửa được từ khâu đóng gói, và bản dump của chúng thường không có tệp playgo* nào.
/// </para>
/// <para>
/// Lớp này CHỈ đụng vào dữ liệu trong <c>sce_sys</c>, không bao giờ đụng vào module trong <c>fakelib</c>. Riêng
/// <c>fakelib/libScePlayGo.sprx</c> (và <c>fakelib/libSceAmpr.sprx</c>) thì chính engine LibProsperoPkg tự loại khỏi gói —
/// đã kiểm chứng bằng cách tạo gói thật: hai tệp đó biến mất khỏi <c>fakelib</c> trong khi mọi module khác, và cả bản sao
/// trong <c>fakelib2</c>, vẫn còn. Công cụ không thêm cũng không ngăn được việc đó.
/// </para>
/// Lớp này chỉ liệt kê; việc đưa tệp ra ngoài lúc tạo gói do BuildEngine làm (không xoá gì trong thư mục nguồn).
/// </summary>
public static class PlayGoCleanup
{
    public const string Folder = "sce_sys";

    public const string Prefix = "playgo";

    /// <summary>
    /// Module stub PlayGo trong fakelib. Công cụ KHÔNG bao giờ đụng vào tệp này; engine LibProsperoPkg tự loại nó khỏi gói
    /// (giống fakelib/libSceAmpr.sprx). Hằng số này chỉ để đối chiếu và ghi chú.
    /// </summary>
    public const string EmuModule = "fakelib/libScePlayGo.sprx";

    private const string RelativePrefix = Folder + "/" + Prefix;

    /// <summary>Tên tệp thuộc bộ dữ liệu playgo (không phân biệt hoa thường).</summary>
    public static bool IsPlayGoFile(string name) => name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Đường dẫn tương đối dạng "sce_sys/playgo-…" (module fakelib/libScePlayGo.sprx không tính).</summary>
    public static bool IsCandidate(string relativePath) =>
        relativePath.Replace('\\', '/').StartsWith(RelativePrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Các tệp sce_sys/playgo* trong thư mục nguồn (đường dẫn tương đối, phân cách '/', đã sắp xếp).</summary>
    public static IReadOnlyList<string> ListFolder(string sourceFolder)
    {
        var sceSys = Path.Combine(sourceFolder, Folder);
        if (!Directory.Exists(sceSys))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.EnumerateFiles(sceSys)
                .Select(Path.GetFileName)
                .Where(name => name != null && IsPlayGoFile(name))
                .Select(name => Folder + "/" + name)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Các tệp sce_sys/playgo* trong ảnh exFAT (tính từ thư mục ứng dụng).</summary>
    public static IReadOnlyList<string> ListImage(ExFatImage image, ExFatEntry appRoot)
    {
        try
        {
            var sceSys = image.Find(Folder, appRoot);
            if (sceSys is not { IsDirectory: true })
            {
                return Array.Empty<string>();
            }

            return image.Enumerate(sceSys)
                .Where(entry => !entry.IsDirectory && IsPlayGoFile(entry.Name))
                .Select(entry => Folder + "/" + entry.Name)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Tên tệp gọn (bỏ tiền tố "sce_sys/") để ghi nhật ký.</summary>
    public static string Describe(IEnumerable<string> paths) =>
        string.Join(", ", paths.Select(path =>
        {
            var normalized = path.Replace('\\', '/');
            return normalized.StartsWith(Folder + "/", StringComparison.OrdinalIgnoreCase) ? normalized[(Folder.Length + 1)..] : normalized;
        }));
}
