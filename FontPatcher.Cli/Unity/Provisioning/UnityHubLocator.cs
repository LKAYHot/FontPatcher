namespace FontPatcher.Cli;

internal sealed class UnityHubLocator
{
    private const string HubExecutableName = "Unity Hub.exe";

    public string? ResolvePath(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string resolved = ResolveCandidate(explicitPath);
            return File.Exists(resolved) ? resolved : null;
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable("UNITY_HUB_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            string resolved = ResolveCandidate(fromEnvironment);
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        foreach (string candidate in GetDefaultCandidates())
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string? GetInstallPath()
    {
        return GetDefaultCandidates().FirstOrDefault(File.Exists);
    }

    private static IEnumerable<string> GetDefaultCandidates()
    {
        string? programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
        string? localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");

        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Unity Hub", HubExecutableName);
        }

        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return Path.Combine(localAppData, "Programs", "Unity Hub", HubExecutableName);
        }
    }

    private static string ResolveCandidate(string candidate)
    {
        string full = Path.GetFullPath(candidate);
        if (Directory.Exists(full))
        {
            string combined = Path.Combine(full, HubExecutableName);
            if (File.Exists(combined))
            {
                return combined;
            }
        }

        return full;
    }
}
