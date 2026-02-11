using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FontPatcher.Cli;

internal sealed class ConversionPipeline
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf",
        ".otf",
        ".ttc",
        ".otc"
    };

    private readonly UnityAutoProvisioner _provisioner;
    private readonly ProcessRunner _processRunner;
    private readonly UnityEpochResolver _epochResolver;
    private readonly UnityEpochAdapterRegistry _adapterRegistry;

    public ConversionPipeline(
        UnityAutoProvisioner provisioner,
        ProcessRunner processRunner,
        UnityEpochResolver epochResolver,
        UnityEpochAdapterRegistry adapterRegistry)
    {
        _provisioner = provisioner;
        _processRunner = processRunner;
        _epochResolver = epochResolver;
        _adapterRegistry = adapterRegistry;
    }

    public async Task<PipelineResult> RunAsync(CliOptions options, CancellationToken cancellationToken)
    {
        ValidateInput(options);

        string unityEditorPath = await _provisioner.ResolveEditorPathAsync(options, cancellationToken);
        UnityEpochResolution epochResolution = _epochResolver.Resolve(options, unityEditorPath);
        IUnityEpochAdapter adapter = _adapterRegistry.Get(epochResolution.Epoch);
        bool useNoGraphics = options.NoGraphicsOverride ?? adapter.DefaultUseNoGraphics;

        string runRoot = Path.Combine(Path.GetTempPath(), "FontPatcherCli", Guid.NewGuid().ToString("N"));
        string workerProject = Path.Combine(runRoot, "UnityWorker");
        string workerTag = Path.GetFileName(runRoot);
        string createLog = Path.Combine(runRoot, "unity-create.log");
        string buildLog = Path.Combine(runRoot, "unity-build.log");
        string outputDirectory = Path.GetFullPath(options.OutputDirectory);

        Directory.CreateDirectory(runRoot);
        Directory.CreateDirectory(outputDirectory);

        try
        {
            await CreateUnityProjectAsync(
                unityEditorPath,
                workerProject,
                createLog,
                useNoGraphics,
                workerTag,
                cancellationToken);
            BuilderScriptSpec builderScript = adapter.GetBuilderScript();
            string jobManifestPath = PrepareWorkerProject(workerProject, options, outputDirectory, builderScript);
            await ExecuteBuildAsync(
                unityEditorPath,
                workerProject,
                builderScript.EntryMethod,
                jobManifestPath,
                buildLog,
                useNoGraphics,
                workerTag,
                cancellationToken);

            string bundlePath = Path.Combine(outputDirectory, options.BundleName);
            string bundleManifestPath = $"{bundlePath}.manifest";

            if (!File.Exists(bundlePath))
            {
                throw new InvalidOperationException(
                    $"AssetBundle was not produced at expected path: {bundlePath}{Environment.NewLine}" +
                    $"Unity log: {buildLog}");
            }

            string workerPath = options.KeepTempProject ? workerProject : string.Empty;
            return new PipelineResult(
                bundlePath,
                bundleManifestPath,
                options.TmpAssetName,
                workerPath,
                unityEditorPath,
                epochResolution.Epoch,
                adapter.Name,
                useNoGraphics);
        }
        finally
        {
            if (!options.KeepTempProject)
            {
                TryDeleteDirectory(runRoot);
            }
        }
    }

    private static void ValidateInput(CliOptions options)
    {
        if (!File.Exists(options.FontPath))
        {
            throw new FileNotFoundException("Input font not found.", options.FontPath);
        }

        string extension = Path.GetExtension(options.FontPath);
        if (!SupportedExtensions.Contains(extension))
        {
            throw new InvalidOperationException(
                $"Unsupported font extension: {extension}. Supported: {string.Join(", ", SupportedExtensions)}");
        }
    }

    private async Task CreateUnityProjectAsync(
        string unityEditorPath,
        string workerProjectPath,
        string createLogPath,
        bool useNoGraphics,
        string workerTag,
        CancellationToken cancellationToken)
    {
        string arguments =
            BuildBatchArguments(useNoGraphics) +
            $" -createProject {Quote(workerProjectPath)} -logFile {Quote(createLogPath)}";

        ProcessResult createResult = await RunUnityWithLiveLogAsync(
            unityEditorPath,
            arguments,
            workingDirectory: null,
            createLogPath,
            $"unity:{workerTag}:create",
            cancellationToken);

        if (createResult.ExitCode != 0)
        {
            string logTail = ReadLogTail(createLogPath);
            throw new InvalidOperationException(
                $"Unity failed creating worker project. Exit code: {createResult.ExitCode}{Environment.NewLine}" +
                $"{BuildUnityFailureHint(createResult.ExitCode, logTail)}{logTail}");
        }
    }

    private async Task ExecuteBuildAsync(
        string unityEditorPath,
        string workerProjectPath,
        string entryMethod,
        string jobManifestPath,
        string buildLogPath,
        bool useNoGraphics,
        string workerTag,
        CancellationToken cancellationToken)
    {
        string arguments =
            BuildBatchArguments(useNoGraphics) + " " +
            $"-projectPath {Quote(workerProjectPath)} " +
            $"-executeMethod {entryMethod} " +
            $"--job-manifest {Quote(jobManifestPath)} " +
            $"-logFile {Quote(buildLogPath)}";

        ProcessResult buildResult = await RunUnityWithLiveLogAsync(
            unityEditorPath,
            arguments,
            workingDirectory: workerProjectPath,
            buildLogPath,
            $"unity:{workerTag}:build",
            cancellationToken);

        if (buildResult.ExitCode != 0)
        {
            string logTail = ReadLogTail(buildLogPath);
            throw new InvalidOperationException(
                $"Unity batch build failed. Exit code: {buildResult.ExitCode}{Environment.NewLine}" +
                $"{BuildUnityFailureHint(buildResult.ExitCode, logTail)}{logTail}");
        }
    }

    private static string PrepareWorkerProject(
        string workerProjectPath,
        CliOptions options,
        string absoluteOutputDirectory,
        BuilderScriptSpec builderScript)
    {
        string assetsDirectory = Path.Combine(workerProjectPath, "Assets");
        string inputFontsDirectory = Path.Combine(assetsDirectory, "InputFonts");
        string editorDirectory = Path.Combine(assetsDirectory, "Editor");
        Directory.CreateDirectory(inputFontsDirectory);
        Directory.CreateDirectory(editorDirectory);

        string fontFileName = Path.GetFileName(options.FontPath);
        string copiedFontPath = Path.Combine(inputFontsDirectory, fontFileName);
        File.Copy(options.FontPath, copiedFontPath, overwrite: true);
        EnsureTextMeshProDependency(workerProjectPath);

        string scriptFileName = string.IsNullOrWhiteSpace(builderScript.OutputFileName)
            ? "FontBundleBuilder.cs"
            : builderScript.OutputFileName;
        string editorScriptPath = Path.Combine(editorDirectory, scriptFileName);
        File.WriteAllText(editorScriptPath, builderScript.SourceCode);

        var job = new UnityJobManifest
        {
            fontAssetPath = $"Assets/InputFonts/{fontFileName}".Replace('\\', '/'),
            unityOutputDirAssetPath = "Assets/Generated",
            absoluteBundleOutputDir = absoluteOutputDirectory,
            assetBundleName = options.BundleName,
            tmpAssetName = options.TmpAssetName,
            buildTarget = options.BuildTarget,
            atlasSizes = options.AtlasSizes,
            samplingPointSize = options.SamplingPointSize,
            padding = options.Padding,
            scanUpperBound = options.ScanUpperBound,
            forceDynamic = options.ForceDynamic,
            forceStatic = options.ForceStatic,
            includeControlCharacters = options.IncludeControlCharacters,
            dynamicWarmupLimit = options.DynamicWarmupLimit,
            dynamicWarmupBatchSize = options.DynamicWarmupBatchSize
        };

        string jobManifestPath = Path.Combine(workerProjectPath, "FontPatcherJob.json");
        string json = JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jobManifestPath, json);
        return jobManifestPath;
    }

    private static void EnsureTextMeshProDependency(string workerProjectPath)
    {
        string packagesDirectory = Path.Combine(workerProjectPath, "Packages");
        string manifestPath = Path.Combine(packagesDirectory, "manifest.json");

        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException(
                $"Unity project manifest was not found: {manifestPath}");
        }

        string json = File.ReadAllText(manifestPath);
        JsonNode? rootNode = JsonNode.Parse(json);
        if (rootNode is not JsonObject rootObject)
        {
            throw new InvalidOperationException(
                $"Unity project manifest is invalid JSON: {manifestPath}");
        }

        JsonObject dependencies =
            rootObject["dependencies"] as JsonObject ?? new JsonObject();
        rootObject["dependencies"] = dependencies;

        if (dependencies["com.unity.textmeshpro"] is null)
        {
            dependencies["com.unity.textmeshpro"] = "3.0.6";
            string updatedJson = rootObject.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(manifestPath, updatedJson);
        }
    }

    private static string ReadLogTail(string logPath, int maxLines = 120)
    {
        if (!File.Exists(logPath))
        {
            return $"Unity log file is missing: {logPath}";
        }

        string[] lines = ReadAllLinesWithRetries(logPath);
        int skip = Math.Max(0, lines.Length - maxLines);
        string joined = string.Join(Environment.NewLine, lines.Skip(skip));
        return $"Unity log tail ({logPath}):{Environment.NewLine}{joined}";
    }

    private static string BuildUnityFailureHint(int exitCode, string logTail)
    {
        if (LooksLikeLicensingIssue(exitCode, logTail))
        {
            return
                "Unity licensing issue detected. " +
                "Open this Unity editor version once interactively and complete license activation, " +
                "then re-run FontPatcher in batch mode." + Environment.NewLine;
        }

        return string.Empty;
    }

    private static bool LooksLikeLicensingIssue(int exitCode, string logTail)
    {
        if (exitCode == 199)
        {
            return true;
        }

        string normalized = logTail.ToLowerInvariant();
        return normalized.Contains("license client") ||
               normalized.Contains("licensing::module") ||
               normalized.Contains("ipc channel to licensingclient") ||
               normalized.Contains("failed to activate/update license");
    }

    private static string[] ReadAllLinesWithRetries(string logPath)
    {
        const int maxAttempts = 15;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    logPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);

                var lines = new List<string>();
                while (!reader.EndOfStream)
                {
                    string? line = reader.ReadLine();
                    if (line is not null)
                    {
                        lines.Add(line);
                    }
                }

                return lines.ToArray();
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                Thread.Sleep(500);
            }
        }

        return Array.Empty<string>();
    }

    private async Task<ProcessResult> RunUnityWithLiveLogAsync(
        string unityEditorPath,
        string arguments,
        string? workingDirectory,
        string logPath,
        string phase,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{phase}] start");

        Task<ProcessResult> processTask = _processRunner.RunAsync(
            unityEditorPath,
            arguments,
            workingDirectory,
            cancellationToken);
        Task streamTask = StreamUnityLogAsync(logPath, phase, processTask, cancellationToken);

        try
        {
            ProcessResult result = await processTask;
            Console.WriteLine($"[{phase}] completed (exit={result.ExitCode})");
            return result;
        }
        finally
        {
            try
            {
                await streamTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Ignore cancellation during live log streaming.
            }
        }
    }

    private static async Task StreamUnityLogAsync(
        string logPath,
        string phase,
        Task processTask,
        CancellationToken cancellationToken)
    {
        long offset = 0;
        var pending = new StringBuilder();
        int idleAfterExit = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool hadNewData = TryReadNewLogChunk(logPath, ref offset, out string chunk);

            if (hadNewData)
            {
                idleAfterExit = 0;
                EmitUnityLogLines(chunk, pending, phase);
            }

            if (!processTask.IsCompleted)
            {
                await Task.Delay(250, cancellationToken);
                continue;
            }

            if (!hadNewData)
            {
                idleAfterExit++;
            }

            if (idleAfterExit >= 3)
            {
                break;
            }

            await Task.Delay(120, cancellationToken);
        }

        if (pending.Length > 0)
        {
            string line = pending.ToString().TrimEnd('\r', '\n');
            if (!string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"[{phase}] {line}");
            }
        }
    }

    private static bool TryReadNewLogChunk(string logPath, ref long offset, out string chunk)
    {
        chunk = string.Empty;
        if (!File.Exists(logPath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (offset > stream.Length)
            {
                offset = 0;
            }

            if (offset >= stream.Length)
            {
                return false;
            }

            stream.Seek(offset, SeekOrigin.Begin);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096,
                leaveOpen: true);
            chunk = reader.ReadToEnd();
            offset = stream.Position;
            return chunk.Length > 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void EmitUnityLogLines(string chunk, StringBuilder pending, string phase)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        pending.Append(chunk);
        string aggregate = pending.ToString();
        string[] lines = aggregate.Split('\n');
        pending.Clear();

        for (int i = 0; i < lines.Length - 1; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (!string.IsNullOrWhiteSpace(line))
            {
                Console.WriteLine($"[{phase}] {line}");
            }
        }

        string trailing = lines[^1];
        if (!string.IsNullOrEmpty(trailing))
        {
            pending.Append(trailing);
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string BuildBatchArguments(bool useNoGraphics)
    {
        return useNoGraphics ? "-batchmode -nographics -quit" : "-batchmode -quit";
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Keep temp artifacts if cleanup fails.
        }
    }
}

internal sealed record PipelineResult(
    string BundleFilePath,
    string BundleManifestPath,
    string TmpAssetName,
    string WorkerProjectPath,
    string UnityEditorPath,
    BuildEpoch Epoch,
    string AdapterName,
    bool UseNoGraphics);

internal sealed class UnityJobManifest
{
    public required string fontAssetPath { get; init; }

    public required string unityOutputDirAssetPath { get; init; }

    public required string absoluteBundleOutputDir { get; init; }

    public required string assetBundleName { get; init; }

    public required string tmpAssetName { get; init; }

    public required string buildTarget { get; init; }

    public required int[] atlasSizes { get; init; }

    public int samplingPointSize { get; init; }

    public int padding { get; init; }

    public int scanUpperBound { get; init; }

    public bool forceDynamic { get; init; }

    public bool forceStatic { get; init; }

    public bool includeControlCharacters { get; init; }

    public int dynamicWarmupLimit { get; init; }

    public int dynamicWarmupBatchSize { get; init; }
}
