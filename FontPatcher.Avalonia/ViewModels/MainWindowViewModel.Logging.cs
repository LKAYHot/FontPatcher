using System.Collections.Specialized;
using Avalonia.Threading;
using FontPatcher.Avalonia.Services;

namespace FontPatcher.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    private void OnRunnerLine(string line, bool isStdErr)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            LogLevel level = BuildOutputInterpreter.ParseLogLevel(line, isStdErr);
            AddLog(line, level);
            BuildProgress = BuildOutputInterpreter.ResolveProgress(line, BuildProgress);
        });
    }

    private void AddLog(string message, LogLevel level)
    {
        Logs.Add(new LogLineViewModel(DateTime.Now, level, message));
        while (Logs.Count > MaxLogLines)
        {
            Logs.RemoveAt(0);
        }
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasLogs));
        OnPropertyChanged(nameof(IsLogEmpty));
    }
}
