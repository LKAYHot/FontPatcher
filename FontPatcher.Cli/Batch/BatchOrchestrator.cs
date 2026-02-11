using System.Collections.Concurrent;
using System.Text.Json;

namespace FontPatcher.Cli;

internal sealed class BatchOrchestrator
{
    private readonly Func<ConversionPipeline> _pipelineFactory;

    public BatchOrchestrator(Func<ConversionPipeline> pipelineFactory)
    {
        _pipelineFactory = pipelineFactory;
    }

    public async Task<BatchRunResult> RunAsync(CliOptions baseOptions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(baseOptions.JobsFilePath))
        {
            throw new InvalidOperationException("Batch mode requires --jobs-file.");
        }

        BatchJobsDocument document = LoadDocument(baseOptions.JobsFilePath);
        if (document.jobs is null || document.jobs.Count == 0)
        {
            throw new InvalidOperationException("Jobs file does not contain any jobs.");
        }

        if (!baseOptions.ContinueOnJobError)
        {
            var sequential = new List<BatchJobResult>(document.jobs.Count);
            for (int i = 0; i < document.jobs.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BatchJobDescriptor descriptor = document.jobs[i];
                BatchJobResult result = await RunSingleJobAsync(baseOptions, descriptor, i, cancellationToken);
                sequential.Add(result);
                if (!result.Success)
                {
                    return new BatchRunResult(sequential);
                }
            }

            return new BatchRunResult(sequential);
        }

        int maxWorkers = Math.Max(1, baseOptions.MaxWorkers);
        using var semaphore = new SemaphoreSlim(maxWorkers, maxWorkers);
        var bag = new ConcurrentBag<BatchJobResult>();
        var tasks = new List<Task>(document.jobs.Count);

        for (int i = 0; i < document.jobs.Count; i++)
        {
            int index = i;
            BatchJobDescriptor descriptor = document.jobs[i];
            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    BatchJobResult result = await RunSingleJobAsync(baseOptions, descriptor, index, cancellationToken);
                    bag.Add(result);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        List<BatchJobResult> ordered = bag.OrderBy(x => x.Index).ToList();
        return new BatchRunResult(ordered);
    }

    private async Task<BatchJobResult> RunSingleJobAsync(
        CliOptions baseOptions,
        BatchJobDescriptor descriptor,
        int index,
        CancellationToken cancellationToken)
    {
        string jobName = string.IsNullOrWhiteSpace(descriptor.id) ? $"job-{index + 1}" : descriptor.id!;
        try
        {
            CliOptions options = MergeJob(baseOptions, descriptor);
            ConversionPipeline pipeline = _pipelineFactory();
            PipelineResult result = await pipeline.RunAsync(options, cancellationToken);

            return BatchJobResult.SuccessResult(index, jobName, result.BundleFilePath, result.UnityEditorPath);
        }
        catch (Exception ex)
        {
            return BatchJobResult.FailureResult(index, jobName, ex.Message);
        }
    }

