using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using LibProsperoPkg.PKG;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>
/// Đọc thông tin một tệp PKG PS5: loại container, header FIH, bản đồ vùng, header CNT + bảng entry,
/// param.json (mọi trường), icon0.png và danh sách tệp sce_sys — không mở ảnh trong.
/// </summary>
public static class PackageInspector
{
    private const int FihRegionSize = 65536;
    private const int MaxParamJsonBytes = 8 * 1024 * 1024;
    private const int MaxIconBytes = 16 * 1024 * 1024;

    // Loại nội dung theo bảng của thư viện (giá trị số trong header CNT) → khoá chuỗi (dịch lúc hiển thị để đổi ngôn ngữ vẫn đúng).
    private static readonly Lazy<Dictionary<uint, string>> ContentTypeKeys = new(() =>
    {
        var map = new Dictionary<uint, string>();
        try
        {
            map[ProsperoPkgBuilder.ContentTypeFor(ProsperoVolumeType.Application)] = "Extract.ContentApp";
            map[ProsperoPkgBuilder.ContentTypeFor(ProsperoVolumeType.AdditionalContentData)] = "Extract.ContentAcData";
            map[ProsperoPkgBuilder.ContentTypeFor(ProsperoVolumeType.AdditionalContentNoData)] = "Extract.ContentAcNoData";
        }
        catch (Exception)
        {
            // Chỉ là nhãn bổ sung.
        }

        return map;
    });

    public static PackageInfo Inspect(string packagePath, string passcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException(Loc.T("Extract.FileMissing"), packagePath);
        }

        var fileInfo = new FileInfo(packagePath);
        if (fileInfo.Length < 4096)
        {
            throw new InvalidDataException(Loc.T("Extract.NotPkg"));
        }

