using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace FontPatcher.Cli;

internal sealed class UnityAutoProvisioner
{
    private const string HubInstallerUrl = "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-x64.exe";

    private static readonly Regex VersionRegex = new(
        @"\d{4}\.\d+\.\d+[abfp]\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex ChangesetRegex = new(
        @"\b[a-f0-9]{12,40}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex WindowsInstallerUrlRegex = new(
        @"https://download\.unity3d\.com/download_unity/(?<changeset>[a-f0-9]{12,40})/Windows64EditorInstaller/UnitySetup64(?:-(?<version>\d{4}\.\d+\.\d+[abfp]\d+))?\.exe",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly UnityEditorLocator _editorLocator;
    private readonly UnityHubLocator _hubLocator;
    private readonly UnityTargetVersionDetector _targetVersionDetector;
    private readonly ProcessRunner _processRunner;

    private HubCliMode _hubCliMode = HubCliMode.Unknown;

    public UnityAutoProvisioner(
        UnityEditorLocator editorLocator,
        UnityHubLocator hubLocator,
        UnityTargetVersionDetector targetVersionDetector,
        ProcessRunner processRunner)
    {
        _editorLocator = editorLocator;
        _hubLocator = hubLocator;
        _targetVersionDetector = targetVersionDetector;
        _processRunner = processRunner;
    }

    public async Task<string> ResolveEditorPathAsync(CliOptions options, CancellationToken cancellationToken)
    {
        string installRoot = GetEffectiveInstallRoot(options.UnityInstallRoot);

        if (!string.IsNullOrWhiteSpace(options.UnityEditorPath))
        {
            string explicitEditor = _editorLocator.ResolveExplicitPath(options.UnityEditorPath);
            await WaitForExecutableReadyAsync(explicitEditor, cancellationToken);
            return explicitEditor;
        }

        UnityVersion? desiredVersion = ResolveDesiredVersion(options);

        string? fromEnvironment = _editorLocator.TryResolveFromEnvironment();
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            if (!desiredVersion.HasValue || PathMatchesVersion(fromEnvironment, desiredVersion.Value))
            {
                await WaitForExecutableReadyAsync(fromEnvironment, cancellationToken);
                return fromEnvironment;
            }
        }

        if (desiredVersion.HasValue)
        {
            string? exact = _editorLocator.FindExactVersion(installRoot, desiredVersion.Value);
            if (!string.IsNullOrWhiteSpace(exact))
            {
                await WaitForExecutableReadyAsync(exact, cancellationToken);
                return exact;
            }

            string? closeByTrain = FindClosestInstalled(installRoot, desiredVersion.Value);
            if (!string.IsNullOrWhiteSpace(closeByTrain))
            {
                await WaitForExecutableReadyAsync(closeByTrain, cancellationToken);
                return closeByTrain;
            }
        }
        else
        {
            string? latestInstalled = _editorLocator.FindLatestInstalled(installRoot);
            if (!string.IsNullOrWhiteSpace(latestInstalled))
            {
                await WaitForExecutableReadyAsync(latestInstalled, cancellationToken);
                return latestInstalled;
            }
        }

        if (!options.AutoInstallUnity)
        {
            throw new InvalidOperationException(
                "Unity Editor is not installed and auto-install is disabled. " +
                "Set --unity <path> or remove --no-auto-install-unity.");
        }

        string hubPath = await EnsureUnityHubAsync(options, cancellationToken);
        await ConfigureInstallRootAsync(hubPath, installRoot, cancellationToken);

        IReadOnlyList<HubRelease> releases = await QueryHubReleasesAsync(hubPath, cancellationToken);
        HubRelease selectedRelease = await SelectReleaseForInstallAsync(
            releases,
            desiredVersion,
            options.PreferLtsEditor,
            cancellationToken);

        if (desiredVersion.HasValue && !selectedRelease.Version.Equals(desiredVersion.Value))
        {
            Console.WriteLine(
                $"Requested Unity {desiredVersion.Value} is unavailable in Hub releases. " +
                $"Using closest available {selectedRelease.Version}.");
        }

        await InstallEditorAsync(hubPath, selectedRelease, installRoot, cancellationToken);
        TrimManagedCacheIfNeeded(installRoot, selectedRelease.Version);

        string? installedPath = _editorLocator.FindExactVersion(installRoot, selectedRelease.Version);
        if (!string.IsNullOrWhiteSpace(installedPath))
        {
            await WaitForExecutableReadyAsync(installedPath, cancellationToken);
            return installedPath;
        }

        string? fallback = _editorLocator.FindLatestInstalled(installRoot);
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            await WaitForExecutableReadyAsync(fallback, cancellationToken);
            return fallback;
        }

