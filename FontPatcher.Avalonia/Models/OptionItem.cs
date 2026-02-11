namespace FontPatcher.Avalonia.Models;

public sealed class OptionItem
{
    public OptionItem(string value, string label)
    {
        Value = value;
        Label = label;
    }

    public string Value { get; }

    public string Label { get; }

    public override string ToString() => Label;
}
