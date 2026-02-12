using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontPatcher.Cli;
using FontPatcher.Avalonia.Models;
using FontPatcher.Avalonia.Services;

namespace FontPatcher.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private const int MaxLogLines = 220;

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

        Logs.CollectionChanged += OnLogsChanged;
        AvailableUnityVersions.CollectionChanged += OnAvailableUnityVersionsChanged;
        LoadUserSettings();
        RefreshUnityRequirementStatus();
    }

    public ObservableCollection<LogLineViewModel> Logs { get; } = [];
    public ObservableCollection<UnityInstalledVersionInfo> AvailableUnityVersions { get; } = [];

    public IReadOnlyList<OptionItem> EpochOptions { get; }

    public IReadOnlyList<OptionItem> BuildTargetOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleTabActive))]
    [NotifyPropertyChangedFor(nameof(IsBatchTabActive))]
    [NotifyPropertyChangedFor(nameof(IsUnityTabActive))]
    [NotifyPropertyChangedFor(nameof(IsAdvancedTabActive))]
    [NotifyPropertyChangedFor(nameof(IsSingleSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsBatchSectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsUnitySectionVisible))]
    [NotifyPropertyChangedFor(nameof(IsAdvancedSectionVisible))]
    private AppSection activeSection = AppSection.Single;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSingleMode))]
    [NotifyPropertyChangedFor(nameof(IsBuildReady))]
    [NotifyPropertyChangedFor(nameof(CanRunBuild))]
    [NotifyPropertyChangedFor(nameof(ShowBuildWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusReady))]
    [NotifyPropertyChangedFor(nameof(IsStatusPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsSingleFinalPolishEnabled))]
    [NotifyPropertyChangedFor(nameof(SingleFinalPolishOpacity))]
    [NotifyCanExecuteChangedFor(nameof(StartBuildCommand))]
    private bool isBatchMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMainLayoutVisible))]
    [NotifyPropertyChangedFor(nameof(IsLogFullscreenVisible))]
    [NotifyPropertyChangedFor(nameof(LogViewToggleText))]
    private bool isLogFullscreen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuildReady))]
    [NotifyPropertyChangedFor(nameof(CanRunBuild))]
    [NotifyPropertyChangedFor(nameof(ShowBuildWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusReady))]
    [NotifyPropertyChangedFor(nameof(IsStatusPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsSingleStep3Enabled))]
    [NotifyPropertyChangedFor(nameof(SingleStep3Opacity))]
    [NotifyPropertyChangedFor(nameof(IsSingleFinalPolishEnabled))]
    [NotifyPropertyChangedFor(nameof(SingleFinalPolishOpacity))]
    [NotifyCanExecuteChangedFor(nameof(StartBuildCommand))]
    private string fontPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuildReady))]
    [NotifyPropertyChangedFor(nameof(CanRunBuild))]
    [NotifyPropertyChangedFor(nameof(ShowBuildWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusReady))]
    [NotifyPropertyChangedFor(nameof(IsStatusPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsSingleFinalPolishEnabled))]
    [NotifyPropertyChangedFor(nameof(SingleFinalPolishOpacity))]
    [NotifyCanExecuteChangedFor(nameof(StartBuildCommand))]
    private string outputPath = string.Empty;
    [ObservableProperty] private string bundleName = string.Empty;
    [ObservableProperty] private string tmpName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuildReady))]
    [NotifyPropertyChangedFor(nameof(CanRunBuild))]
    [NotifyPropertyChangedFor(nameof(ShowBuildWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusReady))]
    [NotifyPropertyChangedFor(nameof(IsStatusPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsSingleFinalPolishEnabled))]
    [NotifyPropertyChangedFor(nameof(SingleFinalPolishOpacity))]
    [NotifyCanExecuteChangedFor(nameof(StartBuildCommand))]
    private string jobsFile = string.Empty;
    [ObservableProperty] private string maxWorkers = "1";
    [ObservableProperty] private bool continueOnJobError;

    [ObservableProperty] private string unityPath = @"C:/Program Files/Unity/Hub/Editor/2022.3.10f1/Editor/Unity.exe";
    [ObservableProperty] private string unityHubPath = @"C:/Program Files/Unity Hub/Unity Hub.exe";
    [ObservableProperty] private string unityVersion = "2022.3.10f1";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuildReady))]
    [NotifyPropertyChangedFor(nameof(CanRunBuild))]
    [NotifyPropertyChangedFor(nameof(ShowBuildWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusReady))]
    [NotifyPropertyChangedFor(nameof(IsStatusPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsSingleStep3Enabled))]
    [NotifyPropertyChangedFor(nameof(SingleStep3Opacity))]
    [NotifyPropertyChangedFor(nameof(IsSingleFinalPolishEnabled))]
    [NotifyPropertyChangedFor(nameof(SingleFinalPolishOpacity))]
    [NotifyCanExecuteChangedFor(nameof(StartBuildCommand))]
    private string targetGame = string.Empty;
    [ObservableProperty] private string unityInstallRoot = @"C:/UnityInstallations";
    [ObservableProperty] private OptionItem selectedEpoch;
    [ObservableProperty] private bool useNographics = true;
    [ObservableProperty] private bool autoInstallUnity = true;
    [ObservableProperty] private bool autoInstallHub = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrictLtsMode))]
    private bool preferNonLts;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallRequiredUnityButton))]
    [NotifyPropertyChangedFor(nameof(CanInstallRequiredUnity))]
    [NotifyCanExecuteChangedFor(nameof(InstallRequiredUnityCommand))]
    private bool hasRequiredUnityVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstallRequiredUnityButton))]
    [NotifyPropertyChangedFor(nameof(CanInstallRequiredUnity))]
    [NotifyCanExecuteChangedFor(nameof(InstallRequiredUnityCommand))]
    private bool isRequiredUnityInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequiredUnityVersionDisplayText))]
    [NotifyPropertyChangedFor(nameof(InstallRequiredUnityButtonText))]
    private string requiredUnityVersion = string.Empty;
    [ObservableProperty] private string unityRequirementStatusText = "Unity version is not defined yet.";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstalledUnityEditorPath))]
    private string installedUnityEditorPath = string.Empty;
    [ObservableProperty] private UnityInstalledVersionInfo? selectedInstalledUnityVersion;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallRequiredUnity))]
    [NotifyPropertyChangedFor(nameof(InstallRequiredUnityButtonText))]
    [NotifyCanExecuteChangedFor(nameof(InstallRequiredUnityCommand))]
    private bool isUnityInstallInProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPlatformText))]
    private OptionItem selectedBuildTarget;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunBuildButtonText))]
    [NotifyPropertyChangedFor(nameof(HasProgress))]
    [NotifyPropertyChangedFor(nameof(CanRunBuild))]
    [NotifyPropertyChangedFor(nameof(ShowBuildWarning))]
    [NotifyPropertyChangedFor(nameof(IsStatusProcessing))]
    [NotifyPropertyChangedFor(nameof(IsStatusReady))]
    [NotifyPropertyChangedFor(nameof(IsStatusPending))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(CanInstallRequiredUnity))]
    [NotifyCanExecuteChangedFor(nameof(StartBuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallRequiredUnityCommand))]
    private bool isBuilding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    private int buildProgress;

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

    partial void OnTargetGameChanged(string value)
    {
        bool versionChanged = AutoDetectUnityVersionFromTargetGame(value);
        if (!versionChanged)
        {
            RefreshUnityRequirementStatus();
        }

        SaveUserSettings();
    }

    partial void OnUnityVersionChanged(string value)
    {
        RefreshUnityRequirementStatus();
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

    [RelayCommand]
    private void OpenSingleMode()
    {
        ActiveSection = AppSection.Single;
        IsBatchMode = false;
    }

    [RelayCommand]
    private void OpenBatchMode()
    {
        ActiveSection = AppSection.Batch;
        IsBatchMode = true;
    }

    [RelayCommand]
    private void OpenUnity()
    {
        ActiveSection = AppSection.Unity;
    }

    [RelayCommand]
    private void OpenAdvanced()
    {
        ActiveSection = AppSection.Advanced;
    }

    [RelayCommand]
    private void FlushLogs()
    {
        Logs.Clear();
    }

    [RelayCommand]
    private void ToggleLogFullscreen()
    {
        IsLogFullscreen = !IsLogFullscreen;
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
