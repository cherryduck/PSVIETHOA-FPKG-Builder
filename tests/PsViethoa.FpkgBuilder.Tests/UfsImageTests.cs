using System.Text.Json;
using PsViethoa.FpkgBuilder.Core.ExFat;
using Xunit;

namespace PsViethoa.FpkgBuilder.Tests;

/// <summary>
/// Bộ đọc ảnh UFS2 — định dạng của tệp .ffpkg. Không hệ điều hành nào trong hai hệ đích gắn được UFS (macOS bỏ từ 10.7,
/// Windows chưa từng có), nên toàn bộ việc phân giải nằm ở đây và phải đúng tuyệt đối.
/// </summary>
public sealed class UfsImageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "psviethoa-ufs-" + Guid.NewGuid().ToString("N"));

    public UfsImageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Bản dump .ffpkg thật của người dùng; bỏ qua khi máy không có.</summary>
    private static string? RealSample
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads", "PPSA02494-ffpkg 01.003", "PPSA02494-ffpkg", "PPSA02494.ffpkg");
            return File.Exists(path) ? path : null;
        }
    }

    [Fact]
    public void HasExtension_MatchesOnlyFfpkg()
    {
        Assert.True(UfsImage.HasExtension("game.ffpkg"));
        Assert.True(UfsImage.HasExtension("/x/GAME.FFPKG"));
        Assert.False(UfsImage.HasExtension("game.exfat"));
        Assert.False(UfsImage.HasExtension("game.pkg"));
        Assert.False(UfsImage.HasExtension("game.ffpfsc"));
    }

    [Fact]
    public void IsUfsFile_RejectsAnythingWithoutTheMagic()
    {
        var junk = Path.Combine(_root, "junk.ffpkg");
        File.WriteAllBytes(junk, new byte[128 * 1024]);
        Assert.False(UfsImage.IsUfsFile(junk));

        var tiny = Path.Combine(_root, "tiny.ffpkg");
        File.WriteAllBytes(tiny, new byte[16]);
        Assert.False(UfsImage.IsUfsFile(tiny));
        Assert.False(UfsImage.IsUfsFile(Path.Combine(_root, "missing.ffpkg")));

        Assert.Throws<InvalidDataException>(() => UfsImage.Open(junk));
    }

    [Fact]
    public void IsUfsFile_AcceptsTheMagicAtTheDocumentedOffset()
    {
        // Magic 0x19540119 ở byte 65536 + 1372 là dấu hiệu nhận dạng duy nhất của UFS2.
        var path = Path.Combine(_root, "magic.ffpkg");
        var bytes = new byte[UfsImage.SuperBlockOffset + 2048];
        BitConverter.GetBytes(UfsImage.Magic).CopyTo(bytes, UfsImage.SuperBlockOffset + UfsImage.MagicOffset);
        File.WriteAllBytes(path, bytes);

        Assert.True(UfsImage.IsUfsFile(path));

        // Nhận ra magic nhưng hình học vô lý thì phải từ chối, không được đọc bừa.
        Assert.Throws<InvalidDataException>(() => UfsImage.Open(path));
    }

    [Fact]
    public void RealSample_WhenPresent_ReadsTheWholeAppFolder()
    {
        if (RealSample is not { } path)
        {
            return;
        }

        Assert.True(UfsImage.IsUfsFile(path));
        using var image = UfsImage.Open(path);

        // Hình học phải khớp: số mảnh nhân kích thước mảnh bằng đúng kích thước tệp.
        Assert.Equal(65536, image.BlockSize);
        Assert.Equal(image.BlockSize, image.FragmentSize);
        Assert.Equal(new FileInfo(path).Length, image.VolumeLengthBytes);
        Assert.Equal(image.BlockSize / 256, image.InodesPerBlock);

        // Thư mục gốc của ảnh chính là thư mục ứng dụng.
        var root = image.Enumerate(image.Root).ToList();
        Assert.Contains(root, e => e.Name == "eboot.bin" && !e.IsDirectory && e.Length > 0);
        Assert.Contains(root, e => e.Name == "sce_sys" && e.IsDirectory);
        Assert.Contains(root, e => e.Name == "sce_module" && e.IsDirectory);
        Assert.DoesNotContain(root, e => e.Name is "." or "..");

        // Đường dẫn nhiều cấp và nội dung tệp thật.
        var param = image.Find("sce_sys/param.json");
        Assert.NotNull(param);
        Assert.False(param!.IsDirectory);
        using var document = JsonDocument.Parse(image.ReadAllBytes(param));
        Assert.Equal("PPSA02494", document.RootElement.GetProperty("titleId").GetString());
        Assert.Equal("JP0700-PPSA02494_00-DRFM2MAINCONTENT", document.RootElement.GetProperty("contentId").GetString());

        // Tìm không phân biệt hoa thường, và đường dẫn không tồn tại trả về null.
        Assert.NotNull(image.Find("SCE_SYS/PARAM.JSON"));
        Assert.Null(image.Find("sce_sys/khong-co-that.dat"));
        Assert.Null(image.Find("eboot.bin/con-cua-tep"));
    }

    [Fact]
    public void RealSample_WhenPresent_StreamsFilesLargerThanOneBlock()
    {
        if (RealSample is not { } path)
        {
            return;
        }

        using var image = UfsImage.Open(path);
        var eboot = image.Find("eboot.bin");
        Assert.NotNull(eboot);
        Assert.True(eboot!.Length > image.BlockSize, "eboot.bin phải lớn hơn một khối để kiểm tra con trỏ gián tiếp.");

        // Đọc cả tệp bằng hai đường và so khớp: luồng phải cho ra đúng byte như đọc một lần.
        var whole = image.ReadAllBytes(eboot, 64L * 1024 * 1024);
        Assert.Equal(eboot.Length, whole.Length);

        using var stream = image.OpenRead(eboot);
        Assert.Equal(eboot.Length, stream.Length);
        var streamed = new byte[eboot.Length];
        var read = 0;
        while (read < streamed.Length)
        {
            var got = stream.Read(streamed, read, streamed.Length - read);
            Assert.True(got > 0, "luồng dừng sớm trước khi hết tệp.");
            read += got;
        }

        Assert.Equal(whole, streamed);

        // eboot.bin của PS5 mở đầu bằng 4F 15 3D 1D (tệp thực thi đã ký của Sony), không phải 7F 'E' 'L' 'F'.
        Assert.Equal(new byte[] { 0x4F, 0x15, 0x3D, 0x1D }, whole.Take(4).ToArray());

        // Đọc từ giữa tệp cũng phải đúng.
        stream.Seek(image.BlockSize + 1234, SeekOrigin.Begin);
        var middle = new byte[64];
        stream.ReadExactly(middle);
        Assert.Equal(whole.Skip(image.BlockSize + 1234).Take(64).ToArray(), middle);
    }

    [Fact]
    public void RealSample_WhenPresent_WalksEveryFile()
    {
        if (RealSample is not { } path)
        {
            return;
        }

        using var image = UfsImage.Open(path);
        var files = 0;
        var directories = 0;
        var bytes = 0L;
        foreach (var entry in image.Walk(image.Root))
        {
            if (entry.IsDirectory)
            {
                directories++;
            }
            else
            {
                files++;
                bytes += entry.Length;
            }
        }

        // Bản dump này có 17.407 tệp trong 27 thư mục và 2,34 GB dữ liệu — đếm bằng một bộ đọc UFS2 độc lập viết riêng
        // để đối chứng, nên con số này kiểm tra được bộ đọc chứ không phải chép lại kết quả của chính nó.
        Assert.Equal(17407, files);
        Assert.Equal(27, directories);
        Assert.Equal(2343308017L, bytes);
        Assert.True(bytes < image.VolumeLengthBytes);
    }
}
