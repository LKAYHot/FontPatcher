namespace FontPatcher.Cli;

internal sealed class CliOptions
{
    public required string FontPath { get; init; }

    public required string OutputDirectory { get; init; }

    public string? UnityEditorPath { get; init; }

    public string? UnityHubPath { get; init; }

    public string? UnityVersion { get; init; }

    public string? TargetGamePath { get; init; }

    public string? UnityInstallRoot { get; init; }

    public bool AutoInstallUnity { get; init; } = true;

    public bool AutoInstallUnityHub { get; init; } = true;

    public bool PreferLtsEditor { get; init; } = true;

    public required string BundleName { get; init; }

    public required string TmpAssetName { get; init; }

    public string BuildTarget { get; init; } = "StandaloneWindows64";

    public int SamplingPointSize { get; init; } = 90;

    public int Padding { get; init; } = 8;

    public int ScanUpperBound { get; init; } = 0x10FFFF;

    public int[] AtlasSizes { get; init; } = [1024, 2048, 4096];

    public bool KeepTempProject { get; init; }

    public bool ForceDynamic { get; init; }

    public bool ForceStatic { get; init; }

    public int DynamicWarmupLimit { get; init; } = 20_000;

    public int DynamicWarmupBatchSize { get; init; } = 1024;

    public bool IncludeControlCharacters { get; init; }

    public string? JobsFilePath { get; init; }

    public int MaxWorkers { get; init; } = 1;

    public bool ContinueOnJobError { get; init; }

    public EpochMode EpochMode { get; init; } = EpochMode.Auto;

    public bool? NoGraphicsOverride { get; init; }

    public bool BundleNameExplicit { get; init; }

    public bool TmpNameExplicit { get; init; }
}
