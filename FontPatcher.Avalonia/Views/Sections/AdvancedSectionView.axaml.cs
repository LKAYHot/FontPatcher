using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;

namespace FontPatcher.Avalonia.Views.Sections;

public partial class AdvancedSectionView : UserControl
{
    public AdvancedSectionView()
    {
        InitializeComponent();
    }

    private void OnNumericTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (e.Text.Any(ch => !char.IsDigit(ch)))
        {
            e.Handled = true;
        }
    }

    private void OnNumericTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        string current = textBox.Text ?? string.Empty;
        string filtered = new(current.Where(char.IsDigit).ToArray());
        if (current == filtered)
        {
            return;
        }

        int caret = textBox.CaretIndex;
        int filteredCaret = 0;
        int max = Math.Min(caret, current.Length);
        for (int i = 0; i < max; i++)
        {
            if (char.IsDigit(current[i]))
            {
                filteredCaret++;
            }
        }

        textBox.Text = filtered;
        textBox.CaretIndex = Math.Min(filteredCaret, filtered.Length);
    }
}
