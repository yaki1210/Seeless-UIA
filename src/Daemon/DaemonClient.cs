using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SeelessUIA.Daemon;

public class DaemonClient
{
    private readonly string _host;
    private readonly int _port;

    public DaemonClient(int port = 9222)
    {
        _host = "127.0.0.1";
        _port = port;
    }

    public async Task<JsonElement> SendAsync(object request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port);
        using var stream = client.GetStream();

        var json = JsonSerializer.Serialize(request) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var readTask = reader.ReadLineAsync(cts.Token);
        var line = await readTask;

        if (line == null)
            throw new IOException("Daemon closed connection without response");

        return JsonSerializer.Deserialize<JsonElement>(line);
    }
}
