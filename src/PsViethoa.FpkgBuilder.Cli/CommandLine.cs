using System.Diagnostics;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;
using PsViethoa.FpkgBuilder.Core.Services;

namespace PsViethoa.FpkgBuilder.Cli;

/// <summary>Bộ phân tích tham số dòng lệnh tối giản, không phụ thuộc thư viện ngoài.</summary>
internal sealed class Arguments
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positionals { get; } = new();

    public static Arguments Parse(IEnumerable<string> args)
    {
        var result = new Arguments();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            var token = list[i];
            if (token.StartsWith("--", StringComparison.Ordinal) && token.Length > 2)
            {
                var body = token[2..];
                var eq = body.IndexOf('=');
                if (eq > 0)
                {
                    result._options[body[..eq]] = body[(eq + 1)..];
                }
                else if (i + 1 < list.Count && !list[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    result._options[body] = list[++i];
                }
                else
                {
                    result._options[body] = null;
                }
            }
            else if (token.StartsWith('-') && token.Length == 2)
            {
                var key = token[1..];
                if (i + 1 < list.Count && !list[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    result._options[key] = list[++i];
                }
                else
                {
                    result._options[key] = null;
                }
            }
            else
            {
                result.Positionals.Add(token);
            }
        }

        return result;
    }

    public bool Has(params string[] names) => names.Any(_options.ContainsKey);

    public string? Get(params string[] names)
    {
        foreach (var name in names)
        {
            if (_options.TryGetValue(name, out var value))
            {
                return value;
            }
        }

        return null;
    }

    public int? GetInt(params string[] names) =>
        int.TryParse(Get(names), out var value) ? value : null;
}

internal static class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        var arguments = Arguments.Parse(args);
        Loc.Current.SetLanguage(arguments.Get("lang") ?? Environment.GetEnvironmentVariable("FPKG_LANG") ?? Loc.DetectSystemLanguage());

        var command = arguments.Positionals.FirstOrDefault()?.ToLowerInvariant();
        if (command == null || arguments.Has("help", "h") || command is "help")
        {
            Console.WriteLine(Loc.T("Cli.Help"));
            Console.WriteLine();
            Console.WriteLine(Loc.T("Cli.Credits"));
            return command == null && !arguments.Has("help", "h") ? 1 : 0;
        }

        try
        {
            return command switch
            {
                "build" => await BuildAsync(arguments),
                "inspect" => Inspect(arguments),
                "verify" => Verify(arguments),
                "clean-junk" => CleanJunk(arguments),
                "info" => Info(),
                _ => Unknown(command),
            };
        }
        catch (BuildValidationException ex)
        {
            Console.Error.WriteLine(Loc.T("Cli.InvalidArgs"));
            foreach (var error in ex.Errors)
            {
                Console.Error.WriteLine($"  - [{error.Field}] {error.Message}");
            }

            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(Loc.T("Cli.Canceled"));
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(Loc.F("Cli.Error", ex.Message));
            if (Environment.GetEnvironmentVariable("FPKG_DEBUG") == "1")
            {
                Console.Error.WriteLine(ex);
            }

            return 2;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine(Loc.F("Cli.Unknown", command));
        return 1;
    }

    private static int Info()
    {
        Console.WriteLine("PSVIETHOA FPKG Builder CLI");
        Console.WriteLine(Loc.T("Cli.Credits"));
        Console.WriteLine(Loc.F("Cli.Os", Environment.OSVersion, System.Runtime.InteropServices.RuntimeInformation.OSArchitecture));
        Console.WriteLine(Loc.F("Cli.Runtime", Environment.Version, Environment.ProcessorCount));
        Console.WriteLine(Loc.F("Cli.Library", BuildEngine.LibraryVersion));
        Console.WriteLine(Loc.T(BuildEngine.KeysAvailable ? "Cli.KeysReady" : "Cli.KeysMissing"));
        var backend = BuildPreparer.ResolveBackend(new BuildRequest(), out var dll);
        Console.WriteLine(Loc.F("Cli.DefaultBackend", Describe(backend) + (dll != null ? " — " + dll : string.Empty)));
        return 0;
    }

    private static string Describe(KrakenBackendKind backend) => Loc.T(backend switch
    {
        KrakenBackendKind.PublishingTools => "Cli.BackendNative",
        KrakenBackendKind.Uncompressed => "Cli.BackendNone",
        _ => "Cli.BackendBuiltIn",
    });

    private static string YesNo(bool value, bool loud = false) => Loc.T(value ? "Cli.Yes" : loud ? "Cli.NO" : "Cli.No");

    private static int Inspect(Arguments arguments)
    {
        var source = arguments.Positionals.Skip(1).FirstOrDefault() ?? arguments.Get("source", "s");
        if (string.IsNullOrWhiteSpace(source) || SourceLocator.Detect(source) == SourceKind.None)
        {
            Console.Error.WriteLine(Loc.T("Cli.NeedSource"));
            return 1;
        }

        var metadata = MetadataReader.Read(source, CancellationToken.None);
        if (metadata.IsExFat)
        {
            Console.WriteLine(Loc.F("Cli.SourceExFat", Path.GetFullPath(source), metadata.VolumeLabel ?? "—", metadata.AppRootInImage));
        }
        else
        {
            Console.WriteLine(Loc.F("Cli.Folder", Path.GetFullPath(source)));
        }

        Console.WriteLine(Loc.F("Cli.Layout", YesNo(metadata.HasSceSys, loud: true), YesNo(metadata.HasEboot), YesNo(metadata.IconPath != null || metadata.IconBytes != null)));
        Console.WriteLine(metadata.HasParamJson
            ? Loc.F("Cli.Param", metadata.ContentId ?? "—", metadata.Version ?? "—", metadata.SdkMajor?.ToString() ?? "—", metadata.Title ?? "—")
            : Loc.T("Cli.NoParam") + (metadata.ParamJsonError != null ? " (" + metadata.ParamJsonError + ")" : string.Empty));
        Console.WriteLine(MetadataReader.DescribePlayGo(metadata, BuildRequest.MaxPlayGoChunks));

        var stats = FolderScanner.Scan(source, CancellationToken.None);
        Console.WriteLine(Loc.F("Cli.Stats", Formatters.Count(stats.FileCount), Formatters.Count(stats.DirectoryCount), Formatters.SizeWithBytes(stats.TotalBytes), Formatters.Size(stats.LargestFileBytes), stats.ScanDuration.TotalMilliseconds.ToString("0")));

        var junk = JunkFileFinder.Find(source, CancellationToken.None);
        Console.WriteLine(junk.Count == 0 ? Loc.T("Cli.NoJunk") : Loc.F("Cli.Junk", junk.Count));
        foreach (var item in junk.Take(10))
        {
            Console.WriteLine("  - " + item.Path);
        }

        var output = arguments.Get("output", "o") ?? BuildPreparer.SuggestOutputFolder(source);
        var temp = arguments.Get("temp") ?? BuildPreparer.SuggestTemporaryFolder(output);
        var staging = metadata.IsExFat && !PsViethoa.FpkgBuilder.Core.ExFat.ExFatMounter.IsAvailable ? stats.TotalBytes : 0;
        var disk = DiskSpaceAdvisor.Check(output, temp, stats.TotalBytes, staging);
        Console.WriteLine(Loc.F("Cli.Disk", disk.Summary) + (disk.Sufficient ? string.Empty : Loc.T("Cli.DiskLow")));
        return 0;
    }

    private static int Verify(Arguments arguments)
    {
        var path = arguments.Positionals.Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Console.Error.WriteLine(Loc.T("Cli.NeedPkg"));
            return 1;
        }

        var mode = string.Equals(arguments.Get("image"), "native", StringComparison.OrdinalIgnoreCase)
            ? OuterImageMode.Native
            : OuterImageMode.PlaintextNoAuth;
        var verification = PackageVerifier.Verify(path, mode, arguments.Has("sha256"), CancellationToken.None);
        PrintVerification(path, verification);
        return 0;
    }

    private static int CleanJunk(Arguments arguments)
    {
        var source = arguments.Positionals.Skip(1).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            Console.Error.WriteLine(Loc.T("Cli.NeedFolder"));
            return 1;
        }

        var junk = JunkFileFinder.Find(source, CancellationToken.None);
        foreach (var item in junk)
        {
            Console.WriteLine(Loc.T(item.IsDirectory ? "Cli.JunkDir" : "Cli.JunkFile") + item.Path);
        }

        if (arguments.Has("dry-run"))
        {
            Console.WriteLine(Loc.F("Cli.JunkDryRun", junk.Count));
            return 0;
        }

        var (deleted, errors) = JunkFileFinder.Delete(junk);
        Console.WriteLine(Loc.F("Cli.JunkDeleted", deleted, junk.Count));
        foreach (var error in errors)
        {
            Console.Error.WriteLine("  ! " + error);
        }

        return errors.Count == 0 ? 0 : 2;
    }

    private static async Task<int> BuildAsync(Arguments arguments)
    {
        var quiet = arguments.Has("quiet", "q");
        var source = arguments.Get("source", "s") ?? arguments.Positionals.Skip(1).FirstOrDefault() ?? string.Empty;
        var kind = SourceLocator.Detect(source);
        var output = arguments.Get("output", "o") ?? (kind != SourceKind.None ? BuildPreparer.SuggestOutputFolder(source) : string.Empty);

        if (arguments.Has("clean-junk") && kind == SourceKind.Folder)
        {
            var junk = JunkFileFinder.Find(source, CancellationToken.None);
            var (deleted, errors) = JunkFileFinder.Delete(junk);
            if (!quiet)
            {
                Console.WriteLine(Loc.F("Cli.CleanResult", deleted, junk.Count));
            }

            foreach (var error in errors)
            {
                Console.Error.WriteLine("  ! " + error);
            }
        }

        SourceMetadata? metadata = null;
        if (kind != SourceKind.None)
        {
            try
            {
                metadata = MetadataReader.Read(source, CancellationToken.None);
            }
            catch (Exception)
            {
                metadata = null;
            }
        }

        var request = new BuildRequest
        {
            SourcePath = source,
            OutputFolder = output,
            TemporaryFolder = arguments.Get("temp") ?? string.Empty,
            ContentId = arguments.Get("content-id", "c") ?? metadata?.ContentId ?? string.Empty,
            Title = arguments.Get("title", "t") ?? metadata?.Title ?? string.Empty,
            Version = arguments.Get("version", "v") ?? VersionHelper.CanonicalOrDefault(metadata?.Version),
            Passcode = arguments.Get("passcode") ?? new string('0', BuildRequest.PasscodeLength),
            Kind = (arguments.Get("kind") ?? "app").ToLowerInvariant() switch
            {
                "homebrew" => PackageKind.Homebrew,
                "dlc" or "ac" => PackageKind.DlcWithData,
                _ => PackageKind.Application,
            },
            ImageMode = string.Equals(arguments.Get("image"), "native", StringComparison.OrdinalIgnoreCase)
                ? OuterImageMode.Native
                : OuterImageMode.PlaintextNoAuth,
            ExFat = (arguments.Get("exfat") ?? "auto").ToLowerInvariant() switch
            {
                "mount" => ExFatStrategy.Mount,
                "extract" => ExFatStrategy.Extract,
                _ => ExFatStrategy.Auto,
            },
            Threads = arguments.GetInt("threads", "j") ?? 0,
            PlayGoChunks = arguments.GetInt("playgo") ?? BuildRequest.MaxPlayGoChunks,
            SdkMajorOverride = arguments.GetInt("sdk"),
            Deterministic = !arguments.Has("no-deterministic"),
            ComputeSha256 = arguments.Has("sha256"),
            PublishingToolsPath = arguments.Get("dll"),
            PreventSleep = !arguments.Has("no-sleep-guard"),
        };

        BuildPresets.Apply(BuildPresets.ById(arguments.Get("preset")) ?? BuildPresets.Default, request);

        if (arguments.Get("backend") is { } backendText)
        {
            request.KrakenBackend = backendText.ToLowerInvariant() switch
            {
                "builtin" or "built-in" or "managed" => KrakenBackendKind.BuiltIn,
                "pubtools" or "oodle" or "native" => KrakenBackendKind.PublishingTools,
                "none" or "uncompressed" or "raw" => KrakenBackendKind.Uncompressed,
                _ => KrakenBackendKind.Auto,
            };
        }

        if (arguments.GetInt("level") is { } level)
        {
            request.KrakenLevel = level;
        }

        if (string.IsNullOrEmpty(request.ContentId) && metadata != null)
        {
            request.ContentId = ContentIdHelper.Suggest(metadata.TitleId, request.Title);
            if (!quiet)
            {
                Console.WriteLine(Loc.F("Cli.SuggestedId", request.ContentId));
            }
        }

        var validationErrors = BuildPreparer.Validate(request);
        if (validationErrors.Count > 0)
        {
            throw new BuildValidationException(validationErrors);
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            if (!cancellation.IsCancellationRequested)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(Loc.T("Cli.CancelRequested"));
                cancellation.Cancel();
            }
        };

        var renderer = new ConsoleProgressRenderer(quiet);
        var engine = new BuildEngine();
        var outcome = await engine.BuildAsync(
            request,
            renderer.Log,
            new Progress<BuildProgress>(renderer.Update),
            cancellation.Token);

        renderer.Finish();

        foreach (var warning in outcome.Warnings)
        {
            Console.WriteLine(Loc.F("Cli.Warning", warning));
        }

        Console.WriteLine();
        Console.WriteLine(Loc.F("Cli.Success", Formatters.Duration(outcome.Elapsed)));
        PrintVerification(outcome.OutputPath, outcome.Verification);
        return 0;
    }

    private static void PrintVerification(string path, PackageVerification verification)
    {
        Console.WriteLine(Loc.F("Cli.File", path));
        Console.WriteLine(Loc.F("Cli.Type", verification.ContainerLabel));
        Console.WriteLine(Loc.F("Cli.Size", Formatters.SizeWithBytes(verification.Length)));
        Console.WriteLine(Loc.F("Cli.Fih", verification.SignedByte.ToString("X2"), verification.OuterMode.ToString("X4")));
        if (verification.SeedMarker != null)
        {
            Console.WriteLine(Loc.F("Cli.Marker", verification.SeedMarker));
        }

        Console.WriteLine(Loc.F("Cli.ContentId", verification.ContentId ?? "—", verification.EntryCount));
        if (verification.Sha256 != null)
        {
            Console.WriteLine(Loc.F("Cli.Sha", verification.Sha256));
        }
    }
}

/// <summary>Vẽ thanh tiến trình một dòng trong terminal, in nhật ký phía trên.</summary>
internal sealed class ConsoleProgressRenderer
{
    private readonly bool _quiet;
    private readonly bool _interactive;
    private readonly object _gate = new();
    private readonly Stopwatch _lastDraw = Stopwatch.StartNew();
    private string _lastLine = string.Empty;
    private BuildProgress? _latest;

    public ConsoleProgressRenderer(bool quiet)
    {
        _quiet = quiet;
        _interactive = !Console.IsOutputRedirected;
    }

    public void Log(LogEntry entry)
    {
        if (_quiet && entry.Level == LogLevel.Info)
        {
            return;
        }

        lock (_gate)
        {
            ClearLine();
            var prefix = entry.Level switch
            {
                LogLevel.Warning => "⚠ ",
                LogLevel.Error => "✖ ",
                LogLevel.Success => "✔ ",
                _ => "  ",
            };
            Console.WriteLine($"{entry.TimeText} {prefix}{entry.Message}");
            if (_interactive)
            {
                Redraw();
            }
        }
    }

    public void Update(BuildProgress progress)
    {
        lock (_gate)
        {
            _latest = progress;
            if (_lastDraw.ElapsedMilliseconds < 120 && !progress.IsComplete)
            {
                return;
            }

            Redraw();
        }
    }

