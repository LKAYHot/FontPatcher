namespace FontPatcher.Cli;

internal sealed class UnityEpochResolver
{
    private readonly UnityTargetVersionDetector _targetVersionDetector;

    public UnityEpochResolver(UnityTargetVersionDetector targetVersionDetector)
    {
        _targetVersionDetector = targetVersionDetector;
    }

    public UnityEpochResolution Resolve(CliOptions options, string unityEditorPath)
    {
        UnityVersion? version = TryParseVersionFromEditorPath(unityEditorPath);

        if (!version.HasValue && !string.IsNullOrWhiteSpace(options.UnityVersion))
        {
            if (UnityVersion.TryParse(options.UnityVersion, out UnityVersion fromOption))
            {
                version = fromOption;
            }
        }

        if (!version.HasValue)
        {
            version = _targetVersionDetector.Detect(options.TargetGamePath);
        }

        BuildEpoch epoch = options.EpochMode switch
        {
            EpochMode.Legacy => BuildEpoch.Legacy2018To2020,
            EpochMode.Mid => BuildEpoch.Mid2021To2022,
            EpochMode.Modern => BuildEpoch.Modern2023Plus,
            _ => version.HasValue ? MapVersionToEpoch(version.Value) : BuildEpoch.Mid2021To2022
        };

        return new UnityEpochResolution(epoch, version);
    }

    private static UnityVersion? TryParseVersionFromEditorPath(string unityEditorPath)
    {
        if (string.IsNullOrWhiteSpace(unityEditorPath))
        {
            return null;
        }

        var directory = new FileInfo(unityEditorPath).Directory;
        while (directory is not null)
        {
            if (UnityVersion.TryParse(directory.Name, out UnityVersion version))
            {
                return version;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static BuildEpoch MapVersionToEpoch(UnityVersion version)
    {
        if (version.Major >= 6000 || version.Major >= 2023)
        {
            return BuildEpoch.Modern2023Plus;
        }

        if (version.Major >= 2021)
        {
            return BuildEpoch.Mid2021To2022;
        }

        return BuildEpoch.Legacy2018To2020;
    }
}
