using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace FontPatcher.Cli;

internal sealed class ProcessRunner
{
    private const int StartRetryCount = 10;
    private static readonly TimeSpan StartRetryDelay = TimeSpan.FromSeconds(2);

    public async Task<ProcessResult> RunAsync(
        string executablePath,
        string arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                stderr.AppendLine(e.Data);
            }
        };

        if (!TryStartWithRetries(process, cancellationToken))
        {
            throw new InvalidOperationException($"Failed to start process: {executablePath}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static bool TryStartWithRetries(Process process, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= StartRetryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (process.Start())
                {
                    return true;
                }
            }
            catch (Exception ex) when (IsTransientFileLockError(ex) && attempt < StartRetryCount)
            {
                Thread.Sleep(StartRetryDelay);
                continue;
            }
        }

        return false;
    }

    private static bool IsTransientFileLockError(Exception ex)
    {
        if (ex is Win32Exception win32)
        {
            if (win32.NativeErrorCode is 32 or 33)
            {
                return true;
            }
        }

        if (ex is IOException ioEx)
        {
            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            if (ioEx.HResult is sharingViolation or lockViolation)
            {
                return true;
            }
        }

        string msg = ex.Message.ToLowerInvariant();
        return msg.Contains("used by another process") ||
               msg.Contains("cannot access the file");
    }
}

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
