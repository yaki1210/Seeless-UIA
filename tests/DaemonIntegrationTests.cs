using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace SeelessUIA.Tests;

public class DaemonIntegrationTests : IDisposable
{
    private readonly int _port;
    private CancellationTokenSource? _daemonCts;
    private Task? _daemonTask;

    public DaemonIntegrationTests()
    {
        _port = new Random().Next(10000, 15000);
    }

    public void Dispose()
    {
        _daemonCts?.Cancel();
        try { _daemonTask?.Wait(3000); } catch { }
        _daemonCts?.Dispose();
    }

    private async Task StartDaemonAsync()
    {
        if (_daemonTask != null) return;
        var server = new Daemon.DaemonServer(_port);
        _daemonCts = new CancellationTokenSource();
        _daemonTask = server.RunAsync();
        // Wait for daemon to be ready
        for (int i = 0; i < 30; i++)
        {
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", _port);
                return;
            }
            catch { await Task.Delay(100); }
        }
        throw new TimeoutException("Daemon did not start");
    }

    private async Task<JsonElement> SendAsync(object request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", _port);
        using var stream = client.GetStream();

        var json = JsonSerializer.Serialize(request) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync();
        Assert.NotNull(line);
        return JsonSerializer.Deserialize<JsonElement>(line);
    }

    private object MakeRequest(string action, string? windowRef = null, string? selector = null)
    {
        var req = new Dictionary<string, object?> { ["action"] = action, ["id"] = "test" };
        if (windowRef != null) req["windowRef"] = windowRef;
        if (selector != null) req["ref"] = selector;
        return req;
    }

    [Fact]
    public async Task Ping_ReturnsPong()
    {
        await StartDaemonAsync();
        var resp = await SendAsync(MakeRequest("ping"));
        Assert.True(resp.GetProperty("success").GetBoolean());
        Assert.True(resp.GetProperty("data").GetProperty("pong").GetBoolean());
    }

    [Fact]
    public async Task Windows_ReturnsRefList()
    {
        await StartDaemonAsync();
        var resp = await SendAsync(MakeRequest("windows"));
        Assert.True(resp.GetProperty("success").GetBoolean());
        var data = resp.GetProperty("data");
        Assert.True(data.TryGetProperty("windows", out _));
        var windows = data.GetProperty("windows");
        Assert.True(windows.GetArrayLength() > 0);
        var first = windows[0];
        Assert.True(first.TryGetProperty("refId", out _));
        Assert.True(first.TryGetProperty("processName", out _));
    }

    [Fact]
    public async Task Snapshot_NoWindowRef_ReturnsSuccess()
    {
        await StartDaemonAsync();
        var resp = await SendAsync(new Dictionary<string, object?>
        {
            ["action"] = "snapshot", ["id"] = "test", ["interactive"] = true
        });
        // Without window ref, uses desktop root — may be large but should succeed
        Assert.True(resp.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Click_InvalidRef_ReturnsError()
    {
        await StartDaemonAsync();
        var resp = await SendAsync(MakeRequest("click", null, "e99999"));
        Assert.False(resp.GetProperty("success").GetBoolean());
        Assert.True(resp.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task Snapshot_ReturnsRefs()
    {
        await StartDaemonAsync();
        // First get a window ref
        var winResp = await SendAsync(MakeRequest("windows"));
        var windows = winResp.GetProperty("data").GetProperty("windows");
        var firstW = windows[0].GetProperty("refId").GetString();

        var resp = await SendAsync(new Dictionary<string, object?>
        {
            ["action"] = "snapshot", ["id"] = "test", ["windowRef"] = firstW, ["interactive"] = true
        });
        Assert.True(resp.GetProperty("success").GetBoolean());
        var data = resp.GetProperty("data");
        Assert.True(data.TryGetProperty("snapshot", out _));
        Assert.True(data.TryGetProperty("refs", out _));
    }

    [Fact]
    public async Task Clipboard_WriteRead_Roundtrip()
    {
        await StartDaemonAsync();
        var writeResp = await SendAsync(new Dictionary<string, object?>
        {
            ["action"] = "clipboard_write", ["id"] = "test", ["value"] = "seeless-test-42"
        });
        Assert.True(writeResp.GetProperty("success").GetBoolean());

        var readResp = await SendAsync(new Dictionary<string, object?>
        {
            ["action"] = "clipboard_read", ["id"] = "test"
        });
        Assert.True(readResp.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task GetCount_ReturnsNumber()
    {
        await StartDaemonAsync();
        var winResp = await SendAsync(MakeRequest("windows"));
        var firstW = winResp.GetProperty("data").GetProperty("windows")[0].GetProperty("refId").GetString();

        var resp = await SendAsync(new Dictionary<string, object?>
        {
            ["action"] = "get_count", ["id"] = "test", ["windowRef"] = firstW, ["ref"] = "control:Button"
        });
        Assert.True(resp.GetProperty("success").GetBoolean());
        Assert.True(resp.GetProperty("data").TryGetProperty("count", out _));
    }

    [Fact]
    public async Task Close_ReturnsSuccess()
    {
        await StartDaemonAsync();
        var resp = await SendAsync(new Dictionary<string, object?>
        {
            ["action"] = "close", ["id"] = "test"
        });
        Assert.True(resp.GetProperty("success").GetBoolean());
    }
}
