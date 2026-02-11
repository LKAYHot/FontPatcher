namespace FontPatcher.Cli;

internal enum BuildEpoch
{
    Legacy2018To2020,
    Mid2021To2022,
    Modern2023Plus
}

internal enum EpochMode
{
    Auto,
    Legacy,
    Mid,
    Modern
}

internal sealed record UnityEpochResolution(BuildEpoch Epoch, UnityVersion? Version);
