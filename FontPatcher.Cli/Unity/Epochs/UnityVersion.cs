using System.Text.RegularExpressions;

namespace FontPatcher.Cli;

internal readonly record struct UnityVersion(int Major, int Minor, int Patch, char Stream, int StreamNumber)
    : IComparable<UnityVersion>
{
    private static readonly Regex Parser = new(
        @"^(?<major>\d{4})\.(?<minor>\d+)\.(?<patch>\d+)(?<stream>[abfp])(?<streamNumber>\d+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? input, out UnityVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Match match = Parser.Match(input.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["major"].Value, out int major) ||
            !int.TryParse(match.Groups["minor"].Value, out int minor) ||
            !int.TryParse(match.Groups["patch"].Value, out int patch) ||
            !int.TryParse(match.Groups["streamNumber"].Value, out int streamNumber))
        {
            return false;
        }

        char stream = char.ToLowerInvariant(match.Groups["stream"].Value[0]);
        version = new UnityVersion(major, minor, patch, stream, streamNumber);
        return true;
    }

    public int CompareTo(UnityVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0)
        {
            return cmp;
        }

        cmp = StreamRank(Stream).CompareTo(StreamRank(other.Stream));
        if (cmp != 0)
        {
            return cmp;
        }

        return StreamNumber.CompareTo(other.StreamNumber);
    }

    public override string ToString()
    {
        return $"{Major}.{Minor}.{Patch}{char.ToLowerInvariant(Stream)}{StreamNumber}";
    }

    private static int StreamRank(char stream) => char.ToLowerInvariant(stream) switch
    {
        'a' => 0,
        'b' => 1,
        'f' => 2,
        'p' => 3,
        _ => -1
    };
}
