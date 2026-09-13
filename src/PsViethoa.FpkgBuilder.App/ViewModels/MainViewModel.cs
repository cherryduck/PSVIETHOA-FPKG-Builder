using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PsViethoa.FpkgBuilder.App.Services;
using PsViethoa.FpkgBuilder.Core.ExFat;
using PsViethoa.FpkgBuilder.Core.Localization;
using PsViethoa.FpkgBuilder.Core.Models;
using PsViethoa.FpkgBuilder.Core.Services;

namespace PsViethoa.FpkgBuilder.App.ViewModels;

public enum StatusKind
{
    Ready,
    Working,
    Success,
    Error,
    Canceled,
}

/// <summary>Toàn bộ trạng thái và hành vi của cửa sổ chính (song ngữ, nguồn thư mục hoặc ảnh exFAT).</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxRecentSources = 8;

    /// <summary>Tên trường giả để view đưa tiêu điểm về nút Hủy khi bắt đầu tạo gói.</summary>
    public const string FocusCancelButton = "CancelButton";

    private readonly DialogService _dialogs;
    private readonly AppSettings _settings;
    private readonly BuildEngine _engine = new();
    private readonly ConcurrentQueue<LogEntry> _pendingLogs = new();
    private readonly DispatcherTimer _drainTimer;
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _sourceDebounce;
    private readonly Stopwatch _buildStopwatch = new();

    private CancellationTokenSource? _buildCancellation;
    private CancellationTokenSource? _metadataCancellation;
    private BuildProgress? _pendingProgress;
    private BuildProgress? _lastProgress;
    private DateTime _lastProgressAt;
    private string? _suggestedTemporary;
    private bool _attemptedBuild;
    private bool _syncingPreset;
    private bool _syncingLanguage;
    private string? _lastMetadataSource;
    private SourceMetadata? _lastMetadata;
    private FolderStats? _lastStats;
    private IReadOnlyList<JunkFile> _junkFiles = Array.Empty<JunkFile>();
    private long _sourceBytes;
    private string _statusKey = "Status.Ready";
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _phaseKey = "Phase.NotStarted";
    private BuildOutcome? _outcome;

    public MainViewModel(AppSettings settings, DialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;

        KeysAvailable = BuildEngine.KeysAvailable;
        LibraryVersion = BuildEngine.LibraryVersion;
        IsWindows = OperatingSystem.IsWindows();
        ProcessorCount = Environment.ProcessorCount;

        _drainTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background, (_, _) => Drain());
        _tickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Tick());
        _sourceDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, OnSourceDebounceTick);

        BuildOptionLists();
        LoadSettings();
        Loc.Current.LanguageChanged += (_, _) => OnLanguageChanged();
        _drainTimer.Start();
        RefreshValidation();
        UpdateHints();
        RefreshStatusText();
        PhaseText = Loc.T(_phaseKey);
    }

    private void OnSourceDebounceTick(object? sender, EventArgs e)
    {
        _sourceDebounce.Stop();
        ReloadMetadata();
    }

    // ===================== Thông tin tĩnh =====================

    public bool KeysAvailable { get; }

    public string LibraryVersion { get; }

    public bool IsWindows { get; }

    public int ProcessorCount { get; }

    public string AppVersionText => Loc.F("App.VersionLabel", AppInfo.Version, AppInfo.PlatformLabel);

    public string CreditsText => Loc.F("App.Credits", LibraryVersion);

    public string ThreadsHint => Loc.F("Advanced.ThreadsHint", ProcessorCount);

    public string ThemeTooltip => Loc.T(IsDarkTheme ? "Header.ThemeToLight" : "Header.ThemeToDark");

    [ObservableProperty] private IReadOnlyList<string> _kindOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _imageModeOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _backendOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _sdkOptions = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _exFatOptions = Array.Empty<string>();

    private void BuildOptionLists()
    {
        KindOptions = [Loc.T("Kind.App"), Loc.T("Kind.Homebrew"), Loc.T("Kind.Dlc")];
        ImageModeOptions = [Loc.T("ImageMode.Plain"), Loc.T("ImageMode.Native")];
        BackendOptions = [Loc.T("Backend.Auto"), Loc.T("Backend.BuiltIn"), Loc.T("Backend.PubTools"), Loc.T("Backend.None")];
        SdkOptions = SdkVersions.All.Select(g => g.Label).ToList();
        ExFatOptions = [Loc.T("ExFat.Auto"), Loc.T("ExFat.Mount"), Loc.T("ExFat.Extract")];
    }

    public IReadOnlyList<string> RecentSources =>
        _settings.RecentSources.Where(p => Directory.Exists(p) || File.Exists(p)).ToArray();

    public bool HasRecent => RecentSources.Count > 0;

    public LogCollection LogEntries { get; } = new();

    /// <summary>View lắng nghe để tự cuộn nhật ký.</summary>
    public event EventHandler? LogAppended;

    /// <summary>View lắng nghe để đưa con trỏ về trường lỗi.</summary>
    public event EventHandler<string>? FocusFieldRequested;

    // ===================== Ngôn ngữ =====================

    [ObservableProperty] private bool _languageVi = true;
    [ObservableProperty] private bool _languageEn;

    partial void OnLanguageViChanged(bool value)
    {
        if (value)
        {
            SetLanguage(Loc.Vietnamese);
        }
    }

    partial void OnLanguageEnChanged(bool value)
    {
        if (value)
        {
            SetLanguage(Loc.English);
        }
    }

    private void SetLanguage(string code)
    {
        if (_syncingLanguage)
        {
            return;
        }

        _settings.Language = code;
        Loc.Current.SetLanguage(code);
        SettingsService.Save(_settings);
    }

    private void OnLanguageChanged()
    {
        _syncingLanguage = true;
        try
        {
            LanguageVi = Loc.Current.Language == Loc.Vietnamese;
            LanguageEn = Loc.Current.Language == Loc.English;
        }
        finally
        {
            _syncingLanguage = false;
        }

        var kind = KindIndex;
        var imageMode = ImageModeIndex;
        var backend = BackendIndex;
        var sdk = SdkIndex;
        var exFat = ExFatIndex;
        BuildOptionLists();
        Dispatcher.UIThread.Post(() =>
        {
            KindIndex = kind;
            ImageModeIndex = imageMode;
            BackendIndex = backend;
            SdkIndex = sdk;
            ExFatIndex = exFat;
        }, DispatcherPriority.Background);

        OnPropertyChanged(nameof(AppVersionText));
        OnPropertyChanged(nameof(CreditsText));
        OnPropertyChanged(nameof(ThreadsHint));
        OnPropertyChanged(nameof(ThemeTooltip));

        UpdateHints();
        SyncPresetFromSettings();
        RefreshStatusText();
        RefreshValidation();
        if (!IsBuilding)
        {
            PhaseText = Loc.T(_phaseKey);
        }

        if (_lastMetadata != null)
        {
            ApplyMetadata(SourcePath.Trim(), _lastMetadata);
            if (_lastStats != null)
            {
                MetaFiles = Loc.F("Meta.Files", Formatters.Count(_lastStats.FileCount), Formatters.Size(_lastStats.TotalBytes));
            }
        }
        else
        {
            ShowEmptyMetadata();
        }

        RefreshJunkSummary();
        RefreshDiskInfo();
        if (_outcome != null)
        {
            ShowResult(_outcome);
        }
    }

    // ===================== Đường dẫn =====================

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _outputFolder = string.Empty;
    [ObservableProperty] private string _temporaryFolder = string.Empty;

    // ===================== Thông tin gói =====================

    [ObservableProperty] private string _contentId = string.Empty;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _version = VersionHelper.Default;
    [ObservableProperty] private int _kindIndex;
    [ObservableProperty] private int _imageModeIndex;
    [ObservableProperty] private string _imageModeHint = string.Empty;

    // ===================== Nâng cao =====================

    [ObservableProperty] private string _passcode = new('0', BuildRequest.PasscodeLength);
    [ObservableProperty] private bool _showPasscode;
    [ObservableProperty] private bool _overrideSdk;
    [ObservableProperty] private int _sdkIndex;
    [ObservableProperty] private int _backendIndex;
    [ObservableProperty] private string _backendHint = string.Empty;
    [ObservableProperty] private double _krakenLevel = BuildRequest.DefaultKrakenLevel;
    [ObservableProperty] private string _krakenLevelText = string.Empty;
    [ObservableProperty] private decimal? _threads = 0;
    [ObservableProperty] private decimal? _playGoChunks = BuildRequest.MaxPlayGoChunks;
    [ObservableProperty] private bool _deterministic = true;
    [ObservableProperty] private bool _computeSha256;
    [ObservableProperty] private bool _preventSleep = true;
    [ObservableProperty] private string _publishingToolsPath = string.Empty;
    [ObservableProperty] private bool _advancedExpanded;
    [ObservableProperty] private int _exFatIndex;

    // ===================== Preset =====================

    [ObservableProperty] private bool _presetFast;
    [ObservableProperty] private bool _presetBalanced;
    [ObservableProperty] private bool _presetSmallest;
    [ObservableProperty] private bool _presetCustom;
    [ObservableProperty] private string _presetSummary = string.Empty;

    // ===================== Metadata nguồn =====================

    [ObservableProperty] private bool _hasSource;
    [ObservableProperty] private bool _hasParamJson;
    [ObservableProperty] private bool _metadataWarning;
    [ObservableProperty] private bool _isExFatSource;
    [ObservableProperty] private string _metaExFatChip = string.Empty;
    [ObservableProperty] private string _metaExFatRoot = string.Empty;
    [ObservableProperty] private string _metaTitle = string.Empty;
    [ObservableProperty] private string _metaSubtitle = string.Empty;
    [ObservableProperty] private string _metaVersion = string.Empty;
    [ObservableProperty] private string _metaSdk = string.Empty;
    [ObservableProperty] private string _metaFiles = string.Empty;
    [ObservableProperty] private string _metaTitleId = string.Empty;
    [ObservableProperty] private string _playGoText = string.Empty;
    [ObservableProperty] private bool _hasEboot;
    [ObservableProperty] private Bitmap? _iconImage;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _diskSummary = string.Empty;
    [ObservableProperty] private bool _diskWarning;
    [ObservableProperty] private bool _hasDiskInfo;
    [ObservableProperty] private int _junkCount;
    [ObservableProperty] private bool _junkReadOnly;
    [ObservableProperty] private string _junkSummary = string.Empty;

    // ===================== Trạng thái tạo gói =====================

    [ObservableProperty] private bool _isBuilding;
    [ObservableProperty] private bool _isCanceling;
    [ObservableProperty] private double _overallPercent;
    [ObservableProperty] private double _phasePercent;
    [ObservableProperty] private string _phaseText = string.Empty;
    [ObservableProperty] private string _percentText = "0%";
    [ObservableProperty] private string _etaText = string.Empty;
    [ObservableProperty] private string _elapsedText = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private StatusKind _statusKind = StatusKind.Ready;
    [ObservableProperty] private string? _errorBanner;
    [ObservableProperty] private string? _noticeBanner;

    // ===================== Kết quả =====================

    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultPath = string.Empty;
    [ObservableProperty] private string _resultType = string.Empty;
    [ObservableProperty] private string _resultSize = string.Empty;
    [ObservableProperty] private string _resultContentId = string.Empty;
    [ObservableProperty] private string _resultSha = string.Empty;
    [ObservableProperty] private string _resultElapsed = string.Empty;
    [ObservableProperty] private string _resultRatio = string.Empty;

    // ===================== Nhật ký & giao diện =====================

    [ObservableProperty] private bool _autoScrollLog = true;
    [ObservableProperty] private bool _isDarkTheme = true;
    [ObservableProperty] private int _logCount;

    // ===================== Lỗi nhập liệu =====================

    [ObservableProperty] private string? _sourceError;
    [ObservableProperty] private string? _outputError;
    [ObservableProperty] private string? _temporaryError;
    [ObservableProperty] private string? _contentIdError;
    [ObservableProperty] private string? _passcodeError;
    [ObservableProperty] private string? _versionError;
    [ObservableProperty] private string? _threadsError;
    [ObservableProperty] private string? _playGoError;
    [ObservableProperty] private string? _sdkError;
    [ObservableProperty] private string? _publishingToolsError;
    [ObservableProperty] private string? _exFatError;
    [ObservableProperty] private bool _hasErrors;

    public bool IsStatusReady => StatusKind == StatusKind.Ready;
    public bool IsStatusWorking => StatusKind == StatusKind.Working;
    public bool IsStatusSuccess => StatusKind == StatusKind.Success;
    public bool IsStatusError => StatusKind == StatusKind.Error;
    public bool IsStatusCanceled => StatusKind == StatusKind.Canceled;
    public bool CanEdit => !IsBuilding;
    public bool HasErrorBanner => !string.IsNullOrEmpty(ErrorBanner);
    public bool HasNoticeBanner => !string.IsNullOrEmpty(NoticeBanner);
    public bool HasJunk => JunkCount > 0;
    public bool HasIcon => IconImage != null;
    public bool UsesPublishingTools => BackendIndex is 0 or 2;
    public bool CompressionEnabled => BackendIndex != 3;
    public bool CanCleanJunk => HasJunk && !IsBuilding && !JunkReadOnly;
    public bool CanOpenOutput => HasResult && !string.IsNullOrEmpty(ResultPath) && File.Exists(ResultPath);

    partial void OnStatusKindChanged(StatusKind value)
    {
        OnPropertyChanged(nameof(IsStatusReady));
        OnPropertyChanged(nameof(IsStatusWorking));
        OnPropertyChanged(nameof(IsStatusSuccess));
        OnPropertyChanged(nameof(IsStatusError));
        OnPropertyChanged(nameof(IsStatusCanceled));
    }

    partial void OnIsBuildingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanCleanJunk));
        BuildCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        CleanJunkCommand.NotifyCanExecuteChanged();
    }

    partial void OnErrorBannerChanged(string? value) => OnPropertyChanged(nameof(HasErrorBanner));
    partial void OnNoticeBannerChanged(string? value) => OnPropertyChanged(nameof(HasNoticeBanner));

    partial void OnIconImageChanged(Bitmap? oldValue, Bitmap? newValue)
    {
        oldValue?.Dispose();
        OnPropertyChanged(nameof(HasIcon));
    }

    partial void OnJunkCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasJunk));
        OnPropertyChanged(nameof(CanCleanJunk));
        CleanJunkCommand.NotifyCanExecuteChanged();
    }

    partial void OnJunkReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanCleanJunk));
        CleanJunkCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasResultChanged(bool value)
    {
        OnPropertyChanged(nameof(CanOpenOutput));
        OpenOutputCommand.NotifyCanExecuteChanged();
        CopyResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        App.ApplyTheme(value);
        OnPropertyChanged(nameof(ThemeTooltip));
    }

    partial void OnSourcePathChanged(string value)
    {
        _sourceDebounce.Stop();
        _sourceDebounce.Start();
        RefreshValidation();
    }

    partial void OnOutputFolderChanged(string value)
    {
        UpdateTemporaryForOutput(value);
        RefreshValidation();
        RefreshDiskInfo();
    }

    partial void OnTemporaryFolderChanged(string value)
    {
        RefreshValidation();
        RefreshDiskInfo();
    }

    partial void OnContentIdChanged(string value) => RefreshValidation();
    partial void OnVersionChanged(string value) => RefreshValidation();
    partial void OnPasscodeChanged(string value) => RefreshValidation();
    partial void OnThreadsChanged(decimal? value) => RefreshValidation();
    partial void OnPublishingToolsPathChanged(string value) => RefreshValidation();
    partial void OnOverrideSdkChanged(bool value) => RefreshValidation();

    partial void OnExFatIndexChanged(int value)
    {
        RefreshValidation();
        RefreshDiskInfo();
    }

    partial void OnPlayGoChunksChanged(decimal? value)
    {
        RefreshValidation();
        if (_lastMetadata != null)
        {
            PlayGoText = MetadataReader.DescribePlayGo(_lastMetadata, (int)(value ?? BuildRequest.MaxPlayGoChunks));
        }
    }

    partial void OnImageModeIndexChanged(int value) => UpdateHints();

    partial void OnBackendIndexChanged(int value)
    {
        OnPropertyChanged(nameof(UsesPublishingTools));
        OnPropertyChanged(nameof(CompressionEnabled));
        UpdateHints();
        SyncPresetFromSettings();
        RefreshValidation();
    }

    partial void OnKrakenLevelChanged(double value)
    {
        UpdateHints();
        SyncPresetFromSettings();
    }

    partial void OnPresetFastChanged(bool value)
    {
        if (value)
        {
            ApplyPreset(BuildPresets.Fast);
        }
    }

    partial void OnPresetBalancedChanged(bool value)
    {
        if (value)
        {
            ApplyPreset(BuildPresets.Balanced);
        }
    }

    partial void OnPresetSmallestChanged(bool value)
    {
        if (value)
        {
            ApplyPreset(BuildPresets.Smallest);
        }
    }

    private void ApplyPreset(BuildPreset preset)
    {
        if (_syncingPreset)
        {
            return;
        }

        _syncingPreset = true;
        try
        {
            KrakenLevel = preset.KrakenLevel;
            if (BackendIndex == 3)
            {
                BackendIndex = 0;
            }

            PresetCustom = false;
            PresetSummary = preset.Detail;
        }
        finally
        {
            _syncingPreset = false;
        }

        UpdateHints();
    }

    private void SyncPresetFromSettings()
    {
        if (_syncingPreset)
        {
            return;
        }

        _syncingPreset = true;
        try
        {
            var level = (int)Math.Round(KrakenLevel);
            var compressing = BackendIndex != 3;
            var preset = compressing ? BuildPresets.Match(KrakenBackendKind.Auto, level) : null;
            PresetFast = preset?.Id == BuildPresets.Fast.Id;
            PresetBalanced = preset?.Id == BuildPresets.Balanced.Id;
            PresetSmallest = preset?.Id == BuildPresets.Smallest.Id;
            PresetCustom = preset == null;
            PresetSummary = preset?.Detail ?? (compressing
                ? Loc.F("Preset.CustomLevel", level, BuildPresets.KrakenLevelName(level), BuildPresets.KrakenLevelHint(level))
                : Loc.T("Preset.CustomUncompressed"));
        }
        finally
        {
            _syncingPreset = false;
        }
    }

    private void UpdateHints()
    {
        ImageModeHint = Loc.T(ImageModeIndex == 0 ? "ImageMode.PlainHint" : "ImageMode.NativeHint");

        var level = (int)Math.Round(KrakenLevel);
        KrakenLevelText = Loc.F("Advanced.LevelText", level, BuildPresets.KrakenLevelName(level), BuildPresets.KrakenLevelHint(level));

        BackendHint = BackendIndex switch
        {
            0 => IsWindows ? Loc.T("Backend.AutoHintWindows") : Loc.F("Backend.AutoHintOther", AppInfo.PlatformLabel),
            1 => Loc.T("Backend.BuiltInHint"),
            2 => Loc.T(IsWindows ? "Backend.PubToolsHintWindows" : "Backend.PubToolsHintOther"),
            _ => Loc.T("Backend.NoneHint"),
        };
    }

    // ===================== Cấu hình =====================

    private void LoadSettings()
    {
        var s = _settings;
        _syncingPreset = true;
        _syncingLanguage = true;
        try
        {
            LanguageVi = Loc.Normalize(s.Language) == Loc.Vietnamese;
            LanguageEn = !LanguageVi;

            SourcePath = s.SourcePath;
            OutputFolder = s.OutputFolder;
            var defaultTemporary = string.IsNullOrWhiteSpace(s.OutputFolder) ? string.Empty : BuildPreparer.SuggestTemporaryFolder(s.OutputFolder);
            var keepSaved = !string.IsNullOrWhiteSpace(s.TemporaryFolder) &&
                            (string.IsNullOrWhiteSpace(s.OutputFolder) || DiskSpaceAdvisor.IsSameVolume(s.TemporaryFolder, s.OutputFolder));
            TemporaryFolder = keepSaved ? s.TemporaryFolder : defaultTemporary;
            _suggestedTemporary = keepSaved ? null : defaultTemporary;

            ContentId = s.ContentId;
            Title = s.Title;
            Version = string.IsNullOrWhiteSpace(s.Version) ? VersionHelper.Default : s.Version;
            KindIndex = s.Kind switch { PackageKind.Homebrew => 1, PackageKind.DlcWithData => 2, _ => 0 };
            ImageModeIndex = s.ImageMode == OuterImageMode.Native ? 1 : 0;
            BackendIndex = s.KrakenBackend switch
            {
                KrakenBackendKind.BuiltIn => 1,
                KrakenBackendKind.PublishingTools => 2,
                KrakenBackendKind.Uncompressed => 3,
                _ => 0,
            };
            ExFatIndex = s.ExFat switch { ExFatStrategy.Mount => 1, ExFatStrategy.Extract => 2, _ => 0 };
            KrakenLevel = Math.Clamp(s.KrakenLevel, BuildRequest.MinKrakenLevel, BuildRequest.MaxKrakenLevel);
            Threads = Math.Clamp(s.Threads, 0, BuildRequest.MaxThreads);
            PlayGoChunks = Math.Clamp(s.PlayGoChunks, BuildRequest.MinPlayGoChunks, BuildRequest.MaxPlayGoChunks);
            Deterministic = s.Deterministic;
            ComputeSha256 = s.ComputeSha256;
            PreventSleep = s.PreventSleep;
            OverrideSdk = s.OverrideSdk;
            SdkIndex = Math.Clamp(s.SdkMajor - 1, 0, SdkOptions.Count - 1);
            PublishingToolsPath = IsWindows ? (PublishingToolsLocator.Find(s.PublishingToolsPath) ?? s.PublishingToolsPath) : s.PublishingToolsPath;
            AdvancedExpanded = s.AdvancedExpanded;
            AutoScrollLog = s.AutoScrollLog;
            IsDarkTheme = !string.Equals(s.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _syncingPreset = false;
            _syncingLanguage = false;
        }

        SyncPresetFromSettings();
    }

    public void SaveSettings()
    {
        var s = _settings;
        s.SourcePath = SourcePath.Trim();
        s.OutputFolder = OutputFolder.Trim();
        s.TemporaryFolder = TemporaryFolder.Trim();
        s.ContentId = ContentId.Trim();
        s.Title = Title.Trim();
        s.Version = Version.Trim();
        s.Kind = KindFromIndex(KindIndex);
        s.ImageMode = ImageModeIndex == 1 ? OuterImageMode.Native : OuterImageMode.PlaintextNoAuth;
        s.KrakenBackend = BackendFromIndex(BackendIndex);
        s.ExFat = ExFatFromIndex(ExFatIndex);
        s.KrakenLevel = (int)Math.Round(KrakenLevel);
        s.Threads = (int)(Threads ?? 0);
        s.PlayGoChunks = (int)(PlayGoChunks ?? BuildRequest.MaxPlayGoChunks);
        s.Deterministic = Deterministic;
        s.ComputeSha256 = ComputeSha256;
        s.PreventSleep = PreventSleep;
        s.OverrideSdk = OverrideSdk;
        s.SdkMajor = SdkIndex + 1;
        s.PublishingToolsPath = PublishingToolsPath.Trim();
        s.AdvancedExpanded = AdvancedExpanded;
        s.AutoScrollLog = AutoScrollLog;
        s.Theme = IsDarkTheme ? "Dark" : "Light";
        s.Language = Loc.Current.Language;
        SettingsService.Save(s);
    }

    public void RememberWindowSize(double width, double height)
    {
        if (width > 400 && height > 300)
        {
            _settings.WindowWidth = width;
            _settings.WindowHeight = height;
        }
    }

    public (double Width, double Height) SavedWindowSize => (_settings.WindowWidth, _settings.WindowHeight);

    private static PackageKind KindFromIndex(int index) => index switch
    {
        1 => PackageKind.Homebrew,
        2 => PackageKind.DlcWithData,
        _ => PackageKind.Application,
    };

    private static KrakenBackendKind BackendFromIndex(int index) => index switch
    {
        1 => KrakenBackendKind.BuiltIn,
        2 => KrakenBackendKind.PublishingTools,
        3 => KrakenBackendKind.Uncompressed,
        _ => KrakenBackendKind.Auto,
    };

    private static ExFatStrategy ExFatFromIndex(int index) => index switch
    {
        1 => ExFatStrategy.Mount,
        2 => ExFatStrategy.Extract,
        _ => ExFatStrategy.Auto,
    };

    // ===================== Chọn nguồn =====================

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var initial = Directory.Exists(SourcePath.Trim()) ? SourcePath.Trim() : Path.GetDirectoryName(SourcePath.Trim());
        var folder = await _dialogs.PickFolderAsync(Loc.T("Pick.Source"), initial);
        if (folder != null)
        {
            SetSource(folder);
        }
    }

    [RelayCommand]
    private async Task BrowseExFatAsync()
    {
        var current = SourcePath.Trim();
        var initial = File.Exists(current) ? Path.GetDirectoryName(current) : Directory.Exists(current) ? current : null;
        var file = await _dialogs.PickFileAsync(
            Loc.T("Pick.ExFat"),
            initial,
            new FilePickerFileType(Loc.T("Pick.ExFatFilter")) { Patterns = ["*.exfat"] },
            new FilePickerFileType(Loc.T("Pick.AllFiles")) { Patterns = ["*"] });
        if (file != null)
        {
            SetSource(file);
        }
    }

    public void SetSource(string path)
    {
        SourcePath = path;
        if (string.IsNullOrWhiteSpace(OutputFolder))
        {
            try
            {
                OutputFolder = BuildPreparer.SuggestOutputFolder(path);
            }
            catch (Exception)
            {
            }
        }

        _sourceDebounce.Stop();
        ReloadMetadata();
    }

    [RelayCommand]
    private void SelectRecent(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && (Directory.Exists(path) || File.Exists(path)))
        {
            SetSource(path);
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var folder = await _dialogs.PickFolderAsync(Loc.T("Pick.Output"), OutputFolder.Trim());
        if (folder != null)
        {
            OutputFolder = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseTemporaryAsync()
    {
        var folder = await _dialogs.PickFolderAsync(Loc.T("Pick.Temp"), TemporaryFolder.Trim());
        if (folder != null)
        {
            TemporaryFolder = folder;
            _suggestedTemporary = null;
        }
    }

    [RelayCommand]
    private async Task BrowsePublishingToolsAsync()
    {
        var initial = File.Exists(PublishingToolsPath) ? Path.GetDirectoryName(PublishingToolsPath) : null;
        var file = await _dialogs.PickFileAsync(
            Loc.T("Pick.Dll"),
            initial,
            new FilePickerFileType("libScePubTools.dll") { Patterns = ["libScePubTools.dll", "*.dll"] });
        if (file != null)
        {
            PublishingToolsPath = file;
        }
    }

    [RelayCommand]
    private void UseSuggestedContentId() => ContentId = ContentIdHelper.Suggest(MetaTitleId, Title);

    [RelayCommand]
    private void ResetTemporary()
    {
        if (!string.IsNullOrWhiteSpace(OutputFolder))
        {
            var suggestion = BuildPreparer.SuggestTemporaryFolder(OutputFolder);
            TemporaryFolder = suggestion;
            _suggestedTemporary = suggestion;
        }
    }

    private void UpdateTemporaryForOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var current = TemporaryFolder.Trim();
        var untouched = string.IsNullOrWhiteSpace(current) ||
                        string.Equals(current, _suggestedTemporary, StringComparison.OrdinalIgnoreCase);
        if (!untouched)
        {
            return;
        }

        try
        {
            var suggestion = BuildPreparer.SuggestTemporaryFolder(output);
            _suggestedTemporary = suggestion;
            TemporaryFolder = suggestion;
        }
        catch (Exception)
        {
        }
    }

    // ===================== Metadata =====================

    [RelayCommand]
    private void ReloadMetadata()
    {
        _metadataCancellation?.Cancel();
        _metadataCancellation?.Dispose();
        _metadataCancellation = null;

        var source = SourcePath.Trim();
        if (SourceLocator.Detect(source) == SourceKind.None)
        {
            _lastMetadata = null;
            _lastStats = null;
            ShowEmptyMetadata();
            return;
        }

        var cancellation = new CancellationTokenSource();
        _metadataCancellation = cancellation;
        _ = LoadMetadataAsync(source, cancellation.Token);
    }

    private async Task LoadMetadataAsync(string source, CancellationToken cancellationToken)
    {
        IsScanning = true;
        try
        {
            var metadata = await Task.Run(() => MetadataReader.Read(source, cancellationToken), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _lastMetadata = metadata;
            _lastStats = null;
            ApplyMetadata(source, metadata);

            if (metadata.HasParamJson && !string.Equals(_lastMetadataSource, source, StringComparison.OrdinalIgnoreCase))
            {
                _lastMetadataSource = source;
                Log(LogLevel.Info, Loc.F("Meta.ReadParam", metadata.ParamJsonPath));
            }

            var iconTask = Task.Run(() => LoadBitmap(metadata), cancellationToken);
            var statsTask = Task.Run(() => FolderScanner.Scan(source, cancellationToken), cancellationToken);
            var junkTask = Task.Run(() => JunkFileFinder.Find(source, cancellationToken), cancellationToken);

            IconImage = await iconTask;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var stats = await statsTask;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _lastStats = stats;
            _sourceBytes = stats.TotalBytes;
            MetaFiles = Loc.F("Meta.Files", Formatters.Count(stats.FileCount), Formatters.Size(stats.TotalBytes));
            RememberRecent(source);

            _junkFiles = await junkTask;
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            JunkReadOnly = _junkFiles.Any(j => j.IsReadOnly);
            JunkCount = _junkFiles.Count;
            RefreshJunkSummary();
            RefreshDiskInfo();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _lastMetadata = null;
            MetaTitle = Loc.T("Meta.ReadFailed");
            MetaSubtitle = ex.Message;
            MetadataWarning = true;
            HasSource = true;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsScanning = false;
            }
        }
    }

    private static Bitmap? LoadBitmap(SourceMetadata metadata)
    {
        try
        {
            if (metadata.IconBytes is { Length: > 0 } bytes)
            {
                using var memory = new MemoryStream(bytes);
                return Bitmap.DecodeToWidth(memory, 256);
            }

            if (metadata.IconPath != null && File.Exists(metadata.IconPath))
            {
                using var stream = File.OpenRead(metadata.IconPath);
                return Bitmap.DecodeToWidth(stream, 256);
            }
        }
        catch (Exception)
        {
        }

        return null;
    }

    private void ShowEmptyMetadata()
    {
        HasSource = false;
        HasParamJson = false;
        MetadataWarning = false;
        IsExFatSource = false;
        MetaExFatChip = string.Empty;
        MetaExFatRoot = string.Empty;
        MetaTitle = Loc.T("Meta.NoSource");
        MetaSubtitle = Loc.T("Meta.NoSourceHint");
        MetaVersion = string.Empty;
        MetaSdk = string.Empty;
        MetaFiles = string.Empty;
        MetaTitleId = string.Empty;
        PlayGoText = string.Empty;
        HasEboot = false;
        IconImage = null;
        IsScanning = false;
        HasDiskInfo = false;
        DiskWarning = false;
        DiskSummary = string.Empty;
        JunkCount = 0;
        JunkReadOnly = false;
        JunkSummary = string.Empty;
        _junkFiles = Array.Empty<JunkFile>();
        _sourceBytes = 0;
    }

    private void ApplyMetadata(string source, SourceMetadata metadata)
    {
        HasSource = true;
        HasEboot = metadata.HasEboot;
        IsExFatSource = metadata.IsExFat;
        MetaExFatChip = metadata.IsExFat ? Loc.F("Meta.ExFatChip", string.IsNullOrWhiteSpace(metadata.VolumeLabel) ? "—" : metadata.VolumeLabel) : string.Empty;
        MetaExFatRoot = metadata.IsExFat
            ? (string.IsNullOrEmpty(metadata.AppRootInImage) ? Loc.T("Meta.ExFatRootTop") : Loc.F("Meta.ExFatRoot", metadata.AppRootInImage))
            : string.Empty;
        MetaTitleId = metadata.TitleId ?? string.Empty;
        if (_lastStats == null)
        {
            MetaFiles = Loc.T("Meta.Scanning");
        }

        PlayGoText = MetadataReader.DescribePlayGo(metadata, (int)(PlayGoChunks ?? BuildRequest.MaxPlayGoChunks));

        if (!metadata.HasSceSys)
        {
            HasParamJson = false;
            MetadataWarning = true;
            MetaTitle = Loc.T(metadata.IsExFat ? "Val.ExFatNoApp" : "Meta.NoSceSys");
            MetaSubtitle = Loc.T("Meta.NoSceSysHint");
            MetaVersion = string.Empty;
            MetaSdk = string.Empty;
            return;
        }

        if (!metadata.HasParamJson)
        {
            HasParamJson = false;
            MetadataWarning = true;
            MetaTitle = Loc.T("Meta.NoParam");
            MetaSubtitle = metadata.ParamJsonError ?? Loc.T("Meta.NoParamHint");
            MetaVersion = string.Empty;
            MetaSdk = string.Empty;
            if (string.IsNullOrWhiteSpace(ContentId))
            {
                ContentId = ContentIdHelper.Suggest(null, Title.Length > 0 ? Title : Path.GetFileNameWithoutExtension(source));
            }

            return;
        }

        HasParamJson = true;
        MetadataWarning = false;
        MetaTitle = string.IsNullOrWhiteSpace(metadata.Title) ? Loc.T("Meta.NoTitle") : metadata.Title;
        MetaSubtitle = string.IsNullOrWhiteSpace(metadata.ContentId) ? Loc.T("Meta.NoContentId") : metadata.ContentId;
        MetaVersion = Loc.F("Meta.Version", string.IsNullOrWhiteSpace(metadata.Version) ? "—" : metadata.Version);
        MetaSdk = Loc.F("Meta.Sdk", metadata.SdkMajor?.ToString() ?? "—");

        if (!string.IsNullOrWhiteSpace(metadata.ContentId))
        {
            ContentId = metadata.ContentId;
        }

        if (VersionHelper.TryCanonicalize(metadata.Version, out var canonical))
        {
            Version = canonical;
        }

        if (!string.IsNullOrWhiteSpace(metadata.Title))
        {
            Title = metadata.Title;
        }

        if (!OverrideSdk && metadata.SdkMajor is { } sdk and >= SdkVersions.MinMajor and <= SdkVersions.MaxMajor)
        {
            SdkIndex = sdk - 1;
        }
    }

    private void RefreshJunkSummary()
    {
        JunkSummary = JunkCount == 0
            ? string.Empty
            : Loc.F(JunkReadOnly ? "Junk.SummaryReadOnly" : "Junk.Summary", JunkCount);
    }

    private bool WillExtractExFat =>
        IsExFatSource && (ExFatIndex == 2 || !ExFatMounter.IsAvailable || (ExFatIndex == 0 && JunkCount > 0));

    private void RefreshDiskInfo()
    {
        if (!HasSource || _sourceBytes <= 0 || string.IsNullOrWhiteSpace(OutputFolder))
        {
            HasDiskInfo = false;
            DiskWarning = false;
            DiskSummary = string.Empty;
            return;
        }

        var output = OutputFolder.Trim();
        var temporary = string.IsNullOrWhiteSpace(TemporaryFolder) ? BuildPreparer.SuggestTemporaryFolder(output) : TemporaryFolder.Trim();
        var bytes = _sourceBytes;
        var staging = WillExtractExFat ? _sourceBytes : 0;
        _ = Task.Run(() => DiskSpaceAdvisor.Check(output, temporary, bytes, staging)).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                var report = task.Result;
                HasDiskInfo = true;
                DiskWarning = !report.Sufficient;
                DiskSummary = report.Summary;
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void RememberRecent(string source)
    {
        var list = _settings.RecentSources;
        list.RemoveAll(item => string.Equals(item, source, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, source);
        while (list.Count > MaxRecentSources)
        {
            list.RemoveAt(list.Count - 1);
        }

        OnPropertyChanged(nameof(RecentSources));
        OnPropertyChanged(nameof(HasRecent));
    }

    [RelayCommand(CanExecute = nameof(CanCleanJunk))]
    private async Task CleanJunkAsync()
    {
        if (_junkFiles.Count == 0 || JunkReadOnly)
        {
            return;
        }

        var root = SourcePath.Trim();
        var preview = string.Join("\n", _junkFiles.Take(8).Select(j => "• " + Path.GetRelativePath(root, j.Path)));
        if (_junkFiles.Count > 8)
        {
            preview += "\n" + Loc.F("Junk.More", _junkFiles.Count - 8);
        }

        var confirmed = await _dialogs.ConfirmAsync(
            Loc.T("Junk.ConfirmTitle"),
            Loc.F("Junk.ConfirmBody", _junkFiles.Count, preview),
            Loc.T("Junk.Delete"),
            Loc.T("Common.Cancel"),
            destructive: true);
        if (!confirmed)
        {
            return;
        }

        var files = _junkFiles;
        var (deleted, errors) = await Task.Run(() => JunkFileFinder.Delete(files));
        Log(deleted > 0 ? LogLevel.Success : LogLevel.Warning, Loc.F("Junk.Result", deleted, files.Count));
        foreach (var error in errors)
        {
            Log(LogLevel.Warning, Loc.F("Junk.DeleteFailed", error));
        }

        ReloadMetadata();
    }

    // ===================== Kiểm tra dữ liệu nhập =====================

    private BuildRequest CreateRequest() => new()
    {
        SourcePath = SourcePath.Trim(),
        OutputFolder = OutputFolder.Trim(),
        TemporaryFolder = TemporaryFolder.Trim(),
        ContentId = ContentId.Trim(),
        Title = Title.Trim(),
        Version = Version.Trim(),
        Passcode = Passcode,
        Kind = KindFromIndex(KindIndex),
        ImageMode = ImageModeIndex == 1 ? OuterImageMode.Native : OuterImageMode.PlaintextNoAuth,
        KrakenBackend = BackendFromIndex(BackendIndex),
        ExFat = ExFatFromIndex(ExFatIndex),
        KrakenLevel = (int)Math.Round(KrakenLevel),
        Threads = (int)(Threads ?? 0),
        PlayGoChunks = (int)(PlayGoChunks ?? BuildRequest.MaxPlayGoChunks),
        Deterministic = Deterministic,
        ComputeSha256 = ComputeSha256,
        SdkMajorOverride = OverrideSdk ? SdkIndex + 1 : null,
        PublishingToolsPath = string.IsNullOrWhiteSpace(PublishingToolsPath) ? null : PublishingToolsPath.Trim(),
        PreventSleep = PreventSleep,
    };

    private IReadOnlyList<ValidationError> RefreshValidation()
    {
        IReadOnlyList<ValidationError> errors;
        try
        {
            errors = BuildPreparer.Validate(CreateRequest());
        }
        catch (Exception ex)
        {
            errors = [new ValidationError(BuildPreparer.FieldSource, ex.Message)];
        }

        string? Pick(string field, bool hideWhenEmpty, string value)
        {
            var error = errors.FirstOrDefault(e => e.Field == field)?.Message;
            if (error != null && hideWhenEmpty && !_attemptedBuild && string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return error;
        }

        SourceError = Pick(BuildPreparer.FieldSource, true, SourcePath);
        OutputError = Pick(BuildPreparer.FieldOutput, true, OutputFolder);
        TemporaryError = Pick(BuildPreparer.FieldTemporary, true, TemporaryFolder);
        ContentIdError = Pick(BuildPreparer.FieldContentId, true, ContentId);
        PasscodeError = Pick(BuildPreparer.FieldPasscode, false, Passcode);
        VersionError = Pick(BuildPreparer.FieldVersion, false, Version);
        ThreadsError = Pick(BuildPreparer.FieldThreads, false, "x");
        PlayGoError = Pick(BuildPreparer.FieldPlayGo, false, "x");
        SdkError = Pick(BuildPreparer.FieldSdk, false, "x");
        PublishingToolsError = Pick(BuildPreparer.FieldPublishingTools, false, "x");
        ExFatError = Pick(BuildPreparer.FieldExFat, false, "x");
        HasErrors = errors.Count > 0;
        return errors;
    }

    // ===================== Tạo gói =====================

    private bool CanBuild => !IsBuilding;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (IsBuilding)
        {
            return;
        }

        _attemptedBuild = true;
        var errors = RefreshValidation();
        if (errors.Count > 0)
        {
            var first = errors[0];
            SetStatus(StatusKind.Error, "Status.Invalid");
            ErrorBanner = first.Message;
            Log(LogLevel.Warning, first.Message);
            FocusFieldRequested?.Invoke(this, first.Field);
            return;
        }

        if (!KeysAvailable)
        {
            ErrorBanner = Loc.T("Build.NoKeysBanner");
            SetStatus(StatusKind.Error, "Status.NoKeys");
            return;
        }

        if (DiskWarning)
        {
            var proceed = await _dialogs.ConfirmAsync(
                Loc.T("Build.DiskTitle"),
                Loc.F("Build.DiskBody", DiskSummary),
                Loc.T("Build.DiskYes"),
                Loc.T("Common.Cancel"));
            if (!proceed)
            {
                return;
            }
        }

        if (JunkCount > 0 && !JunkReadOnly)
        {
            DebugLog.Write($"Build: asking about {JunkCount} junk files");
            var clean = await _dialogs.ConfirmAsync(
                Loc.T("Junk.BuildTitle"),
                Loc.F("Junk.BuildBody", JunkSummary, JunkCount),
                Loc.T("Junk.BuildYes"),
                Loc.T("Junk.BuildNo"));
            if (clean)
            {
                var files = _junkFiles;
                var (deleted, deleteErrors) = await Task.Run(() => JunkFileFinder.Delete(files));
                Log(LogLevel.Info, Loc.F("Junk.Result", deleted, files.Count));
                foreach (var error in deleteErrors)
                {
                    Log(LogLevel.Warning, Loc.F("Junk.DeleteFailed", error));
                }

                JunkCount = 0;
                JunkSummary = string.Empty;
                _junkFiles = Array.Empty<JunkFile>();
            }
        }

        DebugLog.Write("Build: starting engine");
        var request = CreateRequest();

        ErrorBanner = null;
        NoticeBanner = null;
        ClearResult();
        LogEntries.Clear();
        LogCount = 0;
        _pendingLogs.Clear();
        Interlocked.Exchange(ref _pendingProgress, null);
        _lastProgress = null;

        IsBuilding = true;
        IsCanceling = false;
        FocusFieldRequested?.Invoke(this, FocusCancelButton);
        _buildCancellation = new CancellationTokenSource();
        var token = _buildCancellation.Token;

        SetStatus(StatusKind.Working, "Status.Building");
        _phaseKey = "Phase.Preparing";
        PhaseText = Loc.T(_phaseKey) + "…";
        PercentText = "0%";
        OverallPercent = 0;
        PhasePercent = 0;
        EtaText = string.Empty;
        _buildStopwatch.Restart();
        ElapsedText = "00:00";
        _tickTimer.Start();
        SaveSettings();

        try
        {
            var outcome = await _engine.BuildAsync(
                request,
                entry => _pendingLogs.Enqueue(entry),
                new Progress<BuildProgress>(snapshot => Interlocked.Exchange(ref _pendingProgress, snapshot)),
                token);

            Drain();
            foreach (var warning in outcome.Warnings)
            {
                Log(LogLevel.Warning, Loc.F("Build.Warning", warning));
            }

            ShowResult(outcome);
            Log(LogLevel.Info, Loc.F("Build.Container", outcome.Verification.ContainerLabel));
            Log(LogLevel.Info, Loc.F("Build.Size", Formatters.SizeWithBytes(outcome.Verification.Length)));
            Log(LogLevel.Info, Loc.F("Build.Fih", outcome.Verification.SignedByte.ToString("X2"), outcome.Verification.OuterMode.ToString("X4")));
            if (outcome.Verification.SeedMarker != null)
            {
                Log(LogLevel.Info, Loc.F("Build.Marker", outcome.Verification.SeedMarker));
            }

            if (outcome.Verification.Sha256 != null)
            {
                Log(LogLevel.Info, Loc.F("Build.Sha", outcome.Verification.Sha256));
            }

            Log(LogLevel.Success, Loc.F("Build.Success", Formatters.Duration(outcome.Elapsed), outcome.OutputPath));
            SetStatus(StatusKind.Success, "Status.Success");
            _phaseKey = "Phase.Done";
            PhaseText = Loc.T(_phaseKey);
            PercentText = "100%";
            OverallPercent = 100;
            PhasePercent = 100;
            EtaText = string.Empty;
            if (outcome.Warnings.Count > 0)
            {
                NoticeBanner = Loc.F("Build.WarningsNotice", outcome.Warnings.Count);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Drain();
            Log(LogLevel.Warning, Loc.T("Build.UserCanceled"));
            SetStatus(StatusKind.Canceled, "Status.Canceled");
            _phaseKey = "Phase.Canceled";
            PhaseText = Loc.T(_phaseKey);
            EtaText = string.Empty;
        }
        catch (BuildValidationException ex)
        {
            Drain();
            ErrorBanner = ex.Message;
            SetStatus(StatusKind.Error, "Status.Invalid");
            _phaseKey = "Phase.Stopped";
            PhaseText = Loc.T(_phaseKey);
            RefreshValidation();
        }
        catch (Exception ex)
        {
            Drain();
            Log(LogLevel.Error, Loc.F("Build.Error", ex.Message));
            if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            {
                Log(LogLevel.Error, ex.StackTrace!);
            }

            SetStatus(StatusKind.Error, "Status.Failed");
            ErrorBanner = ex.Message;
            _phaseKey = "Phase.Stopped";
            PhaseText = Loc.T(_phaseKey);
            EtaText = string.Empty;
        }
        finally
        {
            _buildStopwatch.Stop();
            _tickTimer.Stop();
            _buildCancellation?.Dispose();
            _buildCancellation = null;
            IsBuilding = false;
            IsCanceling = false;
            ElapsedText = Formatters.Clock(_buildStopwatch.Elapsed);
            Drain();
        }
    }

    private bool CanCancel => IsBuilding && !IsCanceling;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (_buildCancellation == null || _buildCancellation.IsCancellationRequested)
        {
            return;
        }

        IsCanceling = true;
        CancelCommand.NotifyCanExecuteChanged();
        SetStatus(StatusKind.Working, "Status.Canceling");
        Log(LogLevel.Warning, Loc.T("Build.CancelRequested"));
        _buildCancellation.Cancel();
    }

    /// <summary>Gọi khi cửa sổ đóng; trả về true nếu được phép đóng.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!IsBuilding)
        {
            _metadataCancellation?.Cancel();
            SaveSettings();
            return true;
        }

        var close = await _dialogs.ConfirmAsync(
            Loc.T("Close.Title"),
            Loc.T("Close.Body"),
            Loc.T("Close.Yes"),
            Loc.T("Close.No"),
            destructive: true);
        if (!close)
        {
            return false;
        }

        _buildCancellation?.Cancel();
        _metadataCancellation?.Cancel();
        SaveSettings();
        return true;
    }

    private void SetStatus(StatusKind kind, string key, params object?[] args)
    {
        StatusKind = kind;
        _statusKey = key;
        _statusArgs = args;
        RefreshStatusText();
    }

    private void RefreshStatusText() => StatusText = Loc.F(_statusKey, _statusArgs);

    private void ShowResult(BuildOutcome outcome)
    {
        _outcome = outcome;
        var v = outcome.Verification;
        ResultPath = outcome.OutputPath;
        ResultType = v.ContainerLabel;
        ResultSize = Formatters.SizeWithBytes(v.Length);
        ResultContentId = string.IsNullOrWhiteSpace(v.ContentId) ? "—" : v.ContentId;
        ResultSha = v.Sha256 ?? Loc.T("Result.NoSha");
        ResultElapsed = Formatters.Duration(outcome.Elapsed);
        ResultRatio = _sourceBytes > 0
            ? Loc.F("Result.Ratio", Formatters.Size(_sourceBytes), Formatters.Size(v.Length), (v.Length * 100.0 / _sourceBytes).ToString("0.#"))
            : string.Empty;
        HasResult = true;
    }

    private void ClearResult()
    {
        _outcome = null;
        HasResult = false;
        ResultPath = string.Empty;
        ResultType = string.Empty;
        ResultSize = string.Empty;
        ResultContentId = string.Empty;
        ResultSha = string.Empty;
        ResultElapsed = string.Empty;
        ResultRatio = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanOpenOutput))]
    private async Task OpenOutputAsync()
    {
        var folder = Path.GetDirectoryName(ResultPath);
        if (folder != null && !await _dialogs.OpenFolderAsync(folder))
        {
            await _dialogs.ShowErrorAsync(Loc.T("Build.OpenFailed"), folder);
        }
    }

    [RelayCommand(CanExecute = nameof(HasResult))]
    private async Task CopyResultAsync()
    {
        if (_outcome == null)
        {
            return;
        }

        var v = _outcome.Verification;
        var text = new StringBuilder()
            .AppendLine(Loc.F("Result.ClipboardHeader", AppInfo.Name))
            .AppendLine(Loc.T("Result.File") + ": " + _outcome.OutputPath)
            .AppendLine(Loc.T("Result.Type") + ": " + v.ContainerLabel)
            .AppendLine(Loc.T("Result.Size") + ": " + Formatters.SizeWithBytes(v.Length))
            .AppendLine(Loc.F("Build.Fih", v.SignedByte.ToString("X2"), v.OuterMode.ToString("X4")))
            .AppendLine("Content ID: " + (v.ContentId ?? "—"))
            .AppendLine(Loc.F("Result.Entries", v.EntryCount))
            .AppendLine("SHA-256: " + (v.Sha256 ?? "—"))
            .AppendLine(Loc.F("Result.Elapsed", Formatters.Duration(_outcome.Elapsed)))
            .ToString();

        try
        {
            await _dialogs.SetClipboardAsync(text);
            SetStatus(StatusKind.Ready, "Status.CopiedResult");
        }
        catch (Exception ex)
        {
            ErrorBanner = Loc.F("Build.CopyFailed", ex.Message);
        }
    }

    // ===================== Nhật ký =====================

    private void Log(LogLevel level, string message) => _pendingLogs.Enqueue(new LogEntry(level, message));

    private void Drain()
    {
        var progress = Interlocked.Exchange(ref _pendingProgress, null);
        if (progress != null)
        {
            ApplyProgress(progress);
        }

        if (_pendingLogs.IsEmpty)
        {
            return;
        }

        var batch = new List<LogEntry>();
        while (batch.Count < 2000 && _pendingLogs.TryDequeue(out var entry))
        {
            batch.Add(entry);
        }

        LogEntries.AddBatch(batch);
        LogCount = LogEntries.Count;
        LogAppended?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyProgress(BuildProgress progress)
    {
        _lastProgress = progress;
        _lastProgressAt = DateTime.UtcNow;
        OverallPercent = Math.Clamp(progress.OverallPercent, 0, 100);
        PhasePercent = Math.Clamp(progress.PhasePercent, 0, 100);
        PercentText = $"{OverallPercent:0}%";
        PhaseText = progress.IsComplete
            ? Loc.T("Phase.Done")
            : $"{progress.Phase} · {progress.PhasePercent:0}%";
        UpdateEta();
    }

    private void Tick()
    {
        if (!IsBuilding)
        {
            return;
        }

        ElapsedText = Formatters.Clock(_buildStopwatch.Elapsed);
        UpdateEta();
    }

    private void UpdateEta()
    {
        if (_lastProgress?.Eta is not { } eta || _lastProgress.IsComplete)
        {
            EtaText = IsBuilding && _lastProgress != null && _lastProgress.OverallPercent > 0 ? Loc.T("Eta.Estimating") : string.Empty;
            return;
        }

        var remaining = eta - (DateTime.UtcNow - _lastProgressAt);
        if (remaining < TimeSpan.FromSeconds(1))
        {
            remaining = TimeSpan.FromSeconds(1);
        }

        EtaText = Loc.F("Eta.Remaining", Formatters.Duration(remaining));
    }

    private string BuildLogText()
    {
        var builder = new StringBuilder(LogEntries.Count * 80);
        foreach (var entry in LogEntries)
        {
            builder.Append('[').Append(entry.TimeText).Append("] ").AppendLine(entry.Message);
        }

        return builder.ToString();
    }

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (LogEntries.Count == 0)
        {
            return;
        }

        try
        {
            await _dialogs.SetClipboardAsync(BuildLogText());
            SetStatus(IsBuilding ? StatusKind.Working : StatusKind.Ready, "Status.CopiedLog");
        }
        catch (Exception ex)
        {
            ErrorBanner = Loc.F("Log.CopyFailed", ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        if (LogEntries.Count == 0)
        {
            return;
        }

        var path = await _dialogs.SaveFileAsync(
            Loc.T("Log.SaveTitle"),
            $"fpkg-build-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            "txt",
            new FilePickerFileType(Loc.T("Log.TextFiles")) { Patterns = ["*.txt"] });
        if (path == null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, BuildLogText(), Encoding.UTF8);
            SetStatus(IsBuilding ? StatusKind.Working : StatusKind.Ready, "Status.SavedLog", path);
        }
        catch (Exception ex)
        {
            ErrorBanner = Loc.F("Log.SaveFailed", ex.Message);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
        LogCount = 0;
    }

    [RelayCommand]
    private void DismissError() => ErrorBanner = null;

    [RelayCommand]
    private void DismissNotice() => NoticeBanner = null;

    [RelayCommand]
    private void ToggleTheme() => IsDarkTheme = !IsDarkTheme;

    [RelayCommand]
    private Task OpenAuthorAsync() => _dialogs.OpenUriAsync(AppInfo.AuthorUrl);
}
