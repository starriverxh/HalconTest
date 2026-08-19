namespace VisionSystem.Runtime;

public sealed record RuntimeOptions
{
    public string BindAddress { get; init; } = "127.0.0.1";
    public int ProductionPort { get; init; } = 9200;
    public int EngineeringPort { get; init; } = 9201;
    public int QueueCapacity { get; init; } = 1;
    public int IdempotencyCapacity { get; init; } = 1000;
    public bool ProductionCommissioned { get; init; }
    public string RecipesDirectory { get; init; } = "config/recipes";
    public string RuntimeDirectory { get; init; } = "runtime";
    public string DefaultRecipeId { get; init; } = "pcb-pin-row-offline-v2";

    public static RuntimeOptions Parse(string[] args, string baseDirectory)
    {
        string? GetValue(string name)
        {
            var index = Array.FindIndex(args, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var productionPort = ParsePort(GetValue("--production-port"), 9200, "production");
        var engineeringPort = ParsePort(GetValue("--engineering-port"), 9201, "engineering");
        if (productionPort == engineeringPort)
        {
            throw new ArgumentException("Production and engineering ports must be different.");
        }

        var recipeInput = GetValue("--recipes") ?? Path.Combine(baseDirectory, "config", "recipes");
        var runtimeInput = GetValue("--runtime") ?? Path.Combine(baseDirectory, "runtime");
        return new RuntimeOptions
        {
            BindAddress = GetValue("--bind") ?? "127.0.0.1",
            ProductionPort = productionPort,
            EngineeringPort = engineeringPort,
            QueueCapacity = ParsePositive(GetValue("--queue-capacity"), 1, "queue capacity"),
            IdempotencyCapacity = ParsePositive(GetValue("--idempotency-capacity"), 1000, "idempotency capacity"),
            ProductionCommissioned = args.Contains("--production-commissioned", StringComparer.OrdinalIgnoreCase),
            RecipesDirectory = Path.GetFullPath(recipeInput),
            RuntimeDirectory = Path.GetFullPath(runtimeInput),
            DefaultRecipeId = GetValue("--recipe") ?? "pcb-pin-row-offline-v2"
        };
    }

    private static int ParsePort(string? value, int fallback, string name)
    {
        var parsed = ParsePositive(value, fallback, $"{name} port");
        return parsed <= 65535 ? parsed : throw new ArgumentOutOfRangeException(name, "Port must be <= 65535.");
    }

    private static int ParsePositive(string? value, int fallback, string name) =>
        value is null
            ? fallback
            : int.TryParse(value, out var parsed) && parsed > 0
                ? parsed
                : throw new ArgumentException($"Invalid {name}: '{value}'.");
}
