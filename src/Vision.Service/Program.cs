namespace Vision.Service;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ServiceOptions.Parse(args);
            await using var backend = CreateBackend(options);
            await using var server = new VisionTcpServer(options, backend);
            using var shutdownSource = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdownSource.Cancel();
                server.RequestStop();
            };

            Console.WriteLine($"Vision service listening on {options.ListenAddress}:{options.Port} using {backend.Name} backend.");
            await server.RunAsync(shutdownSource.Token).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Vision service failed: {exception.Message}");
            return 1;
        }
    }

    private static IVisionBackend CreateBackend(ServiceOptions options) => options.Backend.ToLowerInvariant() switch
    {
        "mock" => new MockVisionBackend(TimeSpan.FromMilliseconds(options.MockDelayMs)),
        "hdevengine" when options.HalconAssemblyPath is not null && options.ProcedurePath is not null =>
            new HDevEngineBackend(options.HalconAssemblyPath, options.ProcedurePath, options.ProcedureName),
        "hdevengine" => throw new ArgumentException(
            "--backend hdevengine requires --halcon-assembly and --procedure-path."),
        _ => throw new ArgumentException($"Unsupported backend '{options.Backend}'.")
    };
}
