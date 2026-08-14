using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Vision.Service;

namespace Vision.Service.Tests;

public static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), $"vision-service-tests-{Guid.NewGuid():N}");
        var options = new ServiceOptions
        {
            ListenAddress = IPAddress.Loopback,
            Port = 0,
            DefaultTimeoutMs = 1_000,
            MaximumTimeoutMs = 2_000,
            RuntimeDirectory = runtimeDirectory
        };

        await using var server = new VisionTcpServer(options, new MockVisionBackend(TimeSpan.FromMilliseconds(150)));
        var serverTask = server.RunAsync();
        var readyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (server.BoundPort == 0 && DateTime.UtcNow < readyDeadline)
        {
            await Task.Delay(10);
        }

        var port = server.BoundPort;
        Check(port > 0, "Server did not bind a TCP port.");
        try
        {
            await TestAsync("ping", async () =>
            {
                using var response = await SendAsync(port, new { requestId = "ping-1", command = "ping" });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                Equal("OK", response.RootElement.GetProperty("code").GetString());
            });

            await TestAsync("invalid request", async () =>
            {
                using var response = await SendRawAsync(port, "{\"command\":\"ping\"}");
                Equal("INVALID_REQUEST", response.RootElement.GetProperty("code").GetString());
            });

            await TestAsync("trigger and result persistence", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "trigger-1",
                    command = "trigger",
                    parameters = new { mockX = 0.01, mockY = -0.01, toleranceMm = 0.05 }
                });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                var resultPath = response.RootElement.GetProperty("data").GetProperty("resultPath").GetString();
                Check(resultPath is not null && File.Exists(resultPath), "Result file was not persisted.");
            });

            await TestAsync("busy response", async () =>
            {
                var slow = SendAsync(port, new
                {
                    requestId = "slow-1",
                    command = "trigger",
                    parameters = new { delayMs = 500 }
                });
                await Task.Delay(50);
                using var response = await SendAsync(port, new { requestId = "busy-1", command = "trigger" });
                Equal("BUSY", response.RootElement.GetProperty("code").GetString());
                using var completed = await slow;
                Equal(true, completed.RootElement.GetProperty("ok").GetBoolean());
            });

            await TestAsync("timeout", async () =>
            {
                using var response = await SendAsync(port, new
                {
                    requestId = "timeout-1",
                    command = "trigger",
                    timeoutMs = 30,
                    parameters = new { delayMs = 300 }
                });
                Equal("TIMEOUT", response.RootElement.GetProperty("code").GetString());
            });

            await TestAsync("multiple JSON lines per connection", async () =>
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port);
                var stream = client.GetStream();
                var payload = "{\"requestId\":\"multi-1\",\"command\":\"ping\"}\n{\"requestId\":\"multi-2\",\"command\":\"status\"}\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(payload));
                using var first = JsonDocument.Parse(await ReadLineAsync(stream));
                using var second = JsonDocument.Parse(await ReadLineAsync(stream));
                Equal("multi-1", first.RootElement.GetProperty("requestId").GetString());
                Equal("multi-2", second.RootElement.GetProperty("requestId").GetString());
            });

            await TestAsync("local shutdown", async () =>
            {
                using var response = await SendAsync(port, new { requestId = "shutdown-1", command = "shutdown" });
                Equal(true, response.RootElement.GetProperty("ok").GetBoolean());
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            });

            Console.WriteLine($"All {_passed} self-tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            server.RequestStop();
            try
            {
                await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
            }

            return 1;
        }
    }

    private static async Task TestAsync(string name, Func<Task> test)
    {
        await test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static async Task<JsonDocument> SendAsync(int port, object request) =>
        await SendRawAsync(port, JsonSerializer.Serialize(request));

    private static async Task<JsonDocument> SendRawAsync(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(request + "\n"));
        return JsonDocument.Parse(await ReadLineAsync(stream));
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream)
    {
        var buffer = new byte[1];
        using var result = new MemoryStream();
        while (await stream.ReadAsync(buffer) == 1)
        {
            if (buffer[0] == (byte)'\n')
            {
                return Encoding.UTF8.GetString(result.ToArray()).TrimEnd('\r');
            }

            result.WriteByte(buffer[0]);
        }

        throw new IOException("Connection closed before a complete response.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
