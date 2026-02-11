using System.Collections.Specialized;
using System.Collections;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FontPatcher.Avalonia.ViewModels;

namespace FontPatcher.Avalonia.Views;

public partial class MainWindow : Window
{
    private const double ResizeBorderGuardThickness = 8;

    private INotifyCollectionChanged? _logCollection;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_logCollection is not null)
        {
            _logCollection.CollectionChanged -= OnLogsCollectionChanged;
            _logCollection = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            _logCollection = vm.Logs;
            _logCollection.CollectionChanged += OnLogsCollectionChanged;
        }
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add ||
            DataContext is not MainWindowViewModel vm ||
            vm.Logs.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            object last = vm.Logs[^1];
            ScrollListToLast(LogList, last);
            ScrollListToLast(FullLogList, last);
        }, DispatcherPriority.Background);
    }

    private static void ScrollListToLast(ListBox listBox, object lastItem)
    {
        listBox.ScrollIntoView(lastItem);
    }

    private async void OnCopySelectedLogsClick(object? sender, RoutedEventArgs e)
    {
        await CopySelectedLogsAsync(ResolveSourceListBox(sender));
    }

    private async void OnCopyAllLogsClick(object? sender, RoutedEventArgs e)
    {
        await CopyAllLogsAsync();
    }

    private async void OnLogListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            await CopySelectedLogsAsync(ResolveSourceListBox(sender));
            e.Handled = true;
        }
    }

    private async Task CopySelectedLogsAsync(ListBox sourceList)
    {
        IList? selectedItems = sourceList.SelectedItems;
        IEnumerable<object> selectedObjects = selectedItems is null
            ? Array.Empty<object>()
            : selectedItems.Cast<object>();

        var selected = selectedObjects
            .OfType<LogLineViewModel>()
            .ToArray();

        if (selected.Length == 0 && sourceList.SelectedItem is LogLineViewModel single)
        {
            selected = [single];
        }

        if (selected.Length == 0)
        {
            return;
        }

        string payload = string.Join(Environment.NewLine, selected.Select(FormatLogLine));
        await CopyToClipboardAsync(payload);
    }

    private async Task CopyAllLogsAsync()
    {
        if (DataContext is not MainWindowViewModel vm || vm.Logs.Count == 0)
        {
            return;
        }

        string payload = string.Join(Environment.NewLine, vm.Logs.Select(FormatLogLine));
        await CopyToClipboardAsync(payload);
    }

    private async Task CopyToClipboardAsync(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(payload);
    }

    private static string FormatLogLine(LogLineViewModel line)
    {
        return $"[{line.TimestampText}] {line.LevelText,-8} {line.Message}";
    }

    private ListBox ResolveSourceListBox(object? sender)
    {
        if (sender is ListBox listBox)
        {
            return listBox;
        }

        if (sender is MenuItem menuItem &&
            menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is ListBox placementListBox)
        {
            return placementListBox;
        }

        if (DataContext is MainWindowViewModel vm && vm.IsLogFullscreenVisible)
        {
            return FullLogList;
        }

        return LogList;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.Source is Control source &&
            source.GetSelfAndVisualAncestors().OfType<Button>().Any())
        {
            return;
        }

        if (IsNearResizeBorder(e))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleWindowMaximizeRestore();
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
        e.Handled = true;
    }

    private bool IsNearResizeBorder(PointerPressedEventArgs e)
    {
        if (WindowState != WindowState.Normal)
        {
            return false;
        }

        var point = e.GetPosition(this);
        double width = Bounds.Width;
        double height = Bounds.Height;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        return point.X <= ResizeBorderGuardThickness
            || point.X >= width - ResizeBorderGuardThickness
            || point.Y <= ResizeBorderGuardThickness
            || point.Y >= height - ResizeBorderGuardThickness;
    }

    private void OnMinimizeWindowClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreWindowClick(object? sender, RoutedEventArgs e)
    {
        ToggleWindowMaximizeRestore();
    }

    private void OnCloseWindowClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

}
