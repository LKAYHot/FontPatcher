using Avalonia.Media;

namespace FontPatcher.Avalonia.ViewModels;

public enum LogLevel
{
    Info,
    Warn,
    Error,
    Success
}

public sealed class LogLineViewModel
{
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#64748B"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F87171"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#22C55E"));

    public LogLineViewModel(DateTime timestamp, LogLevel level, string message)
    {
        Timestamp = timestamp;
        Level = level;
        Message = message;
    }

    public DateTime Timestamp { get; }

    public LogLevel Level { get; }

    public string Message { get; }

    public string TimestampText => $"[{Timestamp:HH:mm:ss}]";

    public string LevelText => Level.ToString().ToUpperInvariant();

    public IBrush LevelBrush => Level switch
    {
        LogLevel.Info => InfoBrush,
        LogLevel.Warn => WarnBrush,
        LogLevel.Error => ErrorBrush,
        LogLevel.Success => SuccessBrush,
        _ => InfoBrush
    };
}