using System.Diagnostics;
using System.IO;

namespace FontPatcher.Avalonia.Services;

public sealed class CliBuildRunner : IBuildRunner
{
    private const string CliAssemblyName = "FontPatcher.Cli.dll";

    public async Task<BuildExecutionResult> RunAsync(
        BuildLaunchRequest request,
        Action<string, bool> onLine,
        CancellationToken cancellationToken)
    {
        string? cliAssemblyPath = ResolveCliAssemblyPath(request.WorkingDirectory);
        if (cliAssemblyPath is not null)
        {
            var assemblyArgs = new List<string>(request.Arguments.Count + 1) { cliAssemblyPath };
            assemblyArgs.AddRange(request.Arguments);
            return await ExecuteAsync("dotnet", assemblyArgs, request.WorkingDirectory, onLine, cancellationToken)
                .ConfigureAwait(false);
        }

        string cliProjectPath = Path.Combine(request.WorkingDirectory, "FontPatcher.Cli", "FontPatcher.Cli.csproj");
        if (!File.Exists(cliProjectPath))
        {
            throw new FileNotFoundException(
                "Unable to locate FontPatcher.Cli entry point. Build FontPatcher.Cli first or keep the project in the repository root.",
                cliProjectPath);
        }

        var projectArgs = new List<string>(request.Arguments.Count + 4)
        {
            "run",
            "--project",
            cliProjectPath,
            "--"
        };
        projectArgs.AddRange(request.Arguments);

        return await ExecuteAsync("dotnet", projectArgs, request.WorkingDirectory, onLine, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<BuildExecutionResult> ExecuteAsync(
        string command,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        Action<string, bool> onLine,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {command}");
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() => TryKill(process));

        Task outputPump = PumpLinesAsync(process.StandardOutput, isStdErr: false, onLine, cancellationToken);
        Task errorPump = PumpLinesAsync(process.StandardError, isStdErr: true, onLine, cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputPump, errorPump).ConfigureAwait(false);
            return new BuildExecutionResult(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
    }

    private static async Task PumpLinesAsync(
        StreamReader reader,
        bool isStdErr,
        Action<string, bool> onLine,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            onLine(line, isStdErr);
        }
    }

    private static string? ResolveCliAssemblyPath(string repositoryRoot)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, CliAssemblyName),
            Path.Combine(repositoryRoot, CliAssemblyName),
            Path.Combine(repositoryRoot, "FontPatcher.Cli", "bin", "Debug", "net8.0", CliAssemblyName),
            Path.Combine(repositoryRoot, "FontPatcher.Cli", "bin", "Release", "net8.0", CliAssemblyName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryKill(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Ignore cleanup errors on cancellation path.
        }
    }
}
