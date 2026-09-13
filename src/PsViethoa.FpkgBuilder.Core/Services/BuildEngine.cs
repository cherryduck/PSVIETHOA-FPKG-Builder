using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LibProsperoPkg;
using PsViethoa.FpkgBuilder.Core.ExFat;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;

namespace PsViethoa.FpkgBuilder.Core.Services;

/// <summary>Bọc LibProsperoPkg: chuẩn bị nguồn (thư mục hoặc ảnh exFAT), tạo gói, theo dõi tiến trình, kiểm tra kết quả.</summary>
public sealed class BuildEngine
{
    public static string LibraryVersion =>
        typeof(ProsperoPackageBuilder).Assembly.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Khoá debug PS5 có sẵn trong thư viện hay không (không có thì không thể tạo gói).</summary>
    public static bool KeysAvailable
    {
        get
        {
            try
            {
                return ProsperoPackageBuilder.KeysAvailable;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public async Task<BuildOutcome> BuildAsync(
        BuildRequest request,
        Action<LogEntry> log,
        IProgress<BuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalized = BuildPreparer.Normalize(request);
        var errors = BuildPreparer.Validate(normalized);
        if (errors.Count > 0)
        {
            throw new BuildValidationException(errors);
        }

        var source = await Task.Run(() => SourceLocator.Resolve(normalized.SourcePath), cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(normalized.OutputFolder);
        Directory.CreateDirectory(normalized.TemporaryFolder);

        var backend = BuildPreparer.ResolveBackend(normalized, out var publishingToolsPath);
        var strategy = source.IsExFat
            ? await Task.Run(() => DecideStrategy(normalized, source, log), cancellationToken).ConfigureAwait(false)
            : ExFatStrategy.Auto;
        var exFatPhase = source.IsExFat ? (strategy == ExFatStrategy.Mount ? PhaseCatalog.Mount : PhaseCatalog.Extract) : null;

        var stopwatch = Stopwatch.StartNew();
        var tracker = new ProgressTracker(stopwatch, PhaseCatalog.Sequence(normalized.ComputeSha256, exFatPhase));
        LogPlan(normalized, source, backend, publishingToolsPath, log);
        progress?.Report(tracker.Current);

        using var sleepGuard = normalized.PreventSleep ? SleepInhibitor.TryAcquire() : null;
        if (sleepGuard != null)
        {
            log(new LogEntry(LogLevel.Info, Loc.F("Plan.SleepGuard", sleepGuard.Mechanism)));
        }

        ExFatMount? mount = null;
        string? staging = null;
        try
        {
            var sourceFolder = source.Path;
            if (source.IsExFat)
            {
                progress?.Report(tracker.EnterPhase(exFatPhase!));
                if (strategy == ExFatStrategy.Mount)
                {
                    try
                    {
                        mount = await Task.Run(() => ExFatMounter.Mount(source.Path, cancellationToken), cancellationToken).ConfigureAwait(false);
                        sourceFolder = Path.Combine(mount.MountPoint, source.AppRootInImage.Replace('/', Path.DirectorySeparatorChar));
                        log(new LogEntry(LogLevel.Info, Loc.F("Plan.Mounted", mount.MountPoint)));
                        progress?.Report(tracker.UpdatePhasePercent(100));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        log(new LogEntry(LogLevel.Warning, Loc.F("Plan.MountFailed", ex.Message)));
                        strategy = ExFatStrategy.Extract;
                    }
                }

                if (strategy == ExFatStrategy.Extract)
                {
                    staging = Path.Combine(normalized.TemporaryFolder, "exfat-" + StagingName(source.Path));
                    TryDeleteDirectory(staging);
                    var extractionWatch = Stopwatch.StartNew();
                    var plan = await Task.Run(() =>
                    {
                        using var image = ExFatImage.Open(source.Path);
                        var root = SourceLocator.ResolveAppRoot(image, source);
                        var extractionPlan = ExFatExtractor.CreatePlan(image, root, skipJunk: true, cancellationToken);
                        log(new LogEntry(LogLevel.Info, Loc.F("Plan.Extracting", extractionPlan.Files.Count, Formatters.Size(extractionPlan.TotalBytes), staging)));
                        ExFatExtractor.Extract(
                            image,
                            extractionPlan,
                            staging,
                            (done, total) => progress?.Report(tracker.UpdatePhasePercent(done * 100.0 / Math.Max(1, total))),
                            cancellationToken);
                        return extractionPlan;
                    }, cancellationToken).ConfigureAwait(false);

                    var skipped = plan.SkippedJunk > 0 ? Loc.F("Plan.SkippedJunk", plan.SkippedJunk) : string.Empty;
                    log(new LogEntry(LogLevel.Info, Loc.F("Plan.Extracted", Formatters.Duration(extractionWatch.Elapsed), skipped)));
                    sourceFolder = staging;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var options = CreateOptions(normalized, sourceFolder, backend, publishingToolsPath, cancellationToken);

            var result = await Task.Run(
                () => ProsperoPackageBuilder.Build(options, message =>
                {
                    log(LogEntry.FromLibrary(message));
                    if (tracker.TryUpdate(message, out var snapshot))
                    {
                        progress?.Report(snapshot);
                    }
                }),
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            log(new LogEntry(LogLevel.Info, Loc.T(normalized.ComputeSha256 ? "Plan.VerifyingSha" : "Plan.Verifying")));
            progress?.Report(tracker.EnterPhase(normalized.ComputeSha256 ? PhaseCatalog.Sha256 : PhaseCatalog.Verify));

            var verification = await Task.Run(
                () => PackageVerifier.Verify(
                    result.OutputPath,
                    normalized.ImageMode,
                    normalized.ComputeSha256,
                    cancellationToken,
                    percent =>
                    {
                        var snapshot = tracker.UpdatePhasePercent(percent);
                        if (percent >= 100)
                        {
                            snapshot = tracker.EnterPhase(PhaseCatalog.Verify);
                        }

                        progress?.Report(snapshot);
                    }),
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            progress?.Report(tracker.Complete());

            var warnings = result.Warnings is { } list ? list.ToArray() : Array.Empty<string>();
            return new BuildOutcome(result.OutputPath, warnings, verification, stopwatch.Elapsed);
        }
        finally
        {
            if (mount != null)
            {
                await Task.Run(mount.Dispose).ConfigureAwait(false);
                log(new LogEntry(LogLevel.Info, Loc.T("Plan.Unmounted")));
            }

            if (staging != null)
            {
                await Task.Run(() => TryDeleteDirectory(staging)).ConfigureAwait(false);
                log(new LogEntry(LogLevel.Info, Loc.T("Plan.StagingRemoved")));
            }
        }
    }

    /// <summary>Chọn cách xử lý ảnh exFAT: gắn (macOS, ảnh sạch) hoặc giải nén.</summary>
    public static ExFatStrategy DecideStrategy(BuildRequest request, SourceInfo source, Action<LogEntry>? log)
    {
        switch (request.ExFat)
        {
            case ExFatStrategy.Extract:
                return ExFatStrategy.Extract;
            case ExFatStrategy.Mount:
                return ExFatMounter.IsAvailable ? ExFatStrategy.Mount : ExFatStrategy.Extract;
            default:
                if (!ExFatMounter.IsAvailable)
                {
                    return ExFatStrategy.Extract;
                }

                var junk = JunkFileFinder.Find(source.Path, CancellationToken.None);
                if (junk.Count > 0)
                {
                    log?.Invoke(new LogEntry(LogLevel.Info, Loc.T("Plan.MountJunk")));
                    return ExFatStrategy.Extract;
                }

                return ExFatStrategy.Mount;
        }
    }

    public static ProsperoBuildOptions CreateOptions(
        BuildRequest request,
        string sourceFolder,
        KrakenBackendKind backend,
        string? publishingToolsPath,
        CancellationToken cancellationToken)
    {
        var titleId = ContentIdHelper.TitleIdOf(request.ContentId)
                      ?? throw new BuildValidationException([new ValidationError(BuildPreparer.FieldContentId, Loc.T("Val.ContentIdInvalid"))]);

        return new ProsperoBuildOptions
        {
            SourceFolder = sourceFolder,
            OutputFolder = request.OutputFolder,
            TemporaryDirectory = request.TemporaryFolder,
            ContentId = request.ContentId,
            PrimaryId = request.ContentId,
            TitleId = titleId,
            Title = request.Title,
            Version = request.Version,
            Passcode = request.Passcode,
            Mode = request.Kind switch
            {
                PackageKind.Homebrew => ProsperoPackageMode.Homebrew,
                PackageKind.DlcWithData => ProsperoPackageMode.AdditionalContentData,
                _ => ProsperoPackageMode.Application,
            },
            OutputFormat = ProsperoOutputFormat.DebugImage,
            UsePublisherPprNaps = true,
            KrakenCompressionLevel = request.KrakenLevel,
            KrakenMaxDegreeOfParallelism = request.Threads,
            KrakenBackend = backend switch
            {
                KrakenBackendKind.PublishingTools => ProsperoKrakenBackend.PublishingToolsRequired,
                KrakenBackendKind.Uncompressed => ProsperoKrakenBackend.Uncompressed,
                _ => ProsperoKrakenBackend.BuiltIn,
            },
            PublishingToolsLibraryPath = backend == KrakenBackendKind.PublishingTools ? publishingToolsPath : null,
            PlayGoChunkCount = request.PlayGoChunks,
            SdkVersionOverride = request.SdkMajorOverride is { } major ? SdkVersions.Get(major)?.ExecutableVersion : null,
            PublisherImageMode = request.ImageMode == OuterImageMode.Native
                ? ProsperoPublisherImageMode.Native
                : ProsperoPublisherImageMode.PlaintextNoAuth,
            DeterministicBuild = request.Deterministic,
            GenerateParamJsonIfMissing = true,
            CancellationToken = cancellationToken,
        };
    }

    private static void LogPlan(BuildRequest request, SourceInfo source, KrakenBackendKind backend, string? publishingToolsPath, Action<LogEntry> log)
    {
        log(new LogEntry(LogLevel.Success, Loc.T("Plan.Start")));
        log(new LogEntry(LogLevel.Info, source.IsExFat
            ? Loc.F("Plan.SourceExFat", source.Path, source.AppRootInImage)
            : Loc.F("Plan.Source", source.Path)));
        log(new LogEntry(LogLevel.Info, Loc.F("Plan.Output", request.OutputFolder)));
        log(new LogEntry(LogLevel.Info, Loc.F("Plan.Temp", request.TemporaryFolder)));
        log(new LogEntry(LogLevel.Info, Loc.F("Plan.ContentId", request.ContentId)));
        log(new LogEntry(LogLevel.Info, Loc.F("Plan.Version", request.Version)));
        log(new LogEntry(LogLevel.Info, Loc.F("Plan.Kind", request.Kind, request.ImageMode)));

        var workers = request.Threads == 0 ? Math.Max(1, Environment.ProcessorCount) : request.Threads;
        switch (backend)
        {
            case KrakenBackendKind.Uncompressed:
                log(new LogEntry(LogLevel.Info, Loc.T("Plan.KrakenNone")));
                break;
            case KrakenBackendKind.PublishingTools:
                log(new LogEntry(LogLevel.Info, Loc.F("Plan.KrakenNative", request.KrakenLevel, BuildPresets.KrakenLevelName(request.KrakenLevel), workers)));
                log(new LogEntry(LogLevel.Info, Loc.F("Plan.Dll", publishingToolsPath ?? Loc.T("Plan.DllAuto"))));
                break;
            default:
                log(new LogEntry(LogLevel.Info, Loc.F("Plan.KrakenBuiltIn", request.KrakenLevel, BuildPresets.KrakenLevelName(request.KrakenLevel), workers)));
                break;
        }

        log(new LogEntry(LogLevel.Info, Loc.F("Plan.PlayGo", request.PlayGoChunks)));

        if (request.SdkMajorOverride is { } major && SdkVersions.Get(major) is { } generation)
        {
            log(new LogEntry(LogLevel.Info, Loc.F("Plan.SdkOverride", generation.Release, generation.ExecutableVersion.ToString("X16"))));
        }
        else
        {
            log(new LogEntry(LogLevel.Info, Loc.T("Plan.SdkKeep")));
        }

        log(new LogEntry(LogLevel.Info, Loc.T(request.Deterministic ? "Plan.DeterministicOn" : "Plan.DeterministicOff")));
        log(new LogEntry(LogLevel.Info, Loc.T(request.ComputeSha256 ? "Plan.ShaOn" : "Plan.ShaOff")));
    }

    private static string StagingName(string imagePath)
    {
        var name = Path.GetFileNameWithoutExtension(imagePath);
        var safe = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(32).ToArray());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(imagePath))))[..8];
        return (safe.Length > 0 ? safe + "-" : string.Empty) + hash;
    }

    /// <summary>Xoá thư mục (có thử lại) — dùng cho thư mục giải nén tạm.</summary>
    public static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception)
            {
                Thread.Sleep(300);
            }
        }
    }
}