        ProsperoPkgType? detected;
        try
        {
            detected = ProsperoPkgReader.DetectType(packagePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(Loc.T("Extract.NotPkg"), ex);
        }

        var kind = detected switch
        {
            ProsperoPkgType.Meta => PackageContainerKind.Meta,
            ProsperoPkgType.FullRetail => PackageContainerKind.FullRetail,
            ProsperoPkgType.FullDebug => PackageContainerKind.FullDebug,
            _ => throw new InvalidDataException(Loc.T("Extract.NotPkg")),
        };

        cancellationToken.ThrowIfCancellationRequested();

        PackageMapInfo? map = null;
        try
        {
            var m = ProsperoPackageArchive.Inspect(packagePath);
            map = new PackageMapInfo(m.FihOffset, m.FihSize, m.OuterPfsOffset, m.OuterPfsSize, m.CntOffset, m.CntSize, m.SupplementOffset, m.SupplementSize, m.OuterSuperblockIndex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Gói Meta (chỉ CNT) không có bản đồ FIH; các gói lạ cũng bỏ qua.
        }

        PackageFihInfo? fih = null;
        var imageMode = PackageImageMode.Unknown;
        ProsperoPkg package;
        try
        {
            package = ProsperoPkgReader.Read(packagePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidDataException(Loc.F("Extract.ReadFailed", ex.Message), ex);
        }

        using (var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.RandomAccess))
        {
            if (kind != PackageContainerKind.Meta)
            {
                var summary = PackageVerifier.ReadFihSummary(stream);
                var raw = new byte[FihRegionSize];
                stream.Position = 0;
                var read = stream.Read(raw, 0, raw.Length);
                if (read < 256)
                {
                    throw new InvalidDataException(Loc.T("Verify.TooSmall"));
                }

                imageMode = string.Equals(summary.Marker, PackageVerifier.PlaintextMarker, StringComparison.Ordinal)
                    ? PackageImageMode.PlaintextNoAuth
                    : PackageImageMode.Native;

                var lib = package.Fih;
                fih = new PackageFihInfo(
                    summary.SignedByte,
                    lib?.IsOfficial ?? summary.SignedByte != 0,
                    lib?.PfsImageOffset ?? BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihPfsImageOffsetField)),
                    lib?.PfsImageSize ?? BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihPfsImageSizeField)),
                    lib?.EmbeddedCntOffset ?? BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihEmbeddedCntOffsetField)),
                    lib?.InnerImageBlockCount ?? BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihInnerImageBlockCountField)),
                    lib?.MetadataBlockCount ?? BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihMetaBlockCountField)),
                    lib?.NapsLayoutSize ?? BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihInnerImageLogicalSizeField)),
                    BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihDataRegionBlockCountField)),
                    BinaryPrimitives.ReadUInt64LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihInnerImageSizeField)),
                    BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihOuterFileCountField)),
                    BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihSparseAfidCountField)),
                    BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(ProsperoPkgLayout.FihEmptyFileCountField)),
                    summary.OuterSuperblockOffset,
                    summary.OuterMode,
                    imageMode == PackageImageMode.PlaintextNoAuth ? summary.Marker : null);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Header CNT + bảng entry (thư viện đã đọc từ CNT nhúng).
            PackageCntInfo? cnt = null;
            var entries = new List<PackageCntEntry>();
            if (package.Header != null)
            {
                foreach (var e in package.Entries ?? Array.Empty<ProsperoPkgEntry>())
                {
                    entries.Add(new PackageCntEntry(e.Id.ToString(), e.RawId, e.Name ?? string.Empty, e.DataOffset, e.DataSize, e.Encrypted, e.KeyIndex, e.Flags1, e.Flags2));
                }

                bool? wrapValid = null;
                if (kind != PackageContainerKind.Meta)
                {
                    try
                    {
                        wrapValid = ProsperoPackageArchive.VerifyCntMetadataSignature(packagePath);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        wrapValid = null;
                    }
                }

                cnt = new PackageCntInfo(
                    package.Header.ContentId ?? string.Empty,
                    package.Header.DrmType,
                    package.Header.ContentType,
                    package.Header.Flags,
                    package.Header.EntryCount,
                    package.Header.ScEntryCount,
                    package.Header.BodyOffset,
                    package.Header.BodySize,
                    entries,
                    wrapValid);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // param.json và icon0.png: đọc thẳng từ vùng CNT khi entry không mã hoá; nếu mã hoá thì nhờ thư viện giải mã ra thư mục tạm.
            var cntBase = kind == PackageContainerKind.Meta ? 0 : map?.CntOffset ?? (long)(fih?.EmbeddedCntOffset ?? 0);
            byte[]? paramBytes = null;
            byte[]? iconBytes = null;
            string? paramError = null;
            var paramEntry = entries.FirstOrDefault(e => string.Equals(e.Name, "param.json", StringComparison.OrdinalIgnoreCase));
            var iconEntry = entries.FirstOrDefault(e => string.Equals(e.Name, "icon0.png", StringComparison.OrdinalIgnoreCase));
            var needsDecrypt = (paramEntry?.Encrypted ?? false) || (iconEntry?.Encrypted ?? false);
            if (!needsDecrypt)
            {
                paramBytes = ReadEntry(stream, cntBase, paramEntry, MaxParamJsonBytes);
                iconBytes = ReadEntry(stream, cntBase, iconEntry, MaxIconBytes);
            }
            else
            {
                try
                {
                    var exported = PackageReader.ExportCntEntriesToTemp(packagePath, passcode, cancellationToken);
                    try
                    {
                        paramBytes = ReadExported(exported, "param.json", MaxParamJsonBytes);
                        iconBytes = ReadExported(exported, "icon0.png", MaxIconBytes);
                    }
                    finally
                    {
                        BuildEngine.TryDeleteDirectory(exported);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    paramError = Loc.F("Extract.CntDecryptFailed", ex.Message);
                }
            }

            PackageParams? parameters = null;
            if (paramBytes != null)
            {
                try
                {
                    parameters = ParseParamJson(paramBytes);
                }
                catch (JsonException ex)
                {
                    paramError = "param.json: " + ex.Message;
                }
            }

            return new PackageInfo
            {
                Path = fileInfo.FullName,
                FileSize = fileInfo.Length,
                Kind = kind,
                ImageMode = imageMode,
                Fih = fih,
                Map = map,
                Cnt = cnt,
                Params = parameters,
                ParamJsonError = paramError,
                ParamJsonBytes = paramBytes,
                IconBytes = iconBytes,
                SceSysFiles = entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.Name).ToArray(),
            };
        }
    }

    /// <summary>Tính SHA-256 toàn bộ tệp .pkg (tiến độ 0..100).</summary>
    public static string ComputeSha256(string packagePath, CancellationToken cancellationToken, Action<double>? progress = null)
    {
        using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4 << 20, FileOptions.SequentialScan);
        return PackageVerifier.ComputeSha256(stream, cancellationToken, progress);
    }

    /// <summary>Nhãn loại nội dung theo giá trị trong header CNT.</summary>
    public static string DescribeContentType(uint contentType) =>
        ContentTypeKeys.Value.TryGetValue(contentType, out var key)
            ? $"{Loc.T(key)} (0x{contentType:X})"
            : $"0x{contentType:X}";

    /// <summary>Nhãn DRM theo giá trị trong header CNT.</summary>
    public static string DescribeDrmType(uint drmType) =>
        drmType == 0 ? Loc.F("Extract.DrmNone", drmType) : $"0x{drmType:X}";

    /// <summary>Loại ứng dụng suy ra từ applicationCategoryType trong param.json.</summary>
    public static string DescribeCategory(int? category) => category switch
    {
        null => "—",
        0 => Loc.T("Extract.CategoryGame"),
        _ => Loc.F("Extract.CategoryOther", category.Value, category.Value.ToString("X", CultureInfo.InvariantCulture)),
    };

    /// <summary>Phân tích param.json: mọi trường vô hướng cấp 1 (và các trường lồng một cấp) theo thứ tự + các giá trị suy ra.</summary>
    public static PackageParams ParseParamJson(ReadOnlyMemory<byte> json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("root is not an object");
        }

        var fields = new List<ParamField>();
        string? title = null;
        string? defaultLanguage = null;
        foreach (var property in root.EnumerateObject())
        {
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object when property.NameEquals("localizedParameters"):
                    if (property.Value.TryGetProperty("defaultLanguage", out var dl) && dl.ValueKind == JsonValueKind.String)
                    {
                        defaultLanguage = dl.GetString();
                        fields.Add(new ParamField("localizedParameters.defaultLanguage", defaultLanguage ?? string.Empty));
                    }

                    foreach (var language in property.Value.EnumerateObject())
                    {
                        if (language.Value.ValueKind == JsonValueKind.Object &&
                            language.Value.TryGetProperty("titleName", out var titleName) &&
                            titleName.ValueKind == JsonValueKind.String)
                        {
                            var text = titleName.GetString() ?? string.Empty;
                            fields.Add(new ParamField($"titleName ({language.Name})", text));
                            if (title == null || string.Equals(language.Name, defaultLanguage, StringComparison.OrdinalIgnoreCase))
                            {
                                title = text;
                            }
                        }
                    }

                    break;
                case JsonValueKind.Object:
                    foreach (var child in property.Value.EnumerateObject())
                    {
                        fields.Add(new ParamField(property.Name + "." + child.Name, ScalarText(child.Value)));
                    }

                    break;
                case JsonValueKind.Array:
                    fields.Add(new ParamField(property.Name, ScalarText(property.Value)));
                    break;
                default:
                    fields.Add(new ParamField(property.Name, ScalarText(property.Value)));
                    break;
            }
        }

        var sdkVersion = ReadString(root, "sdkVersion");
        int? sdkMajor = SdkVersions.TryReadMajor(sdkVersion, out var major) ? major : null;
        var contentId = ReadString(root, "contentId");
        return new PackageParams
        {
            Fields = fields,
            ContentId = contentId,
            TitleId = ReadString(root, "titleId") ?? ContentIdHelper.TitleIdOf(contentId),
            Title = title,
            DefaultLanguage = defaultLanguage,
            ContentVersion = ReadString(root, "contentVersion"),
            MasterVersion = ReadString(root, "masterVersion"),
            SdkVersion = sdkVersion,
            SdkMajor = sdkMajor,
            RequiredSystemSoftwareVersion = ReadString(root, "requiredSystemSoftwareVersion"),
            ApplicationCategoryType = ReadInt(root, "applicationCategoryType"),
            ApplicationDrmType = ReadString(root, "applicationDrmType"),
            ParentalLevel = ReadInt(root, "parentalLevel") ?? ReadInt(root, "ageLevel"),
            ContentBadgeType = ReadScalarText(root, "contentBadgeType"),
        };
    }

    // Chuỗi bị đệm khoảng trắng (trình tạo gói đệm versionFileUri, serviceIdForSharing…) được cắt gọn để hiển thị;
    // object/array được ghi lại ở dạng JSON một dòng.
    private static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => (value.GetString() ?? string.Empty).Trim(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Object or JsonValueKind.Array => JsonSerializer.Serialize(value),
        _ => value.GetRawText(),
    };

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? ReadScalarText(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array) ? ScalarText(value) : null;

    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static byte[]? ReadEntry(Stream stream, long cntBase, PackageCntEntry? entry, int maxBytes)
    {
        if (entry == null || entry.DataSize == 0 || entry.DataSize > maxBytes)
        {
            return null;
        }

        var position = cntBase + entry.DataOffset;
        if (position < 0 || position + entry.DataSize > stream.Length)
        {
            return null;
        }

        var buffer = new byte[entry.DataSize];
        stream.Position = position;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static byte[]? ReadExported(string folder, string name, int maxBytes)
    {
        var path = Path.Combine(folder, name);
        if (!File.Exists(path))
        {
            return null;
        }

        var info = new FileInfo(path);
        return info.Length == 0 || info.Length > maxBytes ? null : File.ReadAllBytes(path);
    }

    /// <summary>Văn bản tóm tắt để sao chép/in ra console (General + Params).</summary>
    public static string Describe(PackageInfo info, string? sha256 = null)
    {
        var text = new StringBuilder();
        text.AppendLine(Loc.T("Extract.SectionGeneral"));
        foreach (var (key, value) in GeneralRows(info, sha256))
        {
            text.Append("  ").Append(key).Append(": ").AppendLine(value);
        }

        if (info.Params != null)
        {
            text.AppendLine();
            text.AppendLine(Loc.T("Extract.SectionParams"));
            foreach (var field in info.Params.Fields)
            {
                text.Append("  ").Append(field.Key).Append(": ").AppendLine(field.Value);
            }
        }
        else if (info.ParamJsonError != null)
        {
            text.AppendLine(info.ParamJsonError);
        }

        return text.ToString();
    }

    /// <summary>Các hàng "General" (đã dịch) cho giao diện và CLI.</summary>
    public static IReadOnlyList<(string Key, string Value)> GeneralRows(PackageInfo info, string? sha256 = null)
    {
        var rows = new List<(string, string)>
        {
            (Loc.T("Extract.RowFile"), info.Path),
            (Loc.T("Extract.RowSize"), Formatters.SizeWithBytes(info.FileSize)),
            (Loc.T("Extract.RowKind"), info.KindLabel),
        };

        if (info.Fih != null)
        {
            rows.Add((Loc.T("Extract.RowImageMode"), info.ImageModeLabel + (info.Fih.Marker != null ? " · " + info.Fih.Marker.Trim('\0') : string.Empty)));
            rows.Add((Loc.T("Extract.RowFihSigned"), $"0x{info.Fih.SignedByte:X2} · " + Loc.T(info.Fih.IsOfficial ? "Extract.Official" : "Extract.Debug")));
            rows.Add((Loc.T("Extract.RowOuterPfs"), Loc.F("Extract.OffsetSize", info.Fih.PfsImageOffset, Formatters.SizeWithBytes((long)info.Fih.PfsImageSize)) + $" · mode 0x{info.Fih.OuterPfsMode:X4} · superblock @ {info.Fih.OuterSuperblockOffset:N0}"));
            // Trường kích thước trong FIH khác nhau giữa các bản thư viện (vật lý/logic) nên hiển thị theo số khối NAPS (64 KiB) — luôn nhất quán.
            rows.Add((Loc.T("Extract.RowInnerImage"), Loc.F("Extract.InnerImageText", Formatters.Size((long)info.Fih.InnerImageBlockCount * 65536), info.Fih.InnerImageBlockCount, info.Fih.DataRegionBlockCount, info.Fih.MetadataBlockCount)));
            rows.Add((Loc.T("Extract.RowCounts"), Loc.F("Extract.CountsText", info.Fih.OuterFileCount, info.Fih.SparseAfidCount, info.Fih.EmptyFileCount, info.Fih.NapsLayoutSize)));
        }

        if (info.Map != null)
        {
            rows.Add((Loc.T("Extract.RowMap"), Loc.F("Extract.MapText",
                info.Map.FihOffset, info.Map.FihSize,
                info.Map.OuterPfsOffset, info.Map.OuterPfsSize,
                info.Map.CntOffset, info.Map.CntSize,
                info.Map.SupplementOffset, info.Map.SupplementSize)));
        }

        if (info.Cnt != null)
        {
            rows.Add(("Content ID", string.IsNullOrEmpty(info.Cnt.ContentId) ? "—" : info.Cnt.ContentId));
            rows.Add((Loc.T("Extract.RowContentType"), DescribeContentType(info.Cnt.ContentType)));
            rows.Add((Loc.T("Extract.RowDrm"), DescribeDrmType(info.Cnt.DrmType)));
            rows.Add((Loc.T("Extract.RowCntFlags"), $"0x{info.Cnt.Flags:X8} · {Loc.F("Extract.EntriesText", info.Cnt.EntryCount, info.Cnt.ScEntryCount)} · body @ {info.Cnt.BodyOffset:N0} ({Formatters.Size((long)info.Cnt.BodySize)})"));
            if (info.Cnt.MetadataWrapValid is { } valid)
            {
                rows.Add((Loc.T("Extract.RowCntWrap"), Loc.T(valid ? "Extract.WrapValid" : "Extract.WrapInvalid")));
            }

            rows.Add(("sce_sys", info.SceSysFiles.Count == 0 ? "—" : string.Join(", ", info.SceSysFiles)));
        }

        if (info.Params != null)
        {
            rows.Add((Loc.T("Extract.RowTitle"), info.Params.Title ?? "—"));
            rows.Add((Loc.T("Extract.RowCategory"), info.Params.CategoryLabel + (info.Params.ApplicationDrmType != null ? " · DRM " + info.Params.ApplicationDrmType : string.Empty)));
            rows.Add((Loc.T("Extract.RowVersion"), Loc.F("Extract.VersionText", info.Params.ContentVersion ?? "—", info.Params.MasterVersion ?? "—")));
            rows.Add((Loc.T("Extract.RowSdk"), (info.Params.SdkVersion ?? "—") + (info.Params.SdkMajor is { } major ? $" (SDK {major})" : string.Empty)));
            rows.Add((Loc.T("Extract.RowSystem"), info.Params.RequiredSystemSoftwareVersion ?? "—"));
            if (info.Params.ParentalLevel is { } parental)
            {
                rows.Add((Loc.T("Extract.RowParental"), parental.ToString(CultureInfo.InvariantCulture)));
            }
        }

        rows.Add(("SHA-256", sha256 ?? Loc.T("Extract.ShaNotComputed")));
        return rows;
    }
}
