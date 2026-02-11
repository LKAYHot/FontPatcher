namespace FontPatcher.Avalonia.Services;

public sealed record BuildLaunchRequest(IReadOnlyList<string> Arguments, string WorkingDirectory);

public sealed record BuildExecutionResult(int ExitCode);

public interface IBuildRunner
{
    Task<BuildExecutionResult> RunAsync(
        BuildLaunchRequest request,
        Action<string, bool> onLine,
        CancellationToken cancellationToken);
}
