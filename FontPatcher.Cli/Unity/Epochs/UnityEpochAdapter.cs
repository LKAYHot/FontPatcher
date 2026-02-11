namespace FontPatcher.Cli;

internal interface IUnityEpochAdapter
{
    BuildEpoch Epoch { get; }

    string Name { get; }

    bool DefaultUseNoGraphics { get; }

    BuilderScriptSpec GetBuilderScript();
}

internal sealed class LegacyEpochAdapter : IUnityEpochAdapter
{
    public BuildEpoch Epoch => BuildEpoch.Legacy2018To2020;

    public string Name => "legacy-2018-2020";

    public bool DefaultUseNoGraphics => false;

    public BuilderScriptSpec GetBuilderScript() => BuilderScriptRegistry.Get(Epoch);
}

internal sealed class MidEpochAdapter : IUnityEpochAdapter
{
    public BuildEpoch Epoch => BuildEpoch.Mid2021To2022;

    public string Name => "mid-2021-2022";

    public bool DefaultUseNoGraphics => false;

    public BuilderScriptSpec GetBuilderScript() => BuilderScriptRegistry.Get(Epoch);
}

internal sealed class ModernEpochAdapter : IUnityEpochAdapter
{
    public BuildEpoch Epoch => BuildEpoch.Modern2023Plus;

    public string Name => "modern-2023-plus";

    public bool DefaultUseNoGraphics => false;

    public BuilderScriptSpec GetBuilderScript() => BuilderScriptRegistry.Get(Epoch);
}

internal sealed class UnityEpochAdapterRegistry
{
    private readonly Dictionary<BuildEpoch, IUnityEpochAdapter> _adapters;

    public UnityEpochAdapterRegistry(IEnumerable<IUnityEpochAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(x => x.Epoch, x => x);
    }

    public IUnityEpochAdapter Get(BuildEpoch epoch)
    {
        if (_adapters.TryGetValue(epoch, out IUnityEpochAdapter? adapter))
        {
            return adapter;
        }

        throw new InvalidOperationException($"No epoch adapter registered for {epoch}.");
    }

    public static UnityEpochAdapterRegistry CreateDefault()
    {
        return new UnityEpochAdapterRegistry(
            [
                new LegacyEpochAdapter(),
                new MidEpochAdapter(),
                new ModernEpochAdapter()
            ]);
    }
}
