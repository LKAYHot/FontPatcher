using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FontPatcher.Cli;

public sealed record UnityRequirementCheckResult(
    bool HasRequiredVersion,
    string? RequiredVersion,
    bool IsInstalled,
    string? InstalledPath,
    string Message);

public sealed record UnityInstallRequest(
    string? RequiredVersion,
    string? TargetGamePath,
    string? UnityInstallRoot,
    string? UnityHubPath,
    string? UnityEditorPath,
    bool PreferNonLts,
    bool AutoInstallHub);

public sealed record UnityInstallResult(
    bool Success,
    string? RequiredVersion,
    string? InstalledPath,
    string Message);

public sealed record UnityInstalledVersionInfo(
    string Version,
    string Path);

public sealed class UnityProvisioningFacade
{
    private static readonly Regex UnityVersionExtractor = new(
        @"\d{4}\.\d+\.\d+[abfp]\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly UnityTargetVersionDetector _targetVersionDetector = new();
    private readonly UnityEditorLocator _editorLocator = new();

    public UnityRequirementCheckResult CheckRequiredVersion(
        string? requiredVersion,
        string? targetGamePath,
        string? unityInstallRoot,
        string? explicitUnityEditorPath)
    {
        string? resolvedRequiredVersion = ResolveRequiredVersion(requiredVersion, targetGamePath);
        if (string.IsNullOrWhiteSpace(resolvedRequiredVersion))
        {
            return new UnityRequirementCheckResult(
                HasRequiredVersion: false,
                RequiredVersion: null,
                IsInstalled: false,
                InstalledPath: null,
                Message: "Unity version is not defined yet. Select a target game or set Target Version manually.");
        }

        if (!UnityVersion.TryParse(resolvedRequiredVersion, out UnityVersion required))
        {
            return new UnityRequirementCheckResult(
                HasRequiredVersion: false,
                RequiredVersion: resolvedRequiredVersion,
                IsInstalled: false,
                InstalledPath: null,
                Message: $"Unity version '{resolvedRequiredVersion}' has invalid format. Example: 2022.3.62f1");
        }

        string installRoot = GetEffectiveInstallRoot(unityInstallRoot);

        string? explicitMatch = TryMatchExplicitEditor(required, explicitUnityEditorPath);
        if (!string.IsNullOrWhiteSpace(explicitMatch))
        {
            return new UnityRequirementCheckResult(
                HasRequiredVersion: true,
                RequiredVersion: required.ToString(),
                IsInstalled: true,
                InstalledPath: explicitMatch,
                Message: $"Unity {required} is installed (explicit path).");
        }

        IReadOnlyList<InstalledUnityEditor> discovered = DiscoverEditors(installRoot, explicitUnityEditorPath);
        InstalledUnityEditor? exact = discovered.FirstOrDefault(x => x.Version.Equals(required));
        if (exact is not null)
        {
            return new UnityRequirementCheckResult(
                HasRequiredVersion: true,
                RequiredVersion: required.ToString(),
                IsInstalled: true,
                InstalledPath: exact.Path,
                Message: $"Unity {required} is installed.");
        }

        string hint = BuildDiscoveredVersionsHint(discovered);
        return new UnityRequirementCheckResult(
            HasRequiredVersion: true,
            RequiredVersion: required.ToString(),
            IsInstalled: false,
            InstalledPath: null,
            Message: $"Unity {required} was not found.{hint} You can install it from this screen.");
    }

    public IReadOnlyList<UnityInstalledVersionInfo> DiscoverInstalledVersions(
        string? unityInstallRoot,
        string? explicitUnityEditorPath)
    {
        string installRoot = GetEffectiveInstallRoot(unityInstallRoot);
        return DiscoverEditors(installRoot, explicitUnityEditorPath)
            .Select(x => new UnityInstalledVersionInfo(x.Version.ToString(), x.Path))
            .ToArray();
    }

    public async Task<UnityInstallResult> InstallRequiredVersionAsync(
        UnityInstallRequest request,
        CancellationToken cancellationToken = default)
    {
        UnityRequirementCheckResult initial = CheckRequiredVersion(
            request.RequiredVersion,
            request.TargetGamePath,
            request.UnityInstallRoot,
            request.UnityEditorPath);

        if (!initial.HasRequiredVersion)
        {
            return new UnityInstallResult(
                Success: false,
                RequiredVersion: initial.RequiredVersion,
                InstalledPath: null,
                Message: initial.Message);
        }

        if (initial.IsInstalled)
        {
            return new UnityInstallResult(
                Success: true,
                RequiredVersion: initial.RequiredVersion,
                InstalledPath: initial.InstalledPath,
                Message: initial.Message);
        }

        var options = new CliOptions
        {
            FontPath = string.Empty,
            OutputDirectory = string.Empty,
            BundleName = "font",
            TmpAssetName = "TMP_font",
            UnityEditorPath = null,
            UnityHubPath = NormalizePath(request.UnityHubPath),
            UnityVersion = initial.RequiredVersion,
            TargetGamePath = NormalizePath(request.TargetGamePath),
            UnityInstallRoot = NormalizePath(request.UnityInstallRoot),
            AutoInstallUnity = true,
            AutoInstallUnityHub = request.AutoInstallHub,
            PreferLtsEditor = !request.PreferNonLts
        };

        var provisioner = new UnityAutoProvisioner(
            new UnityEditorLocator(),
            new UnityHubLocator(),
            new UnityTargetVersionDetector(),
            new ProcessRunner());

        string resolvedPath = await provisioner.ResolveEditorPathAsync(options, cancellationToken);

        UnityRequirementCheckResult afterInstall = CheckRequiredVersion(
            initial.RequiredVersion,
            request.TargetGamePath,
            request.UnityInstallRoot,
            resolvedPath);

        if (afterInstall.IsInstalled)
        {
            return new UnityInstallResult(
                Success: true,
                RequiredVersion: afterInstall.RequiredVersion,
                InstalledPath: afterInstall.InstalledPath,
                Message: $"Unity {afterInstall.RequiredVersion} installed successfully.");
        }

        string? resolvedVersion = TryResolveVersionFromEditorPath(resolvedPath);
        string suffix = string.IsNullOrWhiteSpace(resolvedVersion)
            ? string.Empty
            : $" Detected installed version: {resolvedVersion}.";

        return new UnityInstallResult(
            Success: false,
            RequiredVersion: initial.RequiredVersion,
            InstalledPath: resolvedPath,
            Message: $"Unity {initial.RequiredVersion} is still missing after install attempt.{suffix}");
    }

    private string? ResolveRequiredVersion(string? requiredVersion, string? targetGamePath)
    {
        if (!string.IsNullOrWhiteSpace(requiredVersion))
        {
            return requiredVersion.Trim();
        }

        UnityVersion? detected = _targetVersionDetector.Detect(targetGamePath);
        return detected?.ToString();
    }

    private IReadOnlyList<InstalledUnityEditor> DiscoverEditors(string installRoot, string? explicitUnityEditorPath)
    {
        var byPath = new Dictionary<string, InstalledUnityEditor>(StringComparer.OrdinalIgnoreCase);
        MergeEditors(byPath, _editorLocator.DiscoverInstalledEditors(installRoot));

        string? explicitRoot = TryGetParentEditorRoot(explicitUnityEditorPath);
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            MergeEditors(byPath, _editorLocator.DiscoverInstalledEditors(explicitRoot));
        }

        return byPath.Values
            .OrderByDescending(x => x.Version)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void MergeEditors(
        IDictionary<string, InstalledUnityEditor> target,
        IEnumerable<InstalledUnityEditor> source)
    {
        foreach (InstalledUnityEditor editor in source)
        {
            if (!target.ContainsKey(editor.Path))
            {
                target[editor.Path] = editor;
            }
        }
    }

    private string? TryMatchExplicitEditor(UnityVersion requiredVersion, string? explicitUnityEditorPath)
    {
        if (string.IsNullOrWhiteSpace(explicitUnityEditorPath))
        {
            return null;
        }

        string resolved;
        try
        {
            resolved = _editorLocator.ResolveExplicitPath(explicitUnityEditorPath);
        }
        catch
        {
            return null;
        }

        string? resolvedVersion = TryResolveVersionFromEditorPath(resolved);
        if (!UnityVersion.TryParse(resolvedVersion, out UnityVersion explicitVersion))
        {
            return null;
        }

        return explicitVersion.Equals(requiredVersion) ? resolved : null;
    }

    private string? TryGetParentEditorRoot(string? explicitUnityEditorPath)
    {
        if (string.IsNullOrWhiteSpace(explicitUnityEditorPath))
        {
            return null;
        }

        try
        {
            string resolved = _editorLocator.ResolveExplicitPath(explicitUnityEditorPath);
            DirectoryInfo? versionDirectory = Directory.GetParent(resolved)?.Parent;
            return versionDirectory?.Parent?.FullName;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDiscoveredVersionsHint(IReadOnlyList<InstalledUnityEditor> discovered)
    {
        if (discovered.Count == 0)
        {
            return " No installed Unity editors were detected.";
        }

        string versions = string.Join(
            ", ",
            discovered
                .Select(x => x.Version.ToString())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6));

        return string.IsNullOrWhiteSpace(versions)
            ? string.Empty
            : $" Installed versions: {versions}.";
    }

    private static string GetEffectiveInstallRoot(string? installRoot)
    {
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            try
            {
                return NormalizeInstallRoot(Path.GetFullPath(installRoot));
            }
            catch
            {
                // Ignore malformed custom path and continue with default managed root.
            }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "FontPatcher", "UnityEditors");
    }

    private static string NormalizeInstallRoot(string fullPath)
    {
        if (File.Exists(fullPath) &&
            string.Equals(Path.GetFileName(fullPath), "Unity.exe", StringComparison.OrdinalIgnoreCase))
        {
            DirectoryInfo? editorDirectory = Directory.GetParent(fullPath);
            DirectoryInfo? versionDirectory = editorDirectory?.Parent;
            if (versionDirectory is not null &&
                UnityVersion.TryParse(versionDirectory.Name, out _))
            {
                return versionDirectory.Parent?.FullName ?? versionDirectory.FullName;
            }

            return editorDirectory?.Parent?.FullName ?? editorDirectory?.FullName ?? fullPath;
        }

        if (!Directory.Exists(fullPath))
        {
            return fullPath;
        }

        var dir = new DirectoryInfo(fullPath);
        if (string.Equals(dir.Name, "Editor", StringComparison.OrdinalIgnoreCase))
        {
            DirectoryInfo? versionDirectory = dir.Parent;
            if (versionDirectory is not null &&
                UnityVersion.TryParse(versionDirectory.Name, out _))
            {
                return versionDirectory.Parent?.FullName ?? versionDirectory.FullName;
            }
        }

        if (UnityVersion.TryParse(dir.Name, out _))
        {
            return dir.Parent?.FullName ?? dir.FullName;
        }

        return dir.FullName;
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveVersionFromEditorPath(string editorPath)
    {
        try
        {
            string? folderVersion = Directory.GetParent(editorPath)?.Parent?.Name;
            if (UnityVersion.TryParse(folderVersion, out UnityVersion parsedFolderVersion))
            {
                return parsedFolderVersion.ToString();
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(editorPath);
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
}