    public void Finish()
    {
        lock (_gate)
        {
            ClearLine();
        }
    }

    private void Redraw()
    {
        if (_latest == null)
        {
            return;
        }

        var p = _latest;
        var eta = p.Eta is { } remaining && !p.IsComplete ? Loc.F("Cli.ProgressEta", Formatters.Clock(remaining)) : string.Empty;
        var text = Loc.F("Cli.Progress", Bar(p.OverallPercent), p.OverallPercent, p.Phase, p.PhasePercent, Formatters.Clock(p.Elapsed), eta);

        if (_interactive)
        {
            var width = Math.Max(20, SafeWindowWidth() - 1);
            if (text.Length > width)
            {
                text = text[..width];
            }

            Console.Write('\r' + text.PadRight(_lastLine.Length));
            _lastLine = text;
        }
        else if (p.IsComplete || p.PhasePercent % 10 == 0)
        {
            Console.WriteLine(text);
        }

        _lastDraw.Restart();
    }

    private void ClearLine()
    {
        if (_interactive && _lastLine.Length > 0)
        {
            Console.Write('\r' + new string(' ', _lastLine.Length) + '\r');
            _lastLine = string.Empty;
        }
    }

    private static string Bar(double percent)
    {
        const int width = 24;
        var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * width);
        return new string('█', filled) + new string('░', width - filled);
    }

    private static int SafeWindowWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch (Exception)
        {
            return 100;
        }
    }
}
