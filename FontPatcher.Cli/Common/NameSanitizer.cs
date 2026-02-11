using System.Text;

namespace FontPatcher.Cli;

internal static class NameSanitizer
{
    public static string SanitizeBundleName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.Length == 0 ? "fontbundle" : builder.ToString();
    }

    public static string SanitizeTmpName(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char c in name.Trim())
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.Length == 0 ? "TMP_Font" : builder.ToString();
    }
}
