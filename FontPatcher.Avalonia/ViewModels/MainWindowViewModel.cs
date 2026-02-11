using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontPatcher.Cli;
using FontPatcher.Avalonia.Models;
using FontPatcher.Avalonia.Services;

namespace FontPatcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 220;
    private static readonly Regex UnityVersionExtractor = new(
        @"\d{4}\.\d+\.\d+[abfp]\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly IBuildRunner _buildRunner;
    private readonly UnityProvisioningFacade _unityProvisioning;
    private readonly IAppSettingsStore _settingsStore;
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _unityInstallCts;
    private bool _isApplyingPersistedSettings;
    private bool _isApplyingInstalledVersionSelection;

    public MainWindowViewModel()
        : this(new NullBuildRunner(), new AppSettingsStore())
    {
    }

    public MainWindowViewModel(IBuildRunner buildRunner)
        : this(buildRunner, new AppSettingsStore())
    {
    }

    public MainWindowViewModel(IBuildRunner buildRunner, IAppSettingsStore settingsStore)
    {
        _buildRunner = buildRunner;
        _unityProvisioning = new UnityProvisioningFacade();
        _settingsStore = settingsStore;

        EpochOptions =
        [
            new OptionItem("auto", "Auto-detect (Recommended)"),
            new OptionItem("legacy", "Legacy (Unity 5.x - 2017)"),
            new OptionItem("modern", "Modern (Unity 2021+)")
        ];

        BuildTargetOptions =
        [
            new OptionItem("StandaloneWindows64", "StandaloneWindows64"),
            new OptionItem("StandaloneOSX", "StandaloneOSX"),
            new OptionItem("StandaloneLinux64", "StandaloneLinux64"),
            new OptionItem("iOS", "iOS"),
            new OptionItem("Android", "Android")
        ];

        selectedEpoch = EpochOptions[0];
        selectedBuildTarget = BuildTargetOptions[0];

        OpenSingleModeCommand = new RelayCommand(OpenSingleMode);
        OpenBatchModeCommand = new RelayCommand(OpenBatchMode);
        OpenUnityCommand = new RelayCommand(OpenUnitySection);
        OpenAdvancedCommand = new RelayCommand(OpenAdvancedSection);
        StartBuildCommand = new AsyncRelayCommand(StartBuildAsync, () => CanRunBuild);
        InstallRequiredUnityCommand = new AsyncRelayCommand(InstallRequiredUnityAsync, () => CanInstallRequiredUnity);
        FlushLogsCommand = new RelayCommand(() => Logs.Clear());
        ToggleLogFullscreenCommand = new RelayCommand(() => IsLogFullscreen = !IsLogFullscreen);

        Logs.CollectionChanged += OnLogsChanged;
        AvailableUnityVersions.CollectionChanged += OnAvailableUnityVersionsChanged;
        LoadUserSettings();
        NotifyBuildStateChanged();
        RefreshUnityRequirementStatus();
    }

    public ObservableCollection<LogLineViewModel> Logs { get; } = [];
    public ObservableCollection<UnityInstalledVersionInfo> AvailableUnityVersions { get; } = [];

    public IReadOnlyList<OptionItem> EpochOptions { get; }

    public IReadOnlyList<OptionItem> BuildTargetOptions { get; }

    public IRelayCommand OpenSingleModeCommand { get; }

    public IRelayCommand OpenBatchModeCommand { get; }

    public IRelayCommand OpenUnityCommand { get; }

    public IRelayCommand OpenAdvancedCommand { get; }

    public IAsyncRelayCommand StartBuildCommand { get; }

    public IAsyncRelayCommand InstallRequiredUnityCommand { get; }

    public IRelayCommand FlushLogsCommand { get; }

    public IRelayCommand ToggleLogFullscreenCommand { get; }

    [ObservableProperty] private AppSection activeSection = AppSection.Single;
    [ObservableProperty] private bool isBatchMode;
    [ObservableProperty] private bool isLogFullscreen;

    [ObservableProperty] private string fontPath = string.Empty;
    [ObservableProperty] private string outputPath = string.Empty;
    [ObservableProperty] private string bundleName = string.Empty;
    [ObservableProperty] private string tmpName = string.Empty;

    [ObservableProperty] private string jobsFile = string.Empty;
    [ObservableProperty] private string maxWorkers = "1";
    [ObservableProperty] private bool continueOnJobError;

    [ObservableProperty] private string unityPath = @"C:/Program Files/Unity/Hub/Editor/2022.3.10f1/Editor/Unity.exe";
    [ObservableProperty] private string unityHubPath = @"C:/Program Files/Unity Hub/Unity Hub.exe";
    [ObservableProperty] private string unityVersion = "2022.3.10f1";
    [ObservableProperty] private string targetGame = string.Empty;
    [ObservableProperty] private string unityInstallRoot = @"C:/UnityInstallations";
    [ObservableProperty] private OptionItem selectedEpoch;
    [ObservableProperty] private bool useNographics = true;
    [ObservableProperty] private bool autoInstallUnity = true;
    [ObservableProperty] private bool autoInstallHub = true;
    [ObservableProperty] private bool preferNonLts;
    [ObservableProperty] private bool hasRequiredUnityVersion;
    [ObservableProperty] private bool isRequiredUnityInstalled;
    [ObservableProperty] private string requiredUnityVersion = string.Empty;
    [ObservableProperty] private string unityRequirementStatusText = "Unity version is not defined yet.";
    [ObservableProperty] private string installedUnityEditorPath = string.Empty;
    [ObservableProperty] private UnityInstalledVersionInfo? selectedInstalledUnityVersion;
    [ObservableProperty] private bool isUnityInstallInProgress;

    [ObservableProperty] private OptionItem selectedBuildTarget;
    [ObservableProperty] private string atlasSizes = "1024,2048,4096";
    [ObservableProperty] private string pointSize = "90";
    [ObservableProperty] private string padding = "8";
    [ObservableProperty] private string scanUpperBound = "1114111";
    [ObservableProperty] private bool forceStatic;
    [ObservableProperty] private bool forceDynamic;
    [ObservableProperty] private string dynamicWarmupLimit = "20000";
    [ObservableProperty] private string dynamicWarmupBatch = "1024";
    [ObservableProperty] private bool includeControl;
    [ObservableProperty] private bool keepTemp;

    [ObservableProperty] private bool isBuilding;
    [ObservableProperty] private int buildProgress;

    public bool IsSingleTabActive => ActiveSection == AppSection.Single;

    public bool IsBatchTabActive => ActiveSection == AppSection.Batch;

    public bool IsUnityTabActive => ActiveSection == AppSection.Unity;

    public bool IsAdvancedTabActive => ActiveSection == AppSection.Advanced;

    public bool IsSingleSectionVisible => ActiveSection == AppSection.Single;

    public bool IsBatchSectionVisible => ActiveSection == AppSection.Batch;

    public bool IsUnitySectionVisible => ActiveSection == AppSection.Unity;

    public bool IsAdvancedSectionVisible => ActiveSection == AppSection.Advanced;

    public bool IsSingleMode => !IsBatchMode;

    public bool IsBuildReady => IsBatchMode
        ? !string.IsNullOrWhiteSpace(JobsFile)
        : !string.IsNullOrWhiteSpace(TargetGame) && !string.IsNullOrWhiteSpace(FontPath) && !string.IsNullOrWhiteSpace(OutputPath);

    public bool CanRunBuild => !IsBuilding && IsBuildReady;

    public bool ShowBuildWarning => !IsBuildReady && !IsBuilding;

    public bool IsStatusProcessing => IsBuilding;

    public bool IsStatusReady => !IsBuilding && IsBuildReady;

    public bool IsStatusPending => !IsBuilding && !IsBuildReady;

    public string StatusText => IsBuilding
        ? "PROCESSING..."
        : IsBuildReady
            ? "READY FOR BUILD"
            : "CONFIGURATION PENDING";

    public string RunBuildButtonText => IsBuilding ? "BUILDING..." : "RUN BUILD";

    public string TargetPlatformText => SelectedBuildTarget.Value;

    public bool HasLogs => Logs.Count > 0;

    public bool IsLogEmpty => Logs.Count == 0;

    public bool HasProgress => IsBuilding;

    public string ProgressLabel => $"{BuildProgress}% COMPLETE";

    public bool IsMainLayoutVisible => !IsLogFullscreen;

    public bool IsLogFullscreenVisible => IsLogFullscreen;

    public string LogViewToggleText => IsLogFullscreen ? "EXIT LOG VIEW" : "OPEN LOG VIEW";

    public bool IsSingleStep3Enabled =>
        !string.IsNullOrWhiteSpace(TargetGame) && !string.IsNullOrWhiteSpace(FontPath);

    public bool IsSingleFinalPolishEnabled => IsBuildReady;

    public double SingleStep3Opacity => IsSingleStep3Enabled ? 1.0 : 0.4;

    public double SingleFinalPolishOpacity => IsSingleFinalPolishEnabled ? 1.0 : 0.4;

    public bool ShowInstallRequiredUnityButton => HasRequiredUnityVersion && !IsRequiredUnityInstalled;

    public bool CanInstallRequiredUnity => ShowInstallRequiredUnityButton && !IsUnityInstallInProgress && !IsBuilding;

    public string InstallRequiredUnityButtonText => IsUnityInstallInProgress
        ? "INSTALLING..."
        : string.IsNullOrWhiteSpace(RequiredUnityVersion)
            ? "INSTALL REQUIRED UNITY"
            : $"INSTALL UNITY {RequiredUnityVersion}";

    public string RequiredUnityVersionDisplayText => string.IsNullOrWhiteSpace(RequiredUnityVersion)
        ? "NOT DETECTED"
        : RequiredUnityVersion;

    public bool HasInstalledUnityEditorPath => !string.IsNullOrWhiteSpace(InstalledUnityEditorPath);
    public bool HasAvailableUnityVersions => AvailableUnityVersions.Count > 0;
    public bool ShowUnityVersionsEmptyHint => !HasAvailableUnityVersions;

    public bool StrictLtsMode
    {
        get => !PreferNonLts;
        set
        {
            if (value == StrictLtsMode)
            {
                return;
            }

            PreferNonLts = !value;
        }
    }

    partial void OnActiveSectionChanged(AppSection value)
    {
        OnPropertyChanged(nameof(IsSingleTabActive));
        OnPropertyChanged(nameof(IsBatchTabActive));
        OnPropertyChanged(nameof(IsUnityTabActive));
        OnPropertyChanged(nameof(IsAdvancedTabActive));
        OnPropertyChanged(nameof(IsSingleSectionVisible));
        OnPropertyChanged(nameof(IsBatchSectionVisible));
        OnPropertyChanged(nameof(IsUnitySectionVisible));
        OnPropertyChanged(nameof(IsAdvancedSectionVisible));
    }

    partial void OnIsBatchModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsSingleMode));
        NotifyBuildStateChanged();
    }

    partial void OnSelectedBuildTargetChanged(OptionItem value)
    {
        OnPropertyChanged(nameof(TargetPlatformText));
    }

    partial void OnPreferNonLtsChanged(bool value)
    {
        OnPropertyChanged(nameof(StrictLtsMode));
    }

    partial void OnTargetGameChanged(string value)
    {
        bool versionChanged = AutoDetectUnityVersionFromTargetGame(value);
        if (!versionChanged)
        {
            RefreshUnityRequirementStatus();
        }

        NotifySingleFlowChanged();
        NotifyBuildStateChanged();
        SaveUserSettings();
    }

    partial void OnUnityVersionChanged(string value)
    {
        RefreshUnityRequirementStatus();
        NotifyUnityAvailabilityChanged();
        SaveUserSettings();
    }

    partial void OnUnityPathChanged(string value)
    {
        RefreshUnityRequirementStatus();
        SaveUserSettings();
    }

    partial void OnUnityInstallRootChanged(string value)
    {
        RefreshUnityRequirementStatus();
        SaveUserSettings();
    }

    partial void OnUnityHubPathChanged(string value)
    {
        SaveUserSettings();
    }

    partial void OnHasRequiredUnityVersionChanged(bool value)
    {
        NotifyUnityAvailabilityChanged();
    }

    partial void OnIsRequiredUnityInstalledChanged(bool value)
    {
        NotifyUnityAvailabilityChanged();
    }

    partial void OnRequiredUnityVersionChanged(string value)
    {
        OnPropertyChanged(nameof(RequiredUnityVersionDisplayText));
        NotifyUnityAvailabilityChanged();
    }

    partial void OnIsUnityInstallInProgressChanged(bool value)
    {
        NotifyUnityAvailabilityChanged();
    }

    partial void OnInstalledUnityEditorPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasInstalledUnityEditorPath));
    }

    partial void OnSelectedInstalledUnityVersionChanged(UnityInstalledVersionInfo? value)
    {
        if (_isApplyingInstalledVersionSelection || value is null)
        {
            return;
        }

        bool updated = false;
        if (!string.Equals(UnityPath, value.Path, StringComparison.OrdinalIgnoreCase))
        {
            UnityPath = value.Path;
            updated = true;
        }

        if (!string.Equals(UnityVersion, value.Version, StringComparison.OrdinalIgnoreCase))
        {
            UnityVersion = value.Version;
            updated = true;
        }

        if (updated)
        {
            AddLog($"Selected installed Unity {value.Version}.", LogLevel.Info);
        }
    }

    partial void OnIsLogFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsMainLayoutVisible));
        OnPropertyChanged(nameof(IsLogFullscreenVisible));
        OnPropertyChanged(nameof(LogViewToggleText));
    }

    partial void OnFontPathChanged(string value)
    {
        NotifySingleFlowChanged();
        NotifyBuildStateChanged();
    }

    partial void OnOutputPathChanged(string value)
    {
        NotifyBuildStateChanged();
    }

    partial void OnJobsFileChanged(string value)
    {
        NotifyBuildStateChanged();
    }

    partial void OnIsBuildingChanged(bool value)
    {
        StartBuildCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(RunBuildButtonText));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(CanRunBuild));
        OnPropertyChanged(nameof(ShowBuildWarning));
        OnPropertyChanged(nameof(IsStatusProcessing));
        OnPropertyChanged(nameof(IsStatusReady));
        OnPropertyChanged(nameof(IsStatusPending));
        OnPropertyChanged(nameof(StatusText));
        NotifyUnityAvailabilityChanged();
    }

    partial void OnBuildProgressChanged(int value)
    {
        OnPropertyChanged(nameof(ProgressLabel));
    }

    private void NotifySingleFlowChanged()
    {
        OnPropertyChanged(nameof(IsSingleStep3Enabled));
        OnPropertyChanged(nameof(SingleStep3Opacity));
    }

    private void NotifyBuildStateChanged()
    {
        OnPropertyChanged(nameof(IsBuildReady));
        OnPropertyChanged(nameof(CanRunBuild));
        OnPropertyChanged(nameof(ShowBuildWarning));
        OnPropertyChanged(nameof(IsStatusReady));
        OnPropertyChanged(nameof(IsStatusPending));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsSingleFinalPolishEnabled));
        OnPropertyChanged(nameof(SingleFinalPolishOpacity));
        StartBuildCommand.NotifyCanExecuteChanged();
    }

    private void NotifyUnityAvailabilityChanged()
    {
        OnPropertyChanged(nameof(ShowInstallRequiredUnityButton));
        OnPropertyChanged(nameof(CanInstallRequiredUnity));
        OnPropertyChanged(nameof(InstallRequiredUnityButtonText));
        InstallRequiredUnityCommand.NotifyCanExecuteChanged();
    }

    private void RefreshUnityRequirementStatus()
    {
        UnityRequirementCheckResult status = _unityProvisioning.CheckRequiredVersion(
            UnityVersion,
            TargetGame,
            UnityInstallRoot,
            UnityPath);

        HasRequiredUnityVersion = status.HasRequiredVersion;
        IsRequiredUnityInstalled = status.IsInstalled;
        RequiredUnityVersion = status.RequiredVersion ?? string.Empty;
        UnityRequirementStatusText = status.Message;
        InstalledUnityEditorPath = status.InstalledPath ?? string.Empty;
        RefreshAvailableUnityVersions();
    }

    private void RefreshAvailableUnityVersions()
    {
        IReadOnlyList<UnityInstalledVersionInfo> discovered = _unityProvisioning.DiscoverInstalledVersions(
            UnityInstallRoot,
            UnityPath);

        string? currentPath = UnityPath;
        string? previouslySelectedPath = SelectedInstalledUnityVersion?.Path;

        AvailableUnityVersions.Clear();
        foreach (UnityInstalledVersionInfo item in discovered)
        {
            AvailableUnityVersions.Add(item);
        }

        UnityInstalledVersionInfo? selected = null;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            selected = AvailableUnityVersions.FirstOrDefault(
                x => string.Equals(x.Path, currentPath, StringComparison.OrdinalIgnoreCase));
        }

        if (selected is null && !string.IsNullOrWhiteSpace(previouslySelectedPath))
        {
            selected = AvailableUnityVersions.FirstOrDefault(
                x => string.Equals(x.Path, previouslySelectedPath, StringComparison.OrdinalIgnoreCase));
        }

        if (selected is null && !string.IsNullOrWhiteSpace(UnityVersion))
        {
            selected = AvailableUnityVersions.FirstOrDefault(
                x => string.Equals(x.Version, UnityVersion.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        _isApplyingInstalledVersionSelection = true;
        // The collection is recreated here, so keep SelectedItem reference in sync with new item instances.
        SelectedInstalledUnityVersion = null;
        if (selected is not null)
        {
            SelectedInstalledUnityVersion = selected;
        }
        _isApplyingInstalledVersionSelection = false;
    }

    private void OnAvailableUnityVersionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasAvailableUnityVersions));
        OnPropertyChanged(nameof(ShowUnityVersionsEmptyHint));
    }

    private void LoadUserSettings()
    {
        AppSettingsSnapshot snapshot = _settingsStore.Load();
        _isApplyingPersistedSettings = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(snapshot.UnityPath))
            {
                UnityPath = snapshot.UnityPath;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.UnityHubPath))
            {
                UnityHubPath = snapshot.UnityHubPath;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.UnityVersion))
            {
                UnityVersion = snapshot.UnityVersion;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.UnityInstallRoot))
            {
                UnityInstallRoot = snapshot.UnityInstallRoot;
            }

            if (!string.IsNullOrWhiteSpace(snapshot.TargetGame))
            {
                TargetGame = snapshot.TargetGame;
            }
        }
        finally
        {
            _isApplyingPersistedSettings = false;
        }
    }

    private void SaveUserSettings()
    {
        if (_isApplyingPersistedSettings)
        {
            return;
        }

        var snapshot = new AppSettingsSnapshot(
            UnityPath.Trim(),
            UnityHubPath.Trim(),
            UnityVersion.Trim(),
            UnityInstallRoot.Trim(),
            TargetGame.Trim());

        _settingsStore.Save(snapshot);
    }

    private bool AutoDetectUnityVersionFromTargetGame(string targetGamePath)
    {
        string? detectedVersion = DetectUnityVersion(targetGamePath);
        if (string.IsNullOrWhiteSpace(detectedVersion))
        {
            return false;
        }

        if (string.Equals(UnityVersion, detectedVersion, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        UnityVersion = detectedVersion;
        AddLog($"Detected Unity version from target game: {detectedVersion}", LogLevel.Info);
        return true;
    }

    private static string? DetectUnityVersion(string targetGamePath)
    {
        if (string.IsNullOrWhiteSpace(targetGamePath))
        {
            return null;
        }

        try
        {
            string fullPath = Path.GetFullPath(targetGamePath);
            string? unityPlayerDll = ResolveUnityPlayerDllPath(fullPath);
            if (unityPlayerDll is null || !File.Exists(unityPlayerDll))
            {
                return null;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(unityPlayerDll);
            string? rawVersion = FirstNonEmpty(info.ProductVersion, info.FileVersion, info.Comments);
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                return null;
            }

            Match match = UnityVersionExtractor.Match(rawVersion);
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveUnityPlayerDllPath(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            if (string.Equals(Path.GetFileName(fullPath), "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            string sibling = Path.Combine(directory, "UnityPlayer.dll");
            return File.Exists(sibling) ? sibling : null;
        }

        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        string direct = Path.Combine(fullPath, "UnityPlayer.dll");
        if (File.Exists(direct))
        {
            return direct;
        }

        if (fullPath.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(fullPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
            {
                string sibling = Path.Combine(parent, "UnityPlayer.dll");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }
        }

        string[] found = Directory.GetFiles(fullPath, "UnityPlayer.dll", SearchOption.TopDirectoryOnly);
        return found.FirstOrDefault();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private void OpenSingleMode()
    {
        ActiveSection = AppSection.Single;
        IsBatchMode = false;
    }

    private void OpenBatchMode()
    {
        ActiveSection = AppSection.Batch;
        IsBatchMode = true;
    }

    private void OpenUnitySection()
    {
        ActiveSection = AppSection.Unity;
    }

    private void OpenAdvancedSection()
    {
        ActiveSection = AppSection.Advanced;
    }

    private async Task InstallRequiredUnityAsync()
    {
        if (!CanInstallRequiredUnity)
        {
            return;
        }

        string targetVersion = string.IsNullOrWhiteSpace(RequiredUnityVersion)
            ? UnityVersion?.Trim() ?? string.Empty
            : RequiredUnityVersion;
        AddLog($"Installing Unity {targetVersion}...", LogLevel.Info);

        _unityInstallCts?.Cancel();
        _unityInstallCts?.Dispose();
        _unityInstallCts = new CancellationTokenSource();

        IsUnityInstallInProgress = true;
        try
        {
            var request = new UnityInstallRequest(
                RequiredVersion: RequiredUnityVersion,
                TargetGamePath: TargetGame,
                UnityInstallRoot: UnityInstallRoot,
                UnityHubPath: UnityHubPath,
                UnityEditorPath: UnityPath,
                PreferNonLts: PreferNonLts,
                AutoInstallHub: AutoInstallHub);

            UnityInstallResult result = await _unityProvisioning.InstallRequiredVersionAsync(request, _unityInstallCts.Token);

            if (result.Success && !string.IsNullOrWhiteSpace(result.InstalledPath))
            {
                UnityPath = result.InstalledPath;
            }

            AddLog(result.Message, result.Success ? LogLevel.Success : LogLevel.Error);
        }
        catch (OperationCanceledException)
        {
            AddLog("Unity installation cancelled.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            AddLog($"Unity installation failed: {ex.Message}", LogLevel.Error);
        }
        finally
        {
            IsUnityInstallInProgress = false;
            _unityInstallCts?.Dispose();
            _unityInstallCts = null;
            RefreshUnityRequirementStatus();
        }
    }

    private async Task StartBuildAsync()
    {
        if (!CanRunBuild)
        {
            return;
        }

        IsBuilding = true;
        BuildProgress = 0;
        Logs.Clear();

        AddLog($"Initializing {(IsBatchMode ? "Batch" : "Single")} Mode conversion...", LogLevel.Info);

        if (!IsBatchMode)
        {
            AddLog("Validating game environment...", LogLevel.Info);
            BuildProgress = 12;

            string gameName = Path.GetFileName(TargetGame);
            if (!string.IsNullOrWhiteSpace(gameName))
            {
                AddLog($"Analyzing {gameName}...", LogLevel.Info);
                BuildProgress = 20;
            }

            string fontName = Path.GetFileName(FontPath);
            if (!string.IsNullOrWhiteSpace(fontName))
            {
                AddLog($"Parsing font metadata from {fontName}", LogLevel.Info);
                BuildProgress = 28;
            }
        }

        List<string> arguments;
        try
        {
            arguments = BuildCliArguments();
        }
        catch (InvalidOperationException ex)
        {
            AddLog(ex.Message, LogLevel.Error);
            IsBuilding = false;
            return;
        }

        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = new CancellationTokenSource();

        try
        {
            var request = new BuildLaunchRequest(arguments, ResolveRepositoryRoot());
            BuildExecutionResult result = await _buildRunner.RunAsync(request, OnRunnerLine, _buildCts.Token);

            if (result.ExitCode == 0)
            {
                BuildProgress = 100;
                AddLog("Successfully built font bundle!", LogLevel.Success);
            }
            else
            {
                AddLog($"Build failed with exit code {result.ExitCode}.", LogLevel.Error);
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("Build cancelled.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            AddLog(ex.Message, LogLevel.Error);
        }
        finally
        {
            IsBuilding = false;
            _buildCts?.Dispose();
            _buildCts = null;
        }
    }

    private List<string> BuildCliArguments()
    {
        var args = new List<string>();

        if (IsBatchMode)
        {
            if (string.IsNullOrWhiteSpace(JobsFile))
            {
                throw new InvalidOperationException("Jobs manifest is required in Batch Processor mode.");
            }

            args.Add("--jobs-file");
            args.Add(JobsFile.Trim());

            args.Add("--max-workers");
            args.Add(ParseOrFallback(MaxWorkers, 1, allowZero: false, "Parallel threads").ToString(CultureInfo.InvariantCulture));

            if (ContinueOnJobError)
            {
                args.Add("--continue-on-job-error");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(TargetGame))
            {
                throw new InvalidOperationException("Step 1 is required: select game executable.");
            }

            if (string.IsNullOrWhiteSpace(FontPath))
            {
                throw new InvalidOperationException("Step 2 is required: select source font file.");
            }

            if (string.IsNullOrWhiteSpace(OutputPath))
            {
                throw new InvalidOperationException("Step 3 is required: select output directory.");
            }

            args.Add("--font");
            args.Add(FontPath.Trim());
            args.Add("--output");
            args.Add(OutputPath.Trim());

            if (!string.IsNullOrWhiteSpace(BundleName))
            {
                args.Add("--bundle-name");
                args.Add(BundleName.Trim());
            }

            if (!string.IsNullOrWhiteSpace(TmpName))
            {
                args.Add("--tmp-name");
                args.Add(TmpName.Trim());
            }
        }

        AddArgumentIfPresent(args, "--unity", UnityPath);
        AddArgumentIfPresent(args, "--unity-hub", UnityHubPath);
        AddArgumentIfPresent(args, "--unity-version", UnityVersion);
        AddArgumentIfPresent(args, "--target-game", TargetGame);
        AddArgumentIfPresent(args, "--unity-install-root", UnityInstallRoot);

        args.Add("--epoch");
        args.Add(SelectedEpoch.Value);

        args.Add(UseNographics ? "--use-nographics" : "--no-nographics");

        if (!AutoInstallUnity)
        {
            args.Add("--no-auto-install-unity");
        }

        if (!AutoInstallHub)
        {
            args.Add("--no-auto-install-hub");
        }

        if (PreferNonLts)
        {
            args.Add("--prefer-non-lts");
        }

        args.Add("--build-target");
        args.Add(SelectedBuildTarget.Value);

        AddArgumentIfPresent(args, "--atlas-sizes", AtlasSizes);

        args.Add("--point-size");
        args.Add(ParseOrFallback(PointSize, 90, allowZero: false, "Point size").ToString(CultureInfo.InvariantCulture));

        args.Add("--padding");
        args.Add(ParseOrFallback(Padding, 8, allowZero: true, "Padding").ToString(CultureInfo.InvariantCulture));

        args.Add("--scan-upper-bound");
        args.Add(ParseOrFallback(ScanUpperBound, 1_114_111, allowZero: true, "Scan upper bound").ToString(CultureInfo.InvariantCulture));

        if (ForceStatic && ForceDynamic)
        {
            throw new InvalidOperationException("Only one of Force Static Generation and Force Dynamic Atlas can be enabled.");
        }

        if (ForceStatic)
        {
            args.Add("--force-static");
        }

        if (ForceDynamic)
        {
            args.Add("--force-dynamic");
        }

        args.Add("--dynamic-warmup-limit");
        args.Add(ParseOrFallback(DynamicWarmupLimit, 20_000, allowZero: true, "Dynamic warmup limit").ToString(CultureInfo.InvariantCulture));

        args.Add("--dynamic-warmup-batch");
        args.Add(ParseOrFallback(DynamicWarmupBatch, 1_024, allowZero: false, "Dynamic warmup batch").ToString(CultureInfo.InvariantCulture));

        if (IncludeControl)
        {
            args.Add("--include-control");
        }

        if (KeepTemp)
        {
            args.Add("--keep-temp");
        }

        return args;
    }

    private int ParseOrFallback(string input, int fallback, bool allowZero, string fieldName)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
            (allowZero ? parsed >= 0 : parsed > 0))
        {
            return parsed;
        }

        AddLog($"{fieldName} is invalid; using default value {fallback}.", LogLevel.Warn);
        return fallback;
    }

    private static void AddArgumentIfPresent(List<string> args, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        args.Add(key);
        args.Add(value.Trim());
    }

    private static string ResolveRepositoryRoot()
    {
        DirectoryInfo? cursor = new DirectoryInfo(AppContext.BaseDirectory);
        while (cursor is not null)
        {
            string cliProject = Path.Combine(cursor.FullName, "FontPatcher.Cli", "FontPatcher.Cli.csproj");
            if (File.Exists(cliProject))
            {
                return cursor.FullName;
            }

            cursor = cursor.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private void OnRunnerLine(string line, bool isStdErr)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            LogLevel level = ParseLogLevel(line, isStdErr);
            AddLog(line, level);
            UpdateProgressFromLine(line);
        });
    }

    private void UpdateProgressFromLine(string line)
    {
        string normalized = line.ToLowerInvariant();

        if (normalized.Contains("validating game environment", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 14);
        }
        else if (normalized.Contains("analyzing", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 24);
        }
        else if (normalized.Contains("parsing font metadata", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 32);
        }
        else if (normalized.Contains("starting unity instance", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 42);
        }
        else if (normalized.Contains("scanning system for unity installation", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 18);
        }
        else if (normalized.Contains("using unity", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 26);
        }
        else if (normalized.Contains("creating temporary unity project", StringComparison.Ordinal) ||
                 normalized.Contains("createproject", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 48);
        }
        else if (normalized.Contains("generating sdf atlas", StringComparison.Ordinal) ||
                 normalized.Contains("generating signed distance field", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 68);
        }
        else if (normalized.Contains("compressing assetbundle", StringComparison.Ordinal) ||
                 normalized.Contains("building assetbundle", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 82);
        }
        else if (normalized.Contains("saving results to", StringComparison.Ordinal) ||
                 normalized.Contains("finalizing output assets", StringComparison.Ordinal))
        {
            BuildProgress = Math.Max(BuildProgress, 92);
        }
        else if (normalized.Contains("successfully built font bundle", StringComparison.Ordinal) ||
                 normalized.Contains("conversion completed", StringComparison.Ordinal) ||
                 normalized.Contains("batch completed", StringComparison.Ordinal))
        {
            BuildProgress = 100;
        }
    }

    private static LogLevel ParseLogLevel(string line, bool isStdErr)
    {
        if (isStdErr)
        {
            return LogLevel.Error;
        }

        string normalized = line.ToLowerInvariant();
        if (normalized.Contains("success", StringComparison.Ordinal) ||
            normalized.Contains("completed", StringComparison.Ordinal))
        {
            return LogLevel.Success;
        }

        if (normalized.Contains("warn", StringComparison.Ordinal))
        {
            return LogLevel.Warn;
        }

        if (normalized.Contains("error", StringComparison.Ordinal) ||
            normalized.Contains("failed", StringComparison.Ordinal) ||
            normalized.Contains("exception", StringComparison.Ordinal))
        {
            return LogLevel.Error;
        }

        return LogLevel.Info;
    }

    private void AddLog(string message, LogLevel level)
    {
        Logs.Add(new LogLineViewModel(DateTime.Now, level, message));
        while (Logs.Count > MaxLogLines)
        {
            Logs.RemoveAt(0);
        }
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasLogs));
        OnPropertyChanged(nameof(IsLogEmpty));
    }

    public void Dispose()
    {
        Logs.CollectionChanged -= OnLogsChanged;
        AvailableUnityVersions.CollectionChanged -= OnAvailableUnityVersionsChanged;
        _buildCts?.Cancel();
        _buildCts?.Dispose();
        _buildCts = null;
        _unityInstallCts?.Cancel();
        _unityInstallCts?.Dispose();
        _unityInstallCts = null;
    }

    private sealed class NullBuildRunner : IBuildRunner
    {
        public Task<BuildExecutionResult> RunAsync(
            BuildLaunchRequest request,
            Action<string, bool> onLine,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new BuildExecutionResult(0));
        }
    }
}
