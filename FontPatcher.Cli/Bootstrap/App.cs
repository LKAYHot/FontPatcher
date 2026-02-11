using System.Text;

namespace FontPatcher.Cli;

internal static class App
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (!CliParser.TryParse(args, out CliOptions? options, out string? error, out bool showHelp))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(CliParser.HelpText);
            return 2;
        }

        if (showHelp || options is null)
        {
            Console.WriteLine(CliParser.HelpText);
            return 0;
        }

        Func<ConversionPipeline> pipelineFactory = () =>
        {
            var processRunner = new ProcessRunner();
            var targetVersionDetector = new UnityTargetVersionDetector();
            return new ConversionPipeline(
                new UnityAutoProvisioner(
                    new UnityEditorLocator(),
                    new UnityHubLocator(),
                    targetVersionDetector,
                    processRunner),
                processRunner,
                new UnityEpochResolver(targetVersionDetector),
                UnityEpochAdapterRegistry.CreateDefault());
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(options.JobsFilePath))
            {
                var orchestrator = new BatchOrchestrator(pipelineFactory);
                BatchRunResult batch = await orchestrator.RunAsync(options, CancellationToken.None);

                foreach (BatchJobResult job in batch.Jobs)
                {
                    Console.WriteLine($"{job.JobName}: {job.Message}");
                }

                Console.WriteLine(
                    $"Batch completed. Success={batch.SuccessCount}, Failed={batch.FailureCount}, Workers={options.MaxWorkers}");
                return batch.AllSucceeded ? 0 : 1;
            }

            ConversionPipeline pipeline = pipelineFactory();
            PipelineResult result = await pipeline.RunAsync(options, CancellationToken.None);

            var summary = new StringBuilder();
            summary.AppendLine("Conversion completed.");
            summary.AppendLine($"Unity editor: {result.UnityEditorPath}");
            summary.AppendLine($"Epoch adapter: {result.AdapterName} ({result.Epoch})");
            summary.AppendLine($"Unity args mode: {(result.UseNoGraphics ? "batchmode+nographics" : "batchmode")}");
            summary.AppendLine($"Bundle: {result.BundleFilePath}");
            summary.AppendLine($"Manifest: {result.BundleManifestPath}");
            summary.AppendLine($"TMP asset name: {result.TmpAssetName}");

            if (!string.IsNullOrWhiteSpace(result.WorkerProjectPath))
            {
                summary.AppendLine($"Temp Unity project: {result.WorkerProjectPath}");
            }

            Console.WriteLine(summary.ToString().TrimEnd());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Conversion failed.");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
