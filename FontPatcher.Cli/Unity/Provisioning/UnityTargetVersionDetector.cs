using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FontPatcher.Cli;

internal sealed class UnityTargetVersionDetector
{
    private static readonly Regex VersionExtractor = new(
        @"\d{4}\.\d+\.\d+[abfp]\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public UnityVersion? Detect(string? targetGamePath)
    {
        if (string.IsNullOrWhiteSpace(targetGamePath))
        {
            return null;
        }

        string full = Path.GetFullPath(targetGamePath);
        string? unityPlayerDll = ResolveUnityPlayerDll(full);
        if (unityPlayerDll is null || !File.Exists(unityPlayerDll))
        {
            return null;
        }

        FileVersionInfo info = FileVersionInfo.GetVersionInfo(unityPlayerDll);
        string? raw = FirstNonEmpty(info.ProductVersion, info.FileVersion, info.Comments);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        Match match = VersionExtractor.Match(raw);
        if (!match.Success)
        {
            return null;
        }

        return UnityVersion.TryParse(match.Value, out UnityVersion parsed) ? parsed : null;
    }

    private static string? ResolveUnityPlayerDll(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            if (string.Equals(Path.GetFileName(fullPath), "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (directory is null)
            {
                return null;
            }

            string candidate = Path.Combine(directory, "UnityPlayer.dll");
            return File.Exists(candidate) ? candidate : null;
        }

        if (!Directory.Exists(fullPath))
        {
            return null;
        }

        string direct = Path.Combine(fullPath, "UnityPlayer.dll");
        if (File.Exists(direct))
        {
            return direct;
        }

        if (fullPath.EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(fullPath)?.FullName;
            if (parent is not null)
            {
                string sibling = Path.Combine(parent, "UnityPlayer.dll");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }
        }

        string[] nested = Directory.GetFiles(fullPath, "UnityPlayer.dll", SearchOption.TopDirectoryOnly);
        return nested.FirstOrDefault();
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
