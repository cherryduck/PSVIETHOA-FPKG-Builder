namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Tìm libScePubTools.dll (Oodle gốc của Publishing Tools) – chỉ hoạt động trên Windows x64.</summary>
public static class PublishingToolsLocator
{
    public const string FileName = "libScePubTools.dll";
    public const string EnvironmentVariable = "LIBPROSPERO_PUBTOOLS_PATH";

    public static bool IsSupportedPlatform => OperatingSystem.IsWindows() && Environment.Is64BitProcess;

    public static IReadOnlyList<string> CandidatePaths(string? explicitPath)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            list.Add(explicitPath);
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            list.Add(fromEnvironment);
        }

        list.Add(Path.Combine(AppContext.BaseDirectory, FileName));
        list.Add(Path.Combine(AppContext.BaseDirectory, "libs", FileName));

        if (OperatingSystem.IsWindows())
        {
            list.Add(@"C:\SCE\Prospero\Tools\Publishing Tools\bin\" + FileName);
            var sdkRoot = Environment.GetEnvironmentVariable("SCE_PROSPERO_SDK_DIR");
            if (!string.IsNullOrWhiteSpace(sdkRoot))
            {
                list.Add(Path.Combine(sdkRoot, "..", "Tools", "Publishing Tools", "bin", FileName));
            }
        }

        return list;
    }

    /// <summary>Trả về đường dẫn DLL đầu tiên tồn tại, hoặc null.</summary>
    public static string? Find(string? explicitPath)
    {
        foreach (var candidate in CandidatePaths(explicitPath))
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch (Exception)
            {
            }
        }

        return null;
    }
}
