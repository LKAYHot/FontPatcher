using System.Collections.Specialized;
using CommunityToolkit.Mvvm.Input;
using FontPatcher.Avalonia.Services;
using FontPatcher.Cli;

namespace FontPatcher.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
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
        string? detectedVersion = UnityVersionDetector.Detect(targetGamePath);
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

    [RelayCommand(CanExecute = nameof(CanInstallRequiredUnity))]
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
}
