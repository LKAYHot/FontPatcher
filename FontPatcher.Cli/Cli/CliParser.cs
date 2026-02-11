using System.Globalization;

namespace FontPatcher.Cli;

internal static class CliParser
{
    public static string HelpText => """
    Usage:
      FontPatcher.Cli --font <path> --output <dir> [options]
      FontPatcher.Cli --jobs-file <path> [shared-options]

    Required:
      --font <path>                 Input font (.ttf/.otf/.ttc/.otc)
      --output <dir>                Directory for AssetBundle output

    Optional:
      --jobs-file <path>            JSON file with batch jobs for clustered execution
      --max-workers <int>           Parallel workers in batch mode (default: 1)
      --continue-on-job-error       Continue remaining jobs after a job failure
      --unity <path>                Full path to Unity.exe (if omitted, auto-detect)
      --unity-hub <path>            Full path to Unity Hub.exe (optional)
      --unity-version <version>     Target editor version, e.g. 2021.3.38f1
      --target-game <path>          Game .exe, UnityPlayer.dll or *_Data folder for auto version detect
      --unity-install-root <path>   Editor installation root (default: %LOCALAPPDATA%\\FontPatcher\\UnityEditors)
      --epoch <auto|legacy|mid|modern>   Force epoch adapter selection (default: auto)
      --use-nographics              Force using -nographics when running Unity batch
      --no-nographics               Force disabling -nographics when running Unity batch
      --no-auto-install-unity       Disable automatic Unity editor installation
      --no-auto-install-hub         Disable automatic Unity Hub installation
      --prefer-non-lts              Prefer newest non-LTS release when auto-selecting version
      --bundle-name <name>          AssetBundle name (default: font filename)
      --tmp-name <name>             TMP asset name (default: TMP_<font name>)
      --build-target <target>       Unity BuildTarget (default: StandaloneWindows64)
      --atlas-sizes <csv>           Atlas candidates, e.g. 1024,2048,4096
      --point-size <int>            Sampling point size (default: 90)
      --padding <int>               Glyph padding (default: 8)
      --scan-upper-bound <int>      Max Unicode code point scan (default: 1114111)
      --force-static                Force static atlas mode
      --force-dynamic               Force dynamic multi-atlas mode
      --dynamic-warmup-limit <int>  Max glyphs to pre-seed in dynamic mode
      --dynamic-warmup-batch <int>  Batch size for dynamic glyph warmup
      --include-control             Include control chars < U+0020
      --keep-temp                   Keep generated temporary Unity worker project
      -h, --help                    Show help
    """;

    public static bool TryParse(
        string[] args,
        out CliOptions? options,
        out string? error,
        out bool showHelp)
    {
        options = null;
        error = null;
        showHelp = false;

        if (args.Length == 0)
        {
            showHelp = true;
            return true;
        }

        string? fontPath = null;
        string? outputDirectory = null;
        string? jobsFilePath = null;
        int maxWorkers = 1;
        bool continueOnJobError = false;
        string? unityEditorPath = null;
        string? unityHubPath = null;
        string? unityVersion = null;
        string? targetGamePath = null;
        string? unityInstallRoot = null;
        bool autoInstallUnity = true;
        bool autoInstallHub = true;
        bool preferLts = true;
        EpochMode epochMode = EpochMode.Auto;
        bool? noGraphicsOverride = null;
        string? bundleName = null;
        string? tmpName = null;
        bool bundleNameExplicit = false;
        bool tmpNameExplicit = false;
        string buildTarget = "StandaloneWindows64";
        int[] atlasSizes = [1024, 2048, 4096];
        int pointSize = 90;
        int padding = 8;
        int scanUpperBound = 0x10FFFF;
        bool keepTemp = false;
        bool forceDynamic = false;
        bool forceStatic = false;
        int dynamicWarmupLimit = 20_000;
        int dynamicWarmupBatch = 1024;
        bool includeControl = false;

        for (var i = 0; i < args.Length; i++)
        {
            string token = args[i];
            if (token is "-h" or "--help")
            {
                showHelp = true;
                return true;
            }

            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unexpected argument: {token}";
                return false;
            }

            (string key, string? value) = SplitToken(token);
            value ??= NextValueOrNull(args, ref i);

            switch (key)
            {
                case "--font":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--font requires a value.";
                        return false;
                    }

                    fontPath = value;
                    break;
                case "--output":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--output requires a value.";
                        return false;
                    }

