using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SeelessUIA.Daemon;
using Xunit;

namespace SeelessUIA.Tests;

public class DaemonIntegrationTests
{
    private readonly int _port = 9221;

    private async Task<JsonElement> SendAsync(int port, object request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        using var stream = client.GetStream();

        var json = JsonSerializer.Serialize(request) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync(cts.Token);
        Assert.NotNull(line);
        return JsonSerializer.Deserialize<JsonElement>(line);
    }

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        using var server = StartDaemon(_port);
        var resp = await SendAsync(_port, new { action = "ping", id = "t1" });
        Assert.True(resp.GetProperty("success").GetBoolean());
        Assert.True(resp.GetProperty("data").GetProperty("pong").GetBoolean());
    }

    [Fact]
    public async Task Windows_ReturnsWindowsList()
    {
        using var server = StartDaemon(_port);
        var resp = await SendAsync(_port, new { action = "windows", id = "t2" });
        Assert.True(resp.GetProperty("success").GetBoolean());
        var data = resp.GetProperty("data");
        Assert.True(data.TryGetProperty("windows", out var wins));
        Assert.True(wins.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Windows_EachEntryHasRequiredFields()
    {
        using var server = StartDaemon(_port);
        var resp = await SendAsync(_port, new { action = "windows", id = "t3" });
        var wins = resp.GetProperty("data").GetProperty("windows");
        var first = wins[0];
        Assert.True(first.TryGetProperty("refId", out _));
        Assert.True(first.TryGetProperty("processName", out _));
        Assert.True(first.TryGetProperty("title", out _));
    }

    [Fact]
    public async Task Click_InvalidRef_ReturnsError()
    {
        using var server = StartDaemon(_port);
        // First set a window via the windows command
        var winResp = await SendAsync(_port, new { action = "windows", id = "setup" });
        var active = winResp.GetProperty("data").GetProperty("active").GetString();

        // Now try clicking a non-existent ref
        var dict = new Dictionary<string, object?>
        {
            ["action"] = "click",
            ["id"] = "t4",
            ["windowRef"] = active,
            ["ref"] = "e99999",
        };
        var resp = await SendAsync(_port, dict);
        // The element resolver will fail because ref doesn't exist — should fail gracefully
        Assert.True(resp.TryGetProperty("error", out _) || !resp.GetProperty("success").GetBoolean());
    }

    private static DaemonServerHandle StartDaemon(int port)
    {
        var server = new DaemonServer(port);
        var cts = new CancellationTokenSource();
        var task = Task.Run(async () =>
        {
            try { await server.RunAsync(); }
            catch (OperationCanceledException) { }
        });
        Thread.Sleep(500);
        return new DaemonServerHandle(cts, task);
    }

    private class DaemonServerHandle : IDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _task;
        public DaemonServerHandle(CancellationTokenSource cts, Task task)
        {
            _cts = cts; _task = task;
        }
        public void Dispose()
        {
            _cts.Cancel();
            try { _task.Wait(2000); } catch { }
            _cts.Dispose();
        }
    }
}
