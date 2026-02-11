using System.Text.Json;

namespace FontPatcher.Cli;

internal static class BuilderScriptRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Lazy<IReadOnlyDictionary<BuildEpoch, BuilderScriptSpec>> CachedScripts =
        new(LoadScripts);

    public static BuilderScriptSpec Get(BuildEpoch epoch)
    {
        if (CachedScripts.Value.TryGetValue(epoch, out BuilderScriptSpec? spec))
        {
            return spec;
        }

        throw new InvalidOperationException($"No builder script is registered for epoch: {epoch}.");
    }

    private static IReadOnlyDictionary<BuildEpoch, BuilderScriptSpec> LoadScripts()
    {
        string definitionsDirectory = ResolveDefinitionsDirectory();
        string scriptsRoot = Directory.GetParent(definitionsDirectory)?.Parent?.FullName
            ?? throw new InvalidOperationException("Unable to resolve scripts root directory.");

        var result = new Dictionary<BuildEpoch, BuilderScriptSpec>();
        foreach (string definitionPath in Directory.GetFiles(definitionsDirectory, "*.builder.json"))
        {
            BuilderScriptDefinition definition = LoadDefinition(definitionPath);
            string sourcePath = ResolveScriptPath(scriptsRoot, definition.sourceFile);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    $"Builder script source file declared in '{definitionPath}' was not found.",
                    sourcePath);
            }

            string sourceCode = File.ReadAllText(sourcePath);
            var spec = new BuilderScriptSpec(
                sourceCode,
                definition.entryMethod.Trim(),
                string.IsNullOrWhiteSpace(definition.outputFileName)
                    ? "FontBundleBuilder.cs"
                    : definition.outputFileName.Trim());

            foreach (BuildEpoch epoch in ResolveEpochs(definition.epochs))
            {
                if (result.TryGetValue(epoch, out BuilderScriptSpec? existing) &&
                    !ReferenceEquals(existing, spec))
                {
                    throw new InvalidOperationException(
                        $"More than one builder script is registered for epoch '{epoch}'.");
                }

                result[epoch] = spec;
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidOperationException(
                $"No builder script definitions were found in '{definitionsDirectory}'.");
        }

        return result;
    }

    private static BuilderScriptDefinition LoadDefinition(string definitionPath)
    {
        string json = File.ReadAllText(definitionPath);
        BuilderScriptDefinition? definition = JsonSerializer.Deserialize<BuilderScriptDefinition>(json, JsonOptions);
        if (definition is null)
        {
            throw new InvalidOperationException($"Cannot parse builder script definition: {definitionPath}");
        }

        if (string.IsNullOrWhiteSpace(definition.id))
        {
            throw new InvalidOperationException($"Builder script definition id is missing: {definitionPath}");
        }

        if (string.IsNullOrWhiteSpace(definition.sourceFile))
        {
            throw new InvalidOperationException($"sourceFile is missing in: {definitionPath}");
        }

        if (string.IsNullOrWhiteSpace(definition.entryMethod))
        {
            throw new InvalidOperationException($"entryMethod is missing in: {definitionPath}");
        }

        if (definition.epochs is null || definition.epochs.Length == 0)
        {
            throw new InvalidOperationException($"epochs list is empty in: {definitionPath}");
        }

        return definition;
    }

    private static string ResolveDefinitionsDirectory()
    {
        string direct = Path.Combine(AppContext.BaseDirectory, "BuilderScripts", "Definitions");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && !string.IsNullOrWhiteSpace(current); i++)
        {
            current = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(current))
            {
                break;
            }

            string candidate = Path.Combine(current, "BuilderScripts", "Definitions");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            "Builder script definitions directory was not found. " +
            "Expected 'BuilderScripts/Definitions' near the executable.");
    }

    private static string ResolveScriptPath(string scriptsRoot, string sourceFile)
    {
        if (Path.IsPathRooted(sourceFile))
        {
            return sourceFile;
        }

        return Path.GetFullPath(Path.Combine(scriptsRoot, sourceFile));
    }

    private static IReadOnlyList<BuildEpoch> ResolveEpochs(string[] epochs)
    {
        var result = new List<BuildEpoch>(epochs.Length);
        foreach (string raw in epochs)
        {
            string token = raw.Trim().ToLowerInvariant();
            switch (token)
            {
                case "legacy":
                case "legacy-2018-2020":
                    result.Add(BuildEpoch.Legacy2018To2020);
                    break;
                case "mid":
                case "mid-2021-2022":
                    result.Add(BuildEpoch.Mid2021To2022);
                    break;
                case "modern":
                case "modern-2023-plus":
                    result.Add(BuildEpoch.Modern2023Plus);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown epoch token in builder script definition: {raw}");
            }
        }

        return result.Distinct().ToArray();
    }
}

internal sealed record BuilderScriptSpec(string SourceCode, string EntryMethod, string OutputFileName);

internal sealed class BuilderScriptDefinition
{
    public required string id { get; init; }

    public required string sourceFile { get; init; }

    public required string entryMethod { get; init; }

    public string outputFileName { get; init; } = "FontBundleBuilder.cs";

    public required string[] epochs { get; init; }
}
