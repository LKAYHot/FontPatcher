using System.Text.RegularExpressions;

namespace FontPatcher.Cli;

internal sealed class UnityEditorLocator
{
    private const string UnityExecutableName = "Unity.exe";
    private static readonly Regex LooseVersionExtractor = new(
        @"(?<version>\d{4}\.\d+\.\d+[abfp]\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public string ResolveExplicitPath(string explicitPath)
    {
        string resolved = ResolveCandidate(explicitPath);
        if (File.Exists(resolved))
        {
            return resolved;
        }

        throw new FileNotFoundException("Unity Editor was not found at provided path.", resolved);
    }

    public string? TryResolveFromEnvironment()
    {
        string? fromEnvironment = Environment.GetEnvironmentVariable("UNITY_EDITOR_PATH");
        if (string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return null;
        }

        string resolved = ResolveCandidate(fromEnvironment);
        return File.Exists(resolved) ? resolved : null;
    }

    public IReadOnlyList<InstalledUnityEditor> DiscoverInstalledEditors(string? installRoot)
    {
        var result = new Dictionary<string, InstalledUnityEditor>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in GetPossibleRoots(installRoot).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string normalizedRoot;
            try
            {
                normalizedRoot = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            AddEditorIfExists(result, normalizedRoot, normalizedRoot);

            if (!Directory.Exists(normalizedRoot))
            {
                continue;
            }

            string[] versionDirectories;
            try
            {
                versionDirectories = Directory.GetDirectories(normalizedRoot);
            }
            catch
            {
                continue;
            }

            foreach (string versionDirectory in versionDirectories)
            {
                AddEditorIfExists(result, normalizedRoot, versionDirectory);
            }
        }

        return result.Values
            .OrderByDescending(x => x.Version)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string? FindLatestInstalled(string? installRoot)
    {
        return DiscoverInstalledEditors(installRoot)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    public string? FindExactVersion(string? installRoot, UnityVersion version)
    {
        return DiscoverInstalledEditors(installRoot)
            .Where(x => x.Version.Equals(version))
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetPossibleRoots(string? installRoot)
    {
        if (!string.IsNullOrWhiteSpace(installRoot))
        {
            string normalized;
            try
            {
                normalized = Path.GetFullPath(installRoot);
            }
            catch
            {
                normalized = installRoot;
            }

            yield return normalized;
        }

        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        string? programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
        string? programFilesW6432 = Environment.GetEnvironmentVariable("ProgramW6432");
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Unity", "Hub", "Editor");
            yield return Path.Combine(programFiles, "Unity");
        }

        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            yield return Path.Combine(programFilesX86, "Unity", "Hub", "Editor");
            yield return Path.Combine(programFilesX86, "Unity");
        }

        if (!string.IsNullOrWhiteSpace(programFilesW6432))
        {
            yield return Path.Combine(programFilesW6432, "Unity", "Hub", "Editor");
            yield return Path.Combine(programFilesW6432, "Unity");
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Unity", "Hub", "Editor");
            yield return Path.Combine(localAppData, "Unity", "Hub", "Editor");
        }
    }

    private static void AddEditorIfExists(
        IDictionary<string, InstalledUnityEditor> result,
        string root,
        string versionDirectory)
    {
        string versionName = Path.GetFileName(versionDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!TryParseVersionName(versionName, out UnityVersion version))
        {
            return;
        }

        string unityPath = Path.Combine(versionDirectory, "Editor", UnityExecutableName);
        if (!File.Exists(unityPath))
        {
            return;
        }

        if (result.ContainsKey(unityPath))
        {
            return;
        }

        result[unityPath] = new InstalledUnityEditor(version, unityPath, root);
    }

    private static bool TryParseVersionName(string? versionName, out UnityVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(versionName))
        {
            return false;
        }

        if (UnityVersion.TryParse(versionName, out version))
        {
            return true;
        }

        Match match = LooseVersionExtractor.Match(versionName);
        return match.Success && UnityVersion.TryParse(match.Groups["version"].Value, out version);
    }

    private static string ResolveCandidate(string candidate)
    {
        string full = Path.GetFullPath(candidate);
        if (Directory.Exists(full))
        {
            string editorJoined = Path.Combine(full, "Editor", UnityExecutableName);
            if (File.Exists(editorJoined))
            {
                return editorJoined;
            }
        }

        return full;
    }
}

internal sealed record InstalledUnityEditor(UnityVersion Version, string Path, string Root);