                    outputDirectory = value;
                    break;
                case "--jobs-file":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--jobs-file requires a value.";
                        return false;
                    }

                    jobsFilePath = value;
                    break;
                case "--max-workers":
                    if (!TryParsePositiveInt(value, out maxWorkers))
                    {
                        error = "--max-workers must be > 0.";
                        return false;
                    }

                    break;
                case "--continue-on-job-error":
                    continueOnJobError = true;
                    break;
                case "--unity":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--unity requires a value.";
                        return false;
                    }

                    unityEditorPath = value;
                    break;
                case "--unity-hub":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--unity-hub requires a value.";
                        return false;
                    }

                    unityHubPath = value;
                    break;
                case "--unity-version":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--unity-version requires a value.";
                        return false;
                    }

                    unityVersion = value.Trim();
                    break;
                case "--target-game":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--target-game requires a value.";
                        return false;
                    }

                    targetGamePath = value;
                    break;
                case "--unity-install-root":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--unity-install-root requires a value.";
                        return false;
                    }

                    unityInstallRoot = value;
                    break;
                case "--epoch":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--epoch requires a value.";
                        return false;
                    }

                    if (!TryParseEpochMode(value, out epochMode))
                    {
                        error = "--epoch must be one of: auto, legacy, mid, modern.";
                        return false;
                    }

                    break;
                case "--use-nographics":
                    noGraphicsOverride = true;
                    break;
                case "--no-nographics":
                    noGraphicsOverride = false;
                    break;
                case "--no-auto-install-unity":
                    autoInstallUnity = false;
                    break;
                case "--no-auto-install-hub":
                    autoInstallHub = false;
                    break;
                case "--prefer-non-lts":
                    preferLts = false;
                    break;
                case "--bundle-name":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--bundle-name requires a value.";
                        return false;
                    }

                    bundleName = value;
                    bundleNameExplicit = true;
                    break;
                case "--tmp-name":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--tmp-name requires a value.";
                        return false;
                    }

                    tmpName = value;
                    tmpNameExplicit = true;
                    break;
                case "--build-target":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--build-target requires a value.";
                        return false;
                    }

                    buildTarget = value;
                    break;
                case "--atlas-sizes":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--atlas-sizes requires a value.";
                        return false;
                    }

                    if (!TryParseAtlasSizes(value, out atlasSizes, out error))
                    {
                        return false;
                    }

                    break;
                case "--point-size":
                    if (!TryParsePositiveInt(value, out pointSize))
                    {
                        error = "--point-size must be a positive integer.";
                        return false;
                    }

                    break;
                case "--padding":
                    if (!TryParseNonNegativeInt(value, out padding))
                    {
                        error = "--padding must be >= 0.";
                        return false;
                    }

                    break;
                case "--scan-upper-bound":
                    if (!TryParseNonNegativeInt(value, out scanUpperBound))
                    {
                        error = "--scan-upper-bound must be >= 0.";
                        return false;
                    }

                    break;
                case "--force-static":
                    forceStatic = true;
                    break;
                case "--force-dynamic":
                    forceDynamic = true;
                    break;
                case "--dynamic-warmup-limit":
                    if (!TryParseNonNegativeInt(value, out dynamicWarmupLimit))
                    {
                        error = "--dynamic-warmup-limit must be >= 0.";
                        return false;
                    }

                    break;
                case "--dynamic-warmup-batch":
                    if (!TryParsePositiveInt(value, out dynamicWarmupBatch))
                    {
                        error = "--dynamic-warmup-batch must be > 0.";
                        return false;
                    }

                    break;
                case "--include-control":
                    includeControl = true;
                    break;
                case "--keep-temp":
                    keepTemp = true;
                    break;
                default:
                    error = $"Unknown option: {key}";
                    return false;
            }
        }

        bool isBatchMode = !string.IsNullOrWhiteSpace(jobsFilePath);
        if (!isBatchMode)
        {
            if (string.IsNullOrWhiteSpace(fontPath))
            {
                error = "--font is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                error = "--output is required.";
                return false;
            }
        }

        if (forceStatic && forceDynamic)
        {
            error = "Use only one of --force-static or --force-dynamic.";
            return false;
        }

        fontPath = string.IsNullOrWhiteSpace(fontPath) ? string.Empty : Path.GetFullPath(fontPath);
        outputDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? string.Empty : Path.GetFullPath(outputDirectory);
        jobsFilePath = string.IsNullOrWhiteSpace(jobsFilePath) ? null : Path.GetFullPath(jobsFilePath);
        unityEditorPath = string.IsNullOrWhiteSpace(unityEditorPath) ? null : Path.GetFullPath(unityEditorPath);
        unityHubPath = string.IsNullOrWhiteSpace(unityHubPath) ? null : Path.GetFullPath(unityHubPath);
        targetGamePath = string.IsNullOrWhiteSpace(targetGamePath) ? null : Path.GetFullPath(targetGamePath);
        unityInstallRoot = string.IsNullOrWhiteSpace(unityInstallRoot) ? null : Path.GetFullPath(unityInstallRoot);

        string fontStem = string.IsNullOrWhiteSpace(fontPath)
            ? "font"
            : Path.GetFileNameWithoutExtension(fontPath);
        bundleName ??= fontStem.ToLowerInvariant();
        tmpName ??= $"TMP_{fontStem}";

        options = new CliOptions
        {
            FontPath = fontPath,
            OutputDirectory = outputDirectory,
            UnityEditorPath = unityEditorPath,
            UnityHubPath = unityHubPath,
            UnityVersion = unityVersion,
            TargetGamePath = targetGamePath,
            UnityInstallRoot = unityInstallRoot,
            AutoInstallUnity = autoInstallUnity,
            AutoInstallUnityHub = autoInstallHub,
            PreferLtsEditor = preferLts,
            BundleName = NameSanitizer.SanitizeBundleName(bundleName),
            TmpAssetName = NameSanitizer.SanitizeTmpName(tmpName),
            BuildTarget = buildTarget.Trim(),
            AtlasSizes = atlasSizes,
            SamplingPointSize = pointSize,
            Padding = padding,
            ScanUpperBound = scanUpperBound,
            KeepTempProject = keepTemp,
            ForceDynamic = forceDynamic,
            ForceStatic = forceStatic,
            DynamicWarmupLimit = dynamicWarmupLimit,
            DynamicWarmupBatchSize = dynamicWarmupBatch,
            IncludeControlCharacters = includeControl,
            JobsFilePath = jobsFilePath,
            MaxWorkers = maxWorkers,
            ContinueOnJobError = continueOnJobError,
            EpochMode = epochMode,
            NoGraphicsOverride = noGraphicsOverride,
            BundleNameExplicit = bundleNameExplicit,
            TmpNameExplicit = tmpNameExplicit
        };
        return true;
    }

    private static (string key, string? value) SplitToken(string token)
    {
        int separator = token.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0)
        {
            return (token, null);
        }

        string key = token[..separator];
        string value = token[(separator + 1)..];
        return (key, value);
    }

    private static string? NextValueOrNull(string[] args, ref int index)
    {
        int nextIndex = index + 1;
        if (nextIndex >= args.Length)
        {
            return null;
        }

        if (args[nextIndex].StartsWith("--", StringComparison.Ordinal))
        {
            return null;
        }

        index = nextIndex;
        return args[nextIndex];
    }

    private static bool TryParseAtlasSizes(string csv, out int[] values, out string? error)
    {
        var parts = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var parsed = new List<int>(parts.Length);
        foreach (string part in parts)
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size))
            {
                error = $"Invalid atlas size: {part}";
                values = Array.Empty<int>();
                return false;
            }

            if (size < 256 || size > 8192)
            {
                error = $"Atlas size out of range [256..8192]: {part}";
                values = Array.Empty<int>();
                return false;
            }

            parsed.Add(size);
        }

        if (parsed.Count == 0)
        {
            error = "At least one atlas size is required.";
            values = Array.Empty<int>();
            return false;
        }

        parsed.Sort();
        values = parsed.ToArray();
        error = null;
        return true;
    }

    private static bool TryParsePositiveInt(string? value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0;
    }

    private static bool TryParseNonNegativeInt(string? value, out int parsed)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed >= 0;
    }

    private static bool TryParseEpochMode(string value, out EpochMode epochMode)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                epochMode = EpochMode.Auto;
                return true;
            case "legacy":
                epochMode = EpochMode.Legacy;
                return true;
            case "mid":
                epochMode = EpochMode.Mid;
                return true;
            case "modern":
                epochMode = EpochMode.Modern;
                return true;
            default:
                epochMode = EpochMode.Auto;
                return false;
        }
    }

}
