using FontPatcher.Avalonia.ViewModels;

namespace FontPatcher.Avalonia.Services;

public static class BuildOutputInterpreter
{
    private static readonly ProgressRule[] ProgressRules =
    [
        new(14, false, "validating game environment"),
        new(24, false, "analyzing"),
        new(32, false, "parsing font metadata"),
        new(42, false, "starting unity instance"),
        new(18, false, "scanning system for unity installation"),
        new(26, false, "using unity"),
        new(48, false, "creating temporary unity project", "createproject"),
        new(68, false, "generating sdf atlas", "generating signed distance field"),
        new(82, false, "compressing assetbundle", "building assetbundle"),
        new(92, false, "saving results to", "finalizing output assets"),
        new(100, true, "successfully built font bundle", "conversion completed", "batch completed")
    ];

    public static int ResolveProgress(string line, int currentProgress)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return currentProgress;
        }

        string normalized = line.ToLowerInvariant();
        foreach (ProgressRule rule in ProgressRules)
        {
            if (!rule.Matches(normalized))
            {
                continue;
            }

            return rule.IsTerminal ? rule.Progress : Math.Max(currentProgress, rule.Progress);
        }

        return currentProgress;
    }

    public static LogLevel ParseLogLevel(string line, bool isStdErr)
    {
        if (isStdErr)
        {
            return LogLevel.Error;
        }

        string normalized = line.ToLowerInvariant();
        if (normalized.Contains("success", StringComparison.Ordinal) ||
            normalized.Contains("completed", StringComparison.Ordinal))
        {
            return LogLevel.Success;
        }

        if (normalized.Contains("warn", StringComparison.Ordinal))
        {
            return LogLevel.Warn;
        }

        if (normalized.Contains("error", StringComparison.Ordinal) ||
            normalized.Contains("failed", StringComparison.Ordinal) ||
            normalized.Contains("exception", StringComparison.Ordinal))
        {
            return LogLevel.Error;
        }

        return LogLevel.Info;
    }

    private sealed record ProgressRule(int Progress, bool IsTerminal, params string[] Markers)
    {
        public bool Matches(string normalizedLine)
        {
            foreach (string marker in Markers)
            {
                if (normalizedLine.Contains(marker, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
