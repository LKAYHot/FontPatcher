using CommunityToolkit.Mvvm.Input;
using FontPatcher.Avalonia.Services;

namespace FontPatcher.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanRunBuild))]
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
        BuildArgumentsResult result = BuildArgumentsFactory.Create(new BuildArgumentsInput(
            IsBatchMode,
            JobsFile,
            MaxWorkers,
            ContinueOnJobError,
            TargetGame,
            FontPath,
            OutputPath,
            BundleName,
            TmpName,
            UnityPath,
            UnityHubPath,
            UnityVersion,
            UnityInstallRoot,
            SelectedEpoch.Value,
            UseNographics,
            AutoInstallUnity,
            AutoInstallHub,
            PreferNonLts,
            SelectedBuildTarget.Value,
            AtlasSizes,
            PointSize,
            Padding,
            ScanUpperBound,
            ForceStatic,
            ForceDynamic,
            DynamicWarmupLimit,
            DynamicWarmupBatch,
            IncludeControl,
            KeepTemp));

        foreach (string warning in result.Warnings)
        {
            AddLog(warning, LogLevel.Warn);
        }

        return result.Arguments.ToList();
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
}
