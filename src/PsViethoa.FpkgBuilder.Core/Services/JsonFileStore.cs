using System.Text.Json;
using System.Text.Json.Serialization;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Đọc/ghi một object JSON ra tệp một cách an toàn (ghi tạm rồi đổi tên).</summary>
public static class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Thư mục dữ liệu ứng dụng theo hệ điều hành (Application Support / AppData).</summary>
    public static string AppDataDirectory(string appName)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrEmpty(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        return Path.Combine(root, appName);
    }

    public static T Load<T>(string path) where T : class, new()
    {
        try
        {
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                return JsonSerializer.Deserialize<T>(stream, Options) ?? new T();
            }
        }
        catch (Exception)
        {
            // Tệp hỏng không được phép chặn ứng dụng khởi động.
        }

        return new T();
    }

    public static bool Save<T>(string path, T value) where T : class
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            using (var stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, value, Options);
            }

            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