        throw new InvalidOperationException(
            "Unity installation finished but Unity.exe was not found. " +
            "Use --unity-install-root to point to the correct editor folder.");
    }

    private UnityVersion? ResolveDesiredVersion(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.UnityVersion))
        {
            if (!UnityVersion.TryParse(options.UnityVersion, out UnityVersion parsed))
            {
                throw new InvalidOperationException(
                    $"Invalid --unity-version value: {options.UnityVersion}. Example: 2022.3.62f1");
            }

            return parsed;
        }

        return _targetVersionDetector.Detect(options.TargetGamePath);
    }

    private static bool PathMatchesVersion(string unityPath, UnityVersion target)
    {
        string? versionFolder = Directory.GetParent(unityPath)?.Parent?.Name;
        return UnityVersion.TryParse(versionFolder, out UnityVersion parsed) && parsed.Equals(target);
    }

    private string? FindClosestInstalled(string installRoot, UnityVersion desiredVersion)
    {
        return _editorLocator.DiscoverInstalledEditors(installRoot)
            .Where(x => x.Version.Major == desiredVersion.Major && x.Version.Minor == desiredVersion.Minor)
            .OrderBy(x => x.Version.Patch > desiredVersion.Patch ? 1 : 0)
            .ThenBy(x => Math.Abs(x.Version.Patch - desiredVersion.Patch))
            .ThenByDescending(x => x.Version)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private async Task<string> EnsureUnityHubAsync(CliOptions options, CancellationToken cancellationToken)
    {
        string? existingHub = _hubLocator.ResolvePath(options.UnityHubPath);
        if (!string.IsNullOrWhiteSpace(existingHub))
        {
            return existingHub;
        }

        if (!options.AutoInstallUnityHub)
        {
            throw new InvalidOperationException(
                "Unity Hub is missing and auto-install is disabled. " +
                "Set --unity-hub <path> or remove --no-auto-install-hub.");
        }

        string tempInstaller = Path.Combine(Path.GetTempPath(), $"UnityHubSetup-{Guid.NewGuid():N}.exe");
        try
        {
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.GetAsync(
                HubInstallerUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream destination = new(tempInstaller, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            ProcessResult installResult = await _processRunner.RunAsync(
                tempInstaller,
                "/S",
                workingDirectory: null,
                cancellationToken);

            if (installResult.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Unity Hub silent install failed with exit code {installResult.ExitCode}.{Environment.NewLine}" +
                    $"{MergeOutput(installResult)}");
            }

            string? installedHub = _hubLocator.ResolvePath(options.UnityHubPath);
            if (!string.IsNullOrWhiteSpace(installedHub))
            {
                return installedHub;
            }

            throw new InvalidOperationException("Unity Hub was installed but executable was not found.");
        }
        finally
        {
            TryDeleteFile(tempInstaller);
        }
    }

    private async Task ConfigureInstallRootAsync(
        string hubPath,
        string installRoot,
        CancellationToken cancellationToken)
    {
        string absolute = Path.GetFullPath(installRoot);
        Directory.CreateDirectory(absolute);

        ProcessResult result = await RunHubAsync(
            hubPath,
            $"--headless install-path -s {Quote(absolute)}",
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to configure Unity install root. Exit code: {result.ExitCode}.{Environment.NewLine}" +
                $"{MergeOutput(result)}");
        }
    }

    private async Task<IReadOnlyList<HubRelease>> QueryHubReleasesAsync(
        string hubPath,
        CancellationToken cancellationToken)
    {
        string[] commands =
        [
            "--headless editors -r",
            "--headless editors --releases"
        ];

        ProcessResult? lastResult = null;
        foreach (string command in commands)
        {
            ProcessResult result = await RunHubAsync(hubPath, command, cancellationToken);
            lastResult = result;

            string raw = result.StdOut + Environment.NewLine + result.StdErr;
            List<HubRelease> parsed = ParseReleaseOutput(raw);
            if (parsed.Count > 0)
            {
                return parsed;
            }

            if (result.ExitCode == 0)
            {
                continue;
            }

            if (!IsLikelyCommandVariantIssue(result))
            {
                break;
            }
        }

        if (lastResult is not null)
        {
            throw new InvalidOperationException(
                "Unable to get Unity release list from Hub CLI." + Environment.NewLine +
                MergeOutput(lastResult));
        }

        throw new InvalidOperationException("Unable to get Unity release list from Hub CLI.");
    }

    private async Task<HubRelease> SelectReleaseForInstallAsync(
        IReadOnlyList<HubRelease> releases,
        UnityVersion? desiredVersion,
        bool preferLts,
        CancellationToken cancellationToken)
    {
        if (releases.Count == 0)
        {
            throw new InvalidOperationException(
                "Unity Hub returned an empty release list. Pass --unity-version explicitly.");
        }

        if (desiredVersion.HasValue)
        {
            UnityVersion desired = desiredVersion.Value;
            HubRelease? exact = releases.FirstOrDefault(x => x.Version.Equals(desired));
            if (exact is not null)
            {
                return exact;
            }

            List<HubRelease> sameTrain = releases
                .Where(x => x.Version.Major == desired.Major && x.Version.Minor == desired.Minor)
                .ToList();

            if (sameTrain.Count > 0)
            {
                return sameTrain
                    .OrderBy(x => x.Version.Patch > desired.Patch ? 1 : 0)
                    .ThenBy(x => Math.Abs(x.Version.Patch - desired.Patch))
                    .ThenByDescending(x => x.Version)
                    .First();
            }

            HubRelease? archiveRelease = await TryResolveArchiveReleaseAsync(desired, cancellationToken);
            if (archiveRelease is not null)
            {
                return archiveRelease;
            }

            string knownTrains = string.Join(
                ", ",
                releases
                    .Select(x => $"{x.Version.Major}.{x.Version.Minor}")
                    .Distinct()
                    .OrderBy(x => x, StringComparer.Ordinal));

            throw new InvalidOperationException(
                $"No compatible Hub release found for Unity train {desired.Major}.{desired.Minor}. " +
                $"Known trains: {knownTrains}. " +
                "Install matching editor manually with --unity or pass --unity-version from an available train.");
        }

        IEnumerable<HubRelease> preferred = preferLts
            ? releases.Where(x => x.IsLts)
            : releases.Where(x => !x.IsLts);

        return preferred.Any()
            ? preferred.OrderByDescending(x => x.Version).First()
            : releases.OrderByDescending(x => x.Version).First();
    }

    private async Task<HubRelease?> TryResolveArchiveReleaseAsync(
        UnityVersion desiredVersion,
        CancellationToken cancellationToken)
    {
        string versionText = desiredVersion.ToString();
        string[] candidates =
        [
            $"https://unity.com/releases/editor/whats-new/{versionText}",
            $"https://unity.com/releases/editor/whats-new/{versionText.ToLowerInvariant()}"
        ];

        using var client = new HttpClient();
        foreach (string url in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                string html = await response.Content.ReadAsStringAsync(cancellationToken);
                Match match = WindowsInstallerUrlRegex.Match(html);
                if (!match.Success)
                {
                    continue;
                }

                string changeset = match.Groups["changeset"].Value;
                string installerUrl = match.Value;
                string matchedVersion = match.Groups["version"].Success
                    ? match.Groups["version"].Value
                    : versionText;

                if (!UnityVersion.TryParse(matchedVersion, out UnityVersion parsedVersion))
                {
                    parsedVersion = desiredVersion;
                }

                if (parsedVersion.Major != desiredVersion.Major || parsedVersion.Minor != desiredVersion.Minor)
                {
                    continue;
                }

                return new HubRelease(parsedVersion, IsLtsFromVersion(parsedVersion), changeset, installerUrl);
            }
            catch
            {
                // Ignore archive probing errors and continue fallback chain.
            }
        }

        return null;
    }

    private async Task InstallEditorAsync(
        string hubPath,
        HubRelease release,
        string installRoot,
        CancellationToken cancellationToken)
    {
        var attempts = new List<string>();

        if (!string.IsNullOrWhiteSpace(release.Changeset))
        {
            attempts.Add($"--headless install --version {release.Version} --changeset {release.Changeset}");
        }

        attempts.Add($"--headless install --version {release.Version}");

        ProcessResult? last = null;
        foreach (string command in attempts.Distinct(StringComparer.Ordinal))
        {
            ProcessResult result = await RunHubAsync(hubPath, command, cancellationToken);
            last = result;
            if (result.ExitCode == 0)
            {
                return;
            }

            if (!IsVersionNotFound(result))
            {
                break;
            }
        }

        if (last is not null)
        {
            if (await TryInstallEditorFromDirectInstallerAsync(release, installRoot, cancellationToken))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Unity Editor install failed for {release.Version}. Exit code: {last.ExitCode}.{Environment.NewLine}" +
                $"{MergeOutput(last)}");
        }

        if (await TryInstallEditorFromDirectInstallerAsync(release, installRoot, cancellationToken))
        {
            return;
        }

        throw new InvalidOperationException($"Unity Editor install failed for {release.Version}.");
    }

    private async Task<bool> TryInstallEditorFromDirectInstallerAsync(
        HubRelease release,
        string installRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(release.InstallerUrl))
        {
            return false;
        }

        Console.WriteLine($"Trying direct Unity installer fallback for {release.Version}.");

        string versionRoot = Path.Combine(Path.GetFullPath(installRoot), release.Version.ToString());
        string unityExe = Path.Combine(versionRoot, "Editor", "Unity.exe");
        if (File.Exists(unityExe))
        {
            return true;
        }

        Directory.CreateDirectory(versionRoot);
        string tempInstaller = Path.Combine(Path.GetTempPath(), $"UnitySetup64-{release.Version}-{Guid.NewGuid():N}.exe");
        try
        {
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.GetAsync(
                release.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (FileStream destination = new(tempInstaller, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination, cancellationToken);
            }

            string installArguments = $"/S /D={versionRoot}";
            ProcessResult result = await _processRunner.RunAsync(
                tempInstaller,
                installArguments,
                workingDirectory: null,
                cancellationToken);

            if (result.ExitCode != 0)
            {
                return false;
            }

            await WaitForInstallerProcessesToFinishAsync(release.Version, cancellationToken);

            bool installed = File.Exists(unityExe);
            if (installed)
            {
                Console.WriteLine($"Direct installer fallback succeeded for {release.Version}.");
            }

            return installed;
        }
        catch
        {
            return false;
        }
        finally
        {
            TryDeleteFile(tempInstaller);
        }
    }

    private async Task<ProcessResult> RunHubAsync(
        string hubPath,
        string hubArguments,
        CancellationToken cancellationToken)
    {
        await EnsureHubCliModeAsync(hubPath, cancellationToken);

        string args = _hubCliMode == HubCliMode.WithDoubleDash
            ? $"-- {hubArguments}"
            : hubArguments;

        ProcessResult result = await _processRunner.RunAsync(hubPath, args, workingDirectory: null, cancellationToken);

        if (_hubCliMode == HubCliMode.Direct && IsDirectSyntaxUnsupported(result))
        {
            _hubCliMode = HubCliMode.WithDoubleDash;
            string fallbackArgs = $"-- {hubArguments}";
            return await _processRunner.RunAsync(hubPath, fallbackArgs, workingDirectory: null, cancellationToken);
        }

        if (_hubCliMode == HubCliMode.WithDoubleDash && IsLegacySyntaxUnsupported(result))
        {
            _hubCliMode = HubCliMode.Direct;
            return await _processRunner.RunAsync(hubPath, hubArguments, workingDirectory: null, cancellationToken);
        }

        return result;
    }

    private async Task EnsureHubCliModeAsync(string hubPath, CancellationToken cancellationToken)
    {
        if (_hubCliMode != HubCliMode.Unknown)
        {
            return;
        }

        ProcessResult direct = await _processRunner.RunAsync(
            hubPath,
            "--headless help",
            workingDirectory: null,
            cancellationToken);

        if (!IsDirectSyntaxUnsupported(direct))
        {
            _hubCliMode = HubCliMode.Direct;
            return;
        }

        ProcessResult legacy = await _processRunner.RunAsync(
            hubPath,
            "-- --headless help",
            workingDirectory: null,
            cancellationToken);

        if (!IsLegacySyntaxUnsupported(legacy))
        {
            _hubCliMode = HubCliMode.WithDoubleDash;
            return;
        }

        _hubCliMode = HubCliMode.Direct;
    }

    private static List<HubRelease> ParseReleaseOutput(string output)
    {
        var result = new Dictionary<string, HubRelease>(StringComparer.OrdinalIgnoreCase);
        string[] lines = output
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string line in lines)
        {
            if (line.Contains("invalid key:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("unityrelease:", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("bit.ly/2XbVrpR#15", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool lts = line.Contains("lts", StringComparison.OrdinalIgnoreCase);
            string? changeset = ExtractChangeset(line);
            MatchCollection matches = VersionRegex.Matches(line);
            foreach (Match match in matches)
            {
                if (!match.Success)
                {
                    continue;
                }

                string rawVersion = match.Value;
                if (!UnityVersion.TryParse(rawVersion, out UnityVersion version))
                {
                    continue;
                }

                if (result.TryGetValue(rawVersion, out HubRelease? existing) && existing is not null)
                {
                    string? mergedChangeset = existing.Changeset;
                    if (string.IsNullOrWhiteSpace(mergedChangeset) && !string.IsNullOrWhiteSpace(changeset))
                    {
                        mergedChangeset = changeset;
                    }

                    result[rawVersion] = new HubRelease(
                        existing.Version,
                        existing.IsLts || lts,
                        mergedChangeset,
                        existing.InstallerUrl);
                }
                else
                {
                    result[rawVersion] = new HubRelease(version, lts, changeset);
                }
            }
        }

        return result.Values
            .OrderByDescending(x => x.Version)
            .ToList();
    }

    private static string? ExtractChangeset(string line)
    {
        MatchCollection matches = ChangesetRegex.Matches(line);
        foreach (Match match in matches)
        {
            string candidate = match.Value;
            if (candidate.Length >= 12)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsVersionNotFound(ProcessResult result)
    {
        string combined = (result.StdOut + "\n" + result.StdErr).ToLowerInvariant();
        return combined.Contains("provided editor version does not match") ||
               combined.Contains("does not match to any known unity editor versions") ||
               combined.Contains("unknown unity editor version");
    }

    private static bool IsLikelyCommandVariantIssue(ProcessResult result)
    {
        return IsDirectSyntaxUnsupported(result) || IsLegacySyntaxUnsupported(result);
    }

    private static bool IsDirectSyntaxUnsupported(ProcessResult result)
    {
        string combined = (result.StdOut + "\n" + result.StdErr).ToLowerInvariant();
        return combined.Contains("bad option: --headless");
    }

    private static bool IsLegacySyntaxUnsupported(ProcessResult result)
    {
        string combined = (result.StdOut + "\n" + result.StdErr).ToLowerInvariant();
        return combined.Contains("cannot find module '--headless'");
    }

    private static string MergeOutput(ProcessResult result)
    {
        string stdout = result.StdOut.Trim();
        string stderr = result.StdErr.Trim();

        if (string.IsNullOrWhiteSpace(stdout) && string.IsNullOrWhiteSpace(stderr))
        {
            return "No command output.";
        }

        return $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}{Environment.NewLine}" +
               $"stderr:{Environment.NewLine}{stderr}";
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static void TryDeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }

    private static string GetEffectiveInstallRoot(string? installRoot)
    {
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            return Path.GetFullPath(installRoot);
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "FontPatcher", "UnityEditors");
    }

    private static void TrimManagedCacheIfNeeded(string installRoot, UnityVersion keepVersion)
    {
        string normalizedInstallRoot = Path.GetFullPath(installRoot).TrimEnd('\\', '/');
        string normalizedDefaultRoot = GetEffectiveInstallRoot(null).TrimEnd('\\', '/');
        if (!string.Equals(normalizedInstallRoot, normalizedDefaultRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Directory.Exists(normalizedInstallRoot))
        {
            return;
        }

        foreach (string versionDirectory in Directory.GetDirectories(normalizedInstallRoot))
        {
            string versionName = Path.GetFileName(versionDirectory);
            if (!UnityVersion.TryParse(versionName, out UnityVersion parsed))
            {
                continue;
            }

            if (parsed.Equals(keepVersion))
            {
                continue;
            }

            TryDeleteDirectory(versionDirectory);
        }
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
            // Ignore cleanup failures.
        }
    }

    private static async Task WaitForExecutableReadyAsync(string executablePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Unity executable was not found.", executablePath);
        }

        const int attempts = 20;
        for (int i = 0; i < attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(
                    executablePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return;
            }
            catch (IOException) when (i < attempts - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (UnauthorizedAccessException) when (i < attempts - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Unity executable is still locked and cannot be opened: {executablePath}");
    }

    private static async Task WaitForInstallerProcessesToFinishAsync(
        UnityVersion version,
        CancellationToken cancellationToken)
    {
        string versionToken = version.ToString();
        DateTime deadline = DateTime.UtcNow.AddMinutes(20);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Process[] active = Process.GetProcesses()
                .Where(p => IsInstallerProcessForVersion(p, versionToken))
                .ToArray();

            if (active.Length == 0)
            {
                return;
            }

            foreach (Process process in active)
            {
                process.Dispose();
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    private static bool IsInstallerProcessForVersion(Process process, string versionToken)
    {
        string name;
        try
        {
            name = process.ProcessName;
        }
        catch
        {
            return false;
        }

        if (!name.StartsWith("UnitySetup64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains(versionToken, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLtsFromVersion(UnityVersion version)
    {
        if (version.Major >= 2023)
        {
            return false;
        }

        return version.Minor == 3 || version.Major == 2020;
    }

    private enum HubCliMode
    {
        Unknown,
        Direct,
        WithDoubleDash
    }

    private sealed record HubRelease(UnityVersion Version, bool IsLts, string? Changeset, string? InstallerUrl = null);
}