    private static CliOptions MergeJob(CliOptions baseOptions, BatchJobDescriptor descriptor)
    {
        string? font = FirstNonEmpty(descriptor.font, baseOptions.FontPath);
        string? output = FirstNonEmpty(descriptor.output, baseOptions.OutputDirectory);
        if (string.IsNullOrWhiteSpace(font))
        {
            throw new InvalidOperationException("Batch job is missing 'font'.");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Batch job is missing 'output'.");
        }

        string fullFont = Path.GetFullPath(font);
        string fullOutput = Path.GetFullPath(output);

        string fontStem = Path.GetFileNameWithoutExtension(fullFont);
        string bundleName = !string.IsNullOrWhiteSpace(descriptor.bundleName)
            ? descriptor.bundleName
            : baseOptions.BundleNameExplicit
                ? baseOptions.BundleName
                : fontStem.ToLowerInvariant();
        string tmpName = !string.IsNullOrWhiteSpace(descriptor.tmpName)
            ? descriptor.tmpName
            : baseOptions.TmpNameExplicit
                ? baseOptions.TmpAssetName
                : $"TMP_{fontStem}";

        EpochMode epochMode = descriptor.epoch switch
        {
            null or "" => baseOptions.EpochMode,
            _ => ParseEpochMode(descriptor.epoch)
        };

        return new CliOptions
        {
            FontPath = fullFont,
            OutputDirectory = fullOutput,
            UnityEditorPath = FirstNonEmpty(descriptor.unity, baseOptions.UnityEditorPath),
            UnityHubPath = baseOptions.UnityHubPath,
            UnityVersion = FirstNonEmpty(descriptor.unityVersion, baseOptions.UnityVersion),
            TargetGamePath = FirstNonEmpty(descriptor.targetGame, baseOptions.TargetGamePath),
            UnityInstallRoot = baseOptions.UnityInstallRoot,
            AutoInstallUnity = baseOptions.AutoInstallUnity,
            AutoInstallUnityHub = baseOptions.AutoInstallUnityHub,
            PreferLtsEditor = baseOptions.PreferLtsEditor,
            BundleName = NameSanitizer.SanitizeBundleName(bundleName),
            TmpAssetName = NameSanitizer.SanitizeTmpName(tmpName),
            BuildTarget = FirstNonEmpty(descriptor.buildTarget, baseOptions.BuildTarget) ?? baseOptions.BuildTarget,
            SamplingPointSize = descriptor.pointSize ?? baseOptions.SamplingPointSize,
            Padding = descriptor.padding ?? baseOptions.Padding,
            ScanUpperBound = descriptor.scanUpperBound ?? baseOptions.ScanUpperBound,
            AtlasSizes = descriptor.atlasSizes ?? baseOptions.AtlasSizes,
            KeepTempProject = descriptor.keepTemp ?? baseOptions.KeepTempProject,
            ForceDynamic = descriptor.forceDynamic ?? baseOptions.ForceDynamic,
            ForceStatic = descriptor.forceStatic ?? baseOptions.ForceStatic,
            DynamicWarmupLimit = descriptor.dynamicWarmupLimit ?? baseOptions.DynamicWarmupLimit,
            DynamicWarmupBatchSize = descriptor.dynamicWarmupBatch ?? baseOptions.DynamicWarmupBatchSize,
            IncludeControlCharacters = descriptor.includeControl ?? baseOptions.IncludeControlCharacters,
            JobsFilePath = baseOptions.JobsFilePath,
            MaxWorkers = baseOptions.MaxWorkers,
            ContinueOnJobError = baseOptions.ContinueOnJobError,
            EpochMode = epochMode,
            NoGraphicsOverride = descriptor.useNoGraphics ?? baseOptions.NoGraphicsOverride,
            BundleNameExplicit = !string.IsNullOrWhiteSpace(descriptor.bundleName) || baseOptions.BundleNameExplicit,
            TmpNameExplicit = !string.IsNullOrWhiteSpace(descriptor.tmpName) || baseOptions.TmpNameExplicit
        };
    }

    private static BatchJobsDocument LoadDocument(string jobsFilePath)
    {
        string absolutePath = Path.GetFullPath(jobsFilePath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Jobs file not found.", absolutePath);
        }

        string json = File.ReadAllText(absolutePath);
        BatchJobsDocument? parsed = JsonSerializer.Deserialize<BatchJobsDocument>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        return parsed ?? new BatchJobsDocument();
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            return primary;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private static EpochMode ParseEpochMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => EpochMode.Auto,
            "legacy" => EpochMode.Legacy,
            "mid" => EpochMode.Mid,
            "modern" => EpochMode.Modern,
            _ => throw new InvalidOperationException($"Unknown epoch value in job: {value}")
        };
    }
}

internal sealed class BatchJobsDocument
{
    public List<BatchJobDescriptor> jobs { get; init; } = [];
}

internal sealed class BatchJobDescriptor
{
    public string? id { get; init; }

    public string? font { get; init; }

    public string? output { get; init; }

    public string? unity { get; init; }

    public string? unityVersion { get; init; }

    public string? targetGame { get; init; }

    public string? buildTarget { get; init; }

    public string? bundleName { get; init; }

    public string? tmpName { get; init; }

    public string? epoch { get; init; }

    public bool? useNoGraphics { get; init; }

    public int? pointSize { get; init; }

    public int? padding { get; init; }

    public int? scanUpperBound { get; init; }

    public int[]? atlasSizes { get; init; }

    public bool? includeControl { get; init; }

    public bool? keepTemp { get; init; }

    public bool? forceDynamic { get; init; }

    public bool? forceStatic { get; init; }

    public int? dynamicWarmupLimit { get; init; }

    public int? dynamicWarmupBatch { get; init; }
}

internal sealed class BatchRunResult
{
    public BatchRunResult(IReadOnlyList<BatchJobResult> jobs)
    {
        Jobs = jobs;
    }

    public IReadOnlyList<BatchJobResult> Jobs { get; }

    public int SuccessCount => Jobs.Count(x => x.Success);

    public int FailureCount => Jobs.Count(x => !x.Success);

    public bool AllSucceeded => FailureCount == 0;
}

internal sealed class BatchJobResult
{
    private BatchJobResult(int index, string jobName, bool success, string message)
    {
        Index = index;
        JobName = jobName;
        Success = success;
        Message = message;
    }

    public int Index { get; }

    public string JobName { get; }

    public bool Success { get; }

    public string Message { get; }

    public static BatchJobResult SuccessResult(int index, string jobName, string bundlePath, string unityEditor)
    {
        return new BatchJobResult(index, jobName, true, $"ok | bundle={bundlePath} | unity={unityEditor}");
    }

    public static BatchJobResult FailureResult(int index, string jobName, string error)
    {
        return new BatchJobResult(index, jobName, false, error);
    }
}
