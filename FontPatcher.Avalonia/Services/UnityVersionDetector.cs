using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FontPatcher.Avalonia.Services;

public static class UnityVersionDetector
{
    private static readonly Regex UnityVersionExtractor = new(
        @"\d{4}\.\d+\.\d+[abfp]\d+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string? Detect(string targetGamePath)
    {
        if (string.IsNullOrWhiteSpace(targetGamePath))
        {
            return null;
        }

        try
        {
            string fullPath = Path.GetFullPath(targetGamePath);
            string? unityPlayerDll = ResolveUnityPlayerDllPath(fullPath);
            if (unityPlayerDll is null || !File.Exists(unityPlayerDll))
            {
                return null;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(unityPlayerDll);
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

    private static string? ResolveUnityPlayerDllPath(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            if (string.Equals(Path.GetFileName(fullPath), "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return null;
            }

            string sibling = Path.Combine(directory, "UnityPlayer.dll");
            return File.Exists(sibling) ? sibling : null;
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
            if (!string.IsNullOrWhiteSpace(parent))
            {
                string sibling = Path.Combine(parent, "UnityPlayer.dll");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }
        }

        string[] found = Directory.GetFiles(fullPath, "UnityPlayer.dll", SearchOption.TopDirectoryOnly);
        return found.FirstOrDefault();
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
