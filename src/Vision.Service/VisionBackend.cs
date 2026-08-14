using System.Reflection;
using System.Text.Json;

namespace Vision.Service;

public sealed record VisionMeasurement(
    bool IsOk,
    double X,
    double Y,
    double Theta,
    double Score,
    string? ImagePath = null,
    IReadOnlyDictionary<string, object?>? Details = null);

public interface IVisionBackend : IAsyncDisposable
{
    string Name { get; }
    bool IsReady { get; }
    Task<VisionMeasurement> TriggerAsync(JsonElement? parameters, CancellationToken cancellationToken);
}

public sealed class MockVisionBackend(TimeSpan? processingTime = null) : IVisionBackend
{
    private readonly TimeSpan _processingTime = processingTime ?? TimeSpan.FromMilliseconds(120);

    public string Name => "mock";
    public bool IsReady => true;

    public async Task<VisionMeasurement> TriggerAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        var delay = ReadDouble(parameters, "delayMs") is { } requested
            ? TimeSpan.FromMilliseconds(Math.Clamp(requested, 0, 60_000))
            : _processingTime;
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        var x = ReadDouble(parameters, "mockX") ?? 0.012;
        var y = ReadDouble(parameters, "mockY") ?? -0.008;
        var theta = ReadDouble(parameters, "mockTheta") ?? 0.015;
        var score = ReadDouble(parameters, "mockScore") ?? 0.998;
        var tolerance = ReadDouble(parameters, "toleranceMm") ?? 0.05;
        return new VisionMeasurement(
            Math.Abs(x) <= tolerance && Math.Abs(y) <= tolerance,
            x,
            y,
            theta,
            score,
            Details: new Dictionary<string, object?>
            {
                ["mode"] = "simulation",
                ["toleranceMm"] = tolerance
            });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static double? ReadDouble(JsonElement? parameters, string propertyName)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty(propertyName, out var property) ||
            !property.TryGetDouble(out var result))
        {
            return null;
        }

        return result;
    }
}

public sealed class HDevEngineBackend : IVisionBackend
{
    private readonly object _engine;
    private readonly object _procedure;
    private readonly Type _procedureType;
    private readonly Type _procedureCallType;
    private readonly Type _tupleType;
    private readonly string[] _outputNames;

    public HDevEngineBackend(string assemblyPath, string procedurePath, string procedureName, string[]? outputNames = null)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException("HALCON .NET assembly not found.", assemblyPath);
        }

        var fullAssemblyPath = Path.GetFullPath(assemblyPath);
        var assemblyDirectory = Path.GetDirectoryName(fullAssemblyPath)
            ?? throw new InvalidOperationException("HALCON assembly directory is unavailable.");
        var halconAssemblyPath = Path.Combine(assemblyDirectory, "halcondotnet.dll");
        var halconAssembly = File.Exists(halconAssemblyPath)
            ? Assembly.LoadFrom(halconAssemblyPath)
            : throw new FileNotFoundException("halcondotnet.dll must be beside hdevenginedotnet.dll.", halconAssemblyPath);
        var assembly = Assembly.LoadFrom(fullAssemblyPath);
        var engineType = RequireType(assembly, "HalconDotNet.HDevEngine");
        _procedureType = RequireType(assembly, "HalconDotNet.HDevProcedure");
        _procedureCallType = RequireType(assembly, "HalconDotNet.HDevProcedureCall");
        _tupleType = RequireType(halconAssembly, "HalconDotNet.HTuple");
        _engine = Activator.CreateInstance(engineType)
            ?? throw new InvalidOperationException("Could not create HDevEngine.");
        engineType.GetMethod("SetProcedurePath", [typeof(string)])?.Invoke(_engine, [Path.GetFullPath(procedurePath)]);
        _procedure = Activator.CreateInstance(_procedureType, procedureName)
            ?? throw new InvalidOperationException($"Could not load HDevelop procedure '{procedureName}'.");
        _outputNames = outputNames ?? ["is_ok", "x", "y", "theta", "score"];
    }

    public string Name => "hdevengine";
    public bool IsReady => true;

    public Task<VisionMeasurement> TriggerAsync(JsonElement? parameters, CancellationToken cancellationToken)
    {
        return Task.Run(() => Execute(parameters, cancellationToken), cancellationToken);
    }

    private VisionMeasurement Execute(JsonElement? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var call = Activator.CreateInstance(_procedureCallType, _procedure)
            ?? throw new InvalidOperationException("Could not create HDevProcedureCall.");
        try
        {
            SetInputs(call, parameters);
            call.GetType().GetMethod("Execute", Type.EmptyTypes)?.Invoke(call, null);
            cancellationToken.ThrowIfCancellationRequested();

            var values = _outputNames.ToDictionary(name => name, name => GetOutputValue(call, name));
            return new VisionMeasurement(
                Convert.ToDouble(values[_outputNames[0]]) != 0,
                Convert.ToDouble(values[_outputNames[1]]),
                Convert.ToDouble(values[_outputNames[2]]),
                Convert.ToDouble(values[_outputNames[3]]),
                Convert.ToDouble(values[_outputNames[4]]),
                Details: values);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw new InvalidOperationException("HALCON procedure execution failed.", exception.InnerException);
        }
        finally
        {
            (call as IDisposable)?.Dispose();
        }
    }

    private void SetInputs(object call, JsonElement? parameters)
    {
        if (parameters is not { ValueKind: JsonValueKind.Object } input)
        {
            return;
        }

        var method = call.GetType().GetMethods()
            .FirstOrDefault(candidate => candidate.Name == "SetInputCtrlParamTuple" && candidate.GetParameters().Length == 2);
        if (method is null)
        {
            return;
        }

        foreach (var property in input.EnumerateObject())
        {
            object value = property.Value.ValueKind switch
            {
                JsonValueKind.Number when property.Value.TryGetInt64(out var integer) => integer,
                JsonValueKind.Number => property.Value.GetDouble(),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                _ => property.Value.GetRawText()
            };
            var tuple = Activator.CreateInstance(_tupleType, value)
                ?? throw new InvalidOperationException($"Could not convert input '{property.Name}' to HTuple.");
            try
            {
                method.Invoke(call, [property.Name, tuple]);
            }
            finally
            {
                (tuple as IDisposable)?.Dispose();
            }
        }
    }

    private static object? GetOutputValue(object call, string name)
    {
        var method = call.GetType().GetMethod("GetOutputCtrlParamTuple", [typeof(string)])
            ?? throw new MissingMethodException(call.GetType().FullName, "GetOutputCtrlParamTuple");
        var tuple = method.Invoke(call, [name])
            ?? throw new InvalidOperationException($"HALCON output '{name}' is null.");
        var tupleType = tuple.GetType();
        return tupleType.GetProperty("D")?.GetValue(tuple)
            ?? tupleType.GetProperty("I")?.GetValue(tuple)
            ?? tupleType.GetProperty("S")?.GetValue(tuple)
            ?? tuple.ToString();
    }

    private static Type RequireType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: false)
        ?? throw new TypeLoadException($"'{name}' was not found in {assembly.FullName}.");

    public ValueTask DisposeAsync()
    {
        (_procedure as IDisposable)?.Dispose();
        (_engine as IDisposable)?.Dispose();
        return ValueTask.CompletedTask;
    }
}
