using System.Globalization;

namespace FontPatcher.Avalonia.Services;

public sealed record BuildArgumentsInput(
    bool IsBatchMode,
    string JobsFile,
    string MaxWorkers,
    bool ContinueOnJobError,
    string TargetGame,
    string FontPath,
    string OutputPath,
    string BundleName,
    string TmpName,
    string UnityPath,
    string UnityHubPath,
    string UnityVersion,
    string UnityInstallRoot,
    string SelectedEpoch,
    bool UseNographics,
    bool AutoInstallUnity,
    bool AutoInstallHub,
    bool PreferNonLts,
    string SelectedBuildTarget,
    string AtlasSizes,
    string PointSize,
    string Padding,
    string ScanUpperBound,
    bool ForceStatic,
    bool ForceDynamic,
    string DynamicWarmupLimit,
    string DynamicWarmupBatch,
    bool IncludeControl,
    bool KeepTemp);

public sealed record BuildArgumentsResult(IReadOnlyList<string> Arguments, IReadOnlyList<string> Warnings);

public static class BuildArgumentsFactory
{
    public static BuildArgumentsResult Create(BuildArgumentsInput input)
    {
        var args = new List<string>();
        var warnings = new List<string>();

        if (input.IsBatchMode)
        {
            if (string.IsNullOrWhiteSpace(input.JobsFile))
            {
                throw new InvalidOperationException("Jobs manifest is required in Batch Processor mode.");
            }

            args.Add("--jobs-file");
            args.Add(input.JobsFile.Trim());

            args.Add("--max-workers");
            args.Add(ParseOrFallback(input.MaxWorkers, 1, allowZero: false, "Parallel threads", warnings)
                .ToString(CultureInfo.InvariantCulture));

            if (input.ContinueOnJobError)
            {
                args.Add("--continue-on-job-error");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(input.TargetGame))
            {
                throw new InvalidOperationException("Step 1 is required: select game executable.");
            }

            if (string.IsNullOrWhiteSpace(input.FontPath))
            {
                throw new InvalidOperationException("Step 2 is required: select source font file.");
            }

            if (string.IsNullOrWhiteSpace(input.OutputPath))
            {
                throw new InvalidOperationException("Step 3 is required: select output directory.");
            }

            args.Add("--font");
            args.Add(input.FontPath.Trim());
            args.Add("--output");
            args.Add(input.OutputPath.Trim());

            AddArgumentIfPresent(args, "--bundle-name", input.BundleName);
            AddArgumentIfPresent(args, "--tmp-name", input.TmpName);
        }

        AddArgumentIfPresent(args, "--unity", input.UnityPath);
        AddArgumentIfPresent(args, "--unity-hub", input.UnityHubPath);
        AddArgumentIfPresent(args, "--unity-version", input.UnityVersion);
        AddArgumentIfPresent(args, "--target-game", input.TargetGame);
        AddArgumentIfPresent(args, "--unity-install-root", input.UnityInstallRoot);

        args.Add("--epoch");
        args.Add(input.SelectedEpoch);

        args.Add(input.UseNographics ? "--use-nographics" : "--no-nographics");

        if (!input.AutoInstallUnity)
        {
            args.Add("--no-auto-install-unity");
        }

        if (!input.AutoInstallHub)
        {
            args.Add("--no-auto-install-hub");
        }

        if (input.PreferNonLts)
        {
            args.Add("--prefer-non-lts");
        }

        args.Add("--build-target");
        args.Add(input.SelectedBuildTarget);

        AddArgumentIfPresent(args, "--atlas-sizes", input.AtlasSizes);

        args.Add("--point-size");
        args.Add(ParseOrFallback(input.PointSize, 90, allowZero: false, "Point size", warnings)
            .ToString(CultureInfo.InvariantCulture));

        args.Add("--padding");
        args.Add(ParseOrFallback(input.Padding, 8, allowZero: true, "Padding", warnings)
            .ToString(CultureInfo.InvariantCulture));

        args.Add("--scan-upper-bound");
        args.Add(ParseOrFallback(input.ScanUpperBound, 1_114_111, allowZero: true, "Scan upper bound", warnings)
            .ToString(CultureInfo.InvariantCulture));

        if (input.ForceStatic && input.ForceDynamic)
        {
            throw new InvalidOperationException("Only one of Force Static Generation and Force Dynamic Atlas can be enabled.");
        }

        if (input.ForceStatic)
        {
            args.Add("--force-static");
        }

        if (input.ForceDynamic)
        {
            args.Add("--force-dynamic");
        }

        args.Add("--dynamic-warmup-limit");
        args.Add(ParseOrFallback(input.DynamicWarmupLimit, 20_000, allowZero: true, "Dynamic warmup limit", warnings)
            .ToString(CultureInfo.InvariantCulture));

        args.Add("--dynamic-warmup-batch");
        args.Add(ParseOrFallback(input.DynamicWarmupBatch, 1_024, allowZero: false, "Dynamic warmup batch", warnings)
            .ToString(CultureInfo.InvariantCulture));

        if (input.IncludeControl)
        {
            args.Add("--include-control");
        }

        if (input.KeepTemp)
        {
            args.Add("--keep-temp");
        }

        return new BuildArgumentsResult(args, warnings);
    }

    private static int ParseOrFallback(
        string input,
        int fallback,
        bool allowZero,
        string fieldName,
        ICollection<string> warnings)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
            (allowZero ? parsed >= 0 : parsed > 0))
        {
            return parsed;
        }

        warnings.Add($"{fieldName} is invalid; using default value {fallback}.");
        return fallback;
    }

    private static void AddArgumentIfPresent(ICollection<string> args, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        args.Add(key);
        args.Add(value.Trim());
    }
}
