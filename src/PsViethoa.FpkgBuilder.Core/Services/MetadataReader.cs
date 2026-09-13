using System.Text.Json;
using PsViethoa.FpkgBuilder.Core.ExFat;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Đọc sce_sys/param.json, icon và các tệp PlayGo từ thư mục nguồn hoặc từ ảnh exFAT.</summary>
public static class MetadataReader
{
    private const long MaxParamJsonBytes = 8L * 1024 * 1024;
    private const long MaxIconBytes = 16L * 1024 * 1024;

    public static SourceMetadata Read(string sourcePath, CancellationToken cancellationToken)
    {
        return SourceLocator.Detect(sourcePath) switch
        {
            SourceKind.ExFatImage => ReadImage(sourcePath, cancellationToken),
            _ => ReadFolder(sourcePath, cancellationToken),
        };
    }

    private static SourceMetadata ReadFolder(string sourceFolder, CancellationToken cancellationToken)
    {
        var metadata = new SourceMetadata();
        var sceSys = Path.Combine(sourceFolder, "sce_sys");
        metadata.HasSceSys = Directory.Exists(sceSys);
        metadata.HasEboot = File.Exists(Path.Combine(sourceFolder, "eboot.bin"));

        var paramPath = Path.Combine(sceSys, "param.json");
        if (File.Exists(paramPath))
        {
            metadata.HasParamJson = true;
            metadata.ParamJsonPath = paramPath;
            try
            {
                using var stream = File.OpenRead(paramPath);
                ReadParamJson(stream, metadata);
            }
            catch (JsonException ex)
            {
                metadata.HasParamJson = false;
                metadata.ParamJsonError = "param.json: " + ex.Message;
            }
            catch (IOException ex)
            {
                metadata.HasParamJson = false;
                metadata.ParamJsonError = "param.json: " + ex.Message;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var iconPath = Path.Combine(sceSys, "icon0.png");
        if (File.Exists(iconPath))
        {
            metadata.IconPath = iconPath;
        }

        metadata.HasPlayGoChunk = File.Exists(Path.Combine(sceSys, "playgo-chunk.dat"));
        metadata.HasPlayGoHashTable = File.Exists(Path.Combine(sceSys, "playgo-hash-table.dat"));
        metadata.HasPlayGoFicm = File.Exists(Path.Combine(sceSys, "playgo-ficm.dat"));
        metadata.HasPlayGoScenario = File.Exists(Path.Combine(sceSys, "playgo-scenario.json"));
        return metadata;
    }

    private static SourceMetadata ReadImage(string imagePath, CancellationToken cancellationToken)
    {
        var metadata = new SourceMetadata { IsExFat = true };
        using var image = ExFatImage.Open(imagePath);
        metadata.VolumeLabel = image.VolumeLabel;

        var appRoot = SourceLocator.FindAppRoot(image);
        if (appRoot == null)
        {
            metadata.HasSceSys = false;
            return metadata;
        }

        metadata.AppRootInImage = appRoot.Path.TrimStart('/');
        var children = image.Enumerate(appRoot).ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);
        metadata.HasEboot = children.TryGetValue("eboot.bin", out var eboot) && !eboot.IsDirectory;
        if (!children.TryGetValue("sce_sys", out var sceSys) || !sceSys.IsDirectory)
        {
            metadata.HasSceSys = false;
            return metadata;
        }

        metadata.HasSceSys = true;
        var system = image.Enumerate(sceSys).Where(e => !e.IsDirectory).ToDictionary(e => e.Name, e => e, StringComparer.OrdinalIgnoreCase);
        cancellationToken.ThrowIfCancellationRequested();

        if (system.TryGetValue("param.json", out var param) && param.Length <= MaxParamJsonBytes)
        {
            metadata.HasParamJson = true;
            metadata.ParamJsonPath = imagePath + "!/" + param.Path.TrimStart('/');
            try
            {
                using var stream = image.OpenRead(param);
                ReadParamJson(stream, metadata);
            }
            catch (JsonException ex)
            {
                metadata.HasParamJson = false;
                metadata.ParamJsonError = "param.json: " + ex.Message;
            }
        }

        if (system.TryGetValue("icon0.png", out var icon) && icon.Length > 0 && icon.Length <= MaxIconBytes)
        {
            try
            {
                metadata.IconBytes = image.ReadAllBytes(icon, MaxIconBytes);
            }
            catch (Exception)
            {
                metadata.IconBytes = null;
            }
        }

        metadata.HasPlayGoChunk = system.ContainsKey("playgo-chunk.dat");
        metadata.HasPlayGoHashTable = system.ContainsKey("playgo-hash-table.dat");
        metadata.HasPlayGoFicm = system.ContainsKey("playgo-ficm.dat");
        metadata.HasPlayGoScenario = system.ContainsKey("playgo-scenario.json");
        return metadata;
    }

    private static void ReadParamJson(Stream stream, SourceMetadata metadata)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("root is not an object");
        }

        metadata.ContentId = ReadString(root, "contentId");
        metadata.TitleId = ReadString(root, "titleId") ?? ContentIdHelper.TitleIdOf(metadata.ContentId);
        metadata.Version = ReadString(root, "contentVersion");
        metadata.SdkVersionRaw = ReadString(root, "sdkVersion");
        if (SdkVersions.TryReadMajor(metadata.SdkVersionRaw, out var major))
        {
            metadata.SdkMajor = major;
        }

        if (root.TryGetProperty("applicationCategoryType", out var category) && category.ValueKind == JsonValueKind.Number &&
            category.TryGetInt32(out var categoryValue))
        {
            metadata.CategoryType = categoryValue;
        }

        metadata.Title = ReadLocalizedTitle(root);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadLocalizedTitle(JsonElement root)
    {
        if (!root.TryGetProperty("localizedParameters", out var localized) || localized.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (localized.TryGetProperty("defaultLanguage", out var defaultLanguage) &&
            defaultLanguage.ValueKind == JsonValueKind.String &&
            defaultLanguage.GetString() is { } language &&
            localized.TryGetProperty(language, out var block) &&
            block.ValueKind == JsonValueKind.Object &&
            block.TryGetProperty("titleName", out var titleName) &&
            titleName.ValueKind == JsonValueKind.String)
        {
            return titleName.GetString();
        }

        foreach (var property in localized.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                property.Value.TryGetProperty("titleName", out var fallback) &&
                fallback.ValueKind == JsonValueKind.String)
            {
                return fallback.GetString();
            }
        }

        return null;
    }

    /// <summary>Mô tả trạng thái PlayGo.</summary>
    public static string DescribePlayGo(SourceMetadata metadata, int chunkCount)
    {
        var text = metadata.PlayGoFileCount == 0
            ? Loc.F("PlayGo.Auto", chunkCount)
            : metadata.HasCompletePlayGo
                ? Loc.T("PlayGo.Complete")
                : Loc.T("PlayGo.Partial");

        if (metadata.HasPlayGoScenario)
        {
            text += Loc.T("PlayGo.Scenario");
        }

        return text;
    }
}
