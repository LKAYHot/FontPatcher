using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace FontPatcher.Avalonia.Controls;

public partial class FileDropZone : UserControl
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(Value), string.Empty, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> PlaceholderProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(Placeholder), "Drop file here");

    public static readonly StyledProperty<string> EmptyHintProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(EmptyHint), "Click to browse or drop file here");

    public static readonly StyledProperty<string> FilledHintProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(FilledHint), "Click or drag to replace");

    public static readonly StyledProperty<string> AcceptProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(Accept), string.Empty);

    public static readonly StyledProperty<bool> IsDirectoryProperty =
        AvaloniaProperty.Register<FileDropZone, bool>(nameof(IsDirectory));

    public static readonly StyledProperty<double> ZoneHeightProperty =
        AvaloniaProperty.Register<FileDropZone, double>(nameof(ZoneHeight), 160d);

    public static readonly StyledProperty<string> IconPathDataProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(IconPathData), "M14.5 2H6A2 2 0 0 0 4 4V20A2 2 0 0 0 6 22H18A2 2 0 0 0 20 20V7.5L14.5 2 M14 2V8H20");

    public static readonly StyledProperty<string> PickerTitleProperty =
        AvaloniaProperty.Register<FileDropZone, string>(nameof(PickerTitle), "Select file");

    public static readonly DirectProperty<FileDropZone, bool> HasValueProperty =
        AvaloniaProperty.RegisterDirect<FileDropZone, bool>(nameof(HasValue), o => o.HasValue);

    public static readonly DirectProperty<FileDropZone, string> DisplayValueProperty =
        AvaloniaProperty.RegisterDirect<FileDropZone, string>(nameof(DisplayValue), o => o.DisplayValue);

    public static readonly DirectProperty<FileDropZone, string> HintTextProperty =
        AvaloniaProperty.RegisterDirect<FileDropZone, string>(nameof(HintText), o => o.HintText);

    public static readonly DirectProperty<FileDropZone, bool> IsDraggingProperty =
        AvaloniaProperty.RegisterDirect<FileDropZone, bool>(nameof(IsDragging), o => o.IsDragging);

    private bool _hasValue;
    private string _displayValue = string.Empty;
    private string _hintText = string.Empty;
    private bool _isDragging;

    public FileDropZone()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(Surface, true);

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
        PointerPressed += OnPointerPressed;

        UpdateValueState(Value);
        UpdateDraggingState(false);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Placeholder
    {
        get => GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string EmptyHint
    {
        get => GetValue(EmptyHintProperty);
        set => SetValue(EmptyHintProperty, value);
    }

    public string FilledHint
    {
        get => GetValue(FilledHintProperty);
        set => SetValue(FilledHintProperty, value);
    }

    public string Accept
    {
        get => GetValue(AcceptProperty);
        set => SetValue(AcceptProperty, value);
    }

    public bool IsDirectory
    {
        get => GetValue(IsDirectoryProperty);
        set => SetValue(IsDirectoryProperty, value);
    }

    public double ZoneHeight
    {
        get => GetValue(ZoneHeightProperty);
        set => SetValue(ZoneHeightProperty, value);
    }

    public string IconPathData
    {
        get => GetValue(IconPathDataProperty);
        set => SetValue(IconPathDataProperty, value);
    }

    public string PickerTitle
    {
        get => GetValue(PickerTitleProperty);
        set => SetValue(PickerTitleProperty, value);
    }

    public bool HasValue
    {
        get => _hasValue;
        private set => SetAndRaise(HasValueProperty, ref _hasValue, value);
    }

    public string DisplayValue
    {
        get => _displayValue;
        private set => SetAndRaise(DisplayValueProperty, ref _displayValue, value);
    }

    public string HintText
    {
        get => _hintText;
        private set => SetAndRaise(HintTextProperty, ref _hintText, value);
    }

    public bool IsDragging
    {
        get => _isDragging;
        private set => SetAndRaise(IsDraggingProperty, ref _isDragging, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ValueProperty)
        {
            UpdateValueState(change.GetNewValue<string>() ?? string.Empty);
        }
        else if (change.Property == PlaceholderProperty ||
                 change.Property == EmptyHintProperty ||
                 change.Property == FilledHintProperty)
        {
            UpdateValueState(Value);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!CanAccept(e))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        UpdateDraggingState(true);
    }

    private void OnDragLeave(object? sender, RoutedEventArgs e)
    {
        UpdateDraggingState(false);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            UpdateDraggingState(false);
            if (!CanAccept(e))
            {
                return;
            }

            IEnumerable<IStorageItem>? items = e.Data.GetFiles();
            IStorageItem? firstItem = items?.FirstOrDefault();
            if (firstItem is null)
            {
                return;
            }

            string? path = ResolveDropPath(firstItem);
            if (!string.IsNullOrWhiteSpace(path))
            {
                Value = path;
            }
        }
        catch
        {
            // Ignore invalid drops.
        }
    }

    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        if (IsDirectory)
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = PickerTitle,
                AllowMultiple = false
            });

            IStorageFolder? folder = folders.FirstOrDefault();
            string? path = folder?.Path.LocalPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                Value = path;
            }

            return;
        }

        var openFileOptions = new FilePickerOpenOptions
        {
            Title = PickerTitle,
            AllowMultiple = false,
            FileTypeFilter = BuildFilters(Accept)
        };

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(openFileOptions);
        IStorageFile? file = files.FirstOrDefault();
        string? filePath = file?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            Value = filePath;
        }
    }

    private bool CanAccept(DragEventArgs e)
    {
        return e.Data.Contains(DataFormats.Files);
    }

    private string? ResolveDropPath(IStorageItem item)
    {
        string? localPath = item.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return null;
        }

        if (IsDirectory)
        {
            if (item is IStorageFolder)
            {
                return localPath;
            }

            return Path.GetDirectoryName(localPath);
        }

        if (item is IStorageFile)
        {
            return PassesExtensionFilter(localPath) ? localPath : null;
        }

        return null;
    }

    private bool PassesExtensionFilter(string path)
    {
        string[] extensions = ParseExtensions(Accept);
        if (extensions.Length == 0)
        {
            return true;
        }

        string extension = Path.GetExtension(path);
        return extensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<FilePickerFileType> BuildFilters(string accept)
    {
        string[] extensions = ParseExtensions(accept);
        if (extensions.Length == 0)
        {
            return [];
        }

        var patterns = extensions
            .Select(ext => "*" + ext.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return
        [
            new FilePickerFileType("Supported files")
            {
                Patterns = patterns
            }
        ];
    }

    private static string[] ParseExtensions(string accept)
    {
        if (string.IsNullOrWhiteSpace(accept))
        {
            return [];
        }

        return accept
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith(".", StringComparison.Ordinal) ? x : "." + x)
            .Where(x => x.Length > 1)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void UpdateValueState(string value)
    {
        bool hasValue = !string.IsNullOrWhiteSpace(value);
        HasValue = hasValue;

        string cleanValue = value?.Trim() ?? string.Empty;
        if (!hasValue)
        {
            DisplayValue = Placeholder;
            HintText = EmptyHint;
        }
        else
        {
            string trimmed = cleanValue.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fileName = Path.GetFileName(trimmed);
            DisplayValue = string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
            HintText = FilledHint;
        }

        SetClassFlag("filled", hasValue);
        SetClassFlag("empty", !hasValue);
    }

    private void UpdateDraggingState(bool dragging)
    {
        IsDragging = dragging;
        SetClassFlag("dragging", dragging);
    }

    private void SetClassFlag(string className, bool enabled)
    {
        if (enabled)
        {
            if (!Classes.Contains(className))
            {
                Classes.Add(className);
            }
        }
        else
        {
            Classes.Remove(className);
        }
    }
}
