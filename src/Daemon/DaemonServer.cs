using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using SeelessUIA.Element;
using SeelessUIA.Interaction;
using SeelessUIA.Protocol;
using SeelessUIA.Snapshot;
using SeelessUIA.Window;

namespace SeelessUIA.Daemon;

/// <summary>
/// Main daemon server listening on a TCP socket for NDJSON commands.
/// Corresponds to agent-browser's daemon.rs (run_socket_server + handle_connection).
/// </summary>
public class DaemonServer
{
    private readonly int _port;
    private readonly CancellationTokenSource _cts = new();
    private readonly WindowManager _windowManager = new();
    private readonly ProcessManager _processManager = new();
    private readonly ScreenshotCapture _screenshotCapture = new();

    // Per-connection state
    private RefMap _refMap = new();
    private AutomationElement? _currentRoot;

    public DaemonServer(int port = 9222)
    {
        _port = port;
    }

    public async Task RunAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, _port);
        listener.Start(10);
        Console.Error.WriteLine($"SeelessUIA daemon listening on 127.0.0.1:{_port}");

        // Write port file for auto-discovery by CLI
        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SeelessUIA");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, "daemon.port"), _port.ToString());

        var idleTimer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        var drainTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var acceptCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

                try
                {
                    var client = await listener.AcceptTcpClientAsync(acceptCts.Token);
                    _ = HandleConnectionAsync(client);
                }
                catch (OperationCanceledException)
                {
                    // No connection, continue loop
                }

                // Check idle timeout
                if (await idleTimer.WaitForNextTickAsync(_cts.Token))
                {
                    // Reset on each command — handled in HandleConnectionAsync
                }

                // Drain timer tick
                await drainTimer.WaitForNextTickAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.UTF8);

        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Skip HTTP-looking lines
                if (line.StartsWith("GET ") || line.StartsWith("POST "))
                    break;

                Request? request;
                try
                {
                    request = JsonSerializer.Deserialize<Request>(line);
                }
                catch
                {
                    var errResponse = Response.Fail("", $"Invalid JSON: {line[..Math.Min(line.Length, 100)]}");
                    await WriteResponseAsync(stream, errResponse);
                    continue;
                }

                if (request == null)
                {
                    await WriteResponseAsync(stream, Response.Fail("", "Empty request"));
                    continue;
                }

                var response = ExecuteCommand(request);
                await WriteResponseAsync(stream, response);

                // Handle close command
                if (request.Action == "close")
                {
                    _cts.Cancel();
                    break;
                }
            }
        }
        catch (IOException)
        {
            // Client disconnected
        }
    }

    private Response ExecuteCommand(Request request)
    {
        try
        {
            return request.Action switch
            {
                "snapshot" => HandleSnapshot(request),
                "click" => HandleClick(request),
                "fill" => HandleFill(request),
                "type" => HandleType(request),
                "hover" => HandleHover(request),
                "scroll" => HandleScroll(request),
                "check" => HandleCheck(request),
                "uncheck" => HandleUncheck(request),
                "focus" => HandleFocus(request),
                "press" => HandlePress(request),
                "expand" => HandleExpand(request),
                "collapse" => HandleCollapse(request),
                "select" => HandleSelect(request),
                "scroll_into_view" => HandleScrollIntoView(request),
                "window_list" => HandleWindowList(request),
                "window_focus" => HandleWindowFocus(request),
                "window_close" => HandleWindowClose(request),
                "app_launch" => HandleAppLaunch(request),
                "screenshot" => HandleScreenshot(request),
                "close" => Response.Ok(request.Id, new { message = "Shutting down" }),
                _ => Response.Fail(request.Id, $"Unknown action: {request.Action}"),
            };
        }
        catch (Exception ex)
        {
            return Response.Fail(request.Id, ex.Message);
        }
    }

    private AutomationElement GetOrResolveRoot(Request request)
    {
        if (_currentRoot != null)
            return _currentRoot;

        if (request.ProcessId.HasValue)
        {
            _currentRoot = _windowManager.FindWindowByProcessId(request.ProcessId.Value)
                ?? throw new InvalidOperationException($"No window found for process ID {request.ProcessId}");
        }
        else if (request.Hwnd.HasValue)
        {
            _currentRoot = _windowManager.FindWindowByHwnd(request.Hwnd.Value)
                ?? throw new InvalidOperationException($"No window found for HWND {request.Hwnd}");
        }
        else
        {
            // Default: use desktop root (may produce huge trees)
            _currentRoot = AutomationElement.RootElement;
        }

        return _currentRoot;
    }

    private string GetSelectorOrRef(Request request)
    {
        return request.Ref ?? request.Selector
            ?? throw new InvalidOperationException("Either 'ref' or 'selector' is required");
    }

    // ── Snapshot ─────────────────────────────────────────────

    private Response HandleSnapshot(Request request)
    {
        var root = GetOrResolveRoot(request);

        _refMap.Clear();
        var options = new SnapshotOptions
        {
            Interactive = request.Interactive ?? false,
            Structured = !(request.Interactive ?? false),
            Compact = request.Compact ?? false,
            Depth = request.Depth,
            RawView = false,
        };

        var pipeline = new SnapshotPipeline(options, _refMap);
        var snapshotText = pipeline.TakeSnapshot(root);

        var refsObj = new Dictionary<string, object>();
        foreach (var (refId, entry) in _refMap.EntriesSorted())
        {
            refsObj[refId] = new { role = entry.Role, name = entry.Name };
        }

        return Response.Ok(request.Id, new
        {
            snapshot = snapshotText,
            refs = refsObj,
        });
    }

    // ── Actions ──────────────────────────────────────────────

    private Response HandleClick(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        var button = request.Button ?? "left";
        var clickCount = request.ClickCount ?? 1;
        executor.Click(selector, button, clickCount);
        return Response.Ok(request.Id, new { clicked = selector, button, clickCount });
    }

    private Response HandleFill(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        var value = request.Value ?? "";
        executor.Fill(selector, value);
        return Response.Ok(request.Id, new { filled = selector, value });
    }

    private Response HandleType(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        var text = request.Text ?? "";
        var delay = request.Delay ?? 0;
        executor.Type(selector, text, delay);
        return Response.Ok(request.Id, new { typed = selector, text });
    }

    private Response HandleHover(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Hover(selector);
        return Response.Ok(request.Id, new { hovered = selector });
    }

    private Response HandleScroll(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);

        var horizontalPercent = double.NaN;
        var verticalPercent = double.NaN;

        // Pixel mode: x/y deltas
        if (request.X.HasValue || request.Y.HasValue)
        {
            var dx = request.X ?? 0;
            var dy = request.Y ?? 0;
            // Convert approximate pixels to percentage (rough: 1 page ~ 1000px)
            horizontalPercent = dx / 1000.0;
            verticalPercent = dy / 1000.0;
        }
        else if (!string.IsNullOrEmpty(request.Direction))
        {
            var amount = request.Amount ?? 300.0;
            switch (request.Direction.ToLowerInvariant())
            {
                case "up": verticalPercent = -amount / 1000.0; break;
                case "down": verticalPercent = amount / 1000.0; break;
                case "left": horizontalPercent = -amount / 1000.0; break;
                case "right": horizontalPercent = amount / 1000.0; break;
            }
        }
        else
        {
            // Default: scroll down half a page
            verticalPercent = 0.5;
        }

        executor.Scroll(selector, horizontalPercent, verticalPercent);
        return Response.Ok(request.Id, new { scrolled = selector });
    }

    private Response HandleCheck(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Check(selector);
        return Response.Ok(request.Id, new { checked_target = selector });
    }

    private Response HandleUncheck(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Uncheck(selector);
        return Response.Ok(request.Id, new { unchecked_target = selector });
    }

    private Response HandleFocus(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Focus(selector);
        return Response.Ok(request.Id, new { focused = selector });
    }

    private Response HandlePress(Request request)
    {
        var key = request.Key ?? throw new InvalidOperationException("'key' is required");
        var resolver = new ElementResolver(_refMap, GetOrResolveRoot(request));
        var executor = new ActionExecutor(resolver);
        executor.Press(key);
        return Response.Ok(request.Id, new { pressed = key });
    }

    private Response HandleExpand(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Expand(selector);
        return Response.Ok(request.Id, new { expanded = selector });
    }

    private Response HandleCollapse(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Collapse(selector);
        return Response.Ok(request.Id, new { collapsed = selector });
    }

    private Response HandleSelect(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.Select(selector);
        return Response.Ok(request.Id, new { selected = selector });
    }

    private Response HandleScrollIntoView(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver);
        var selector = GetSelectorOrRef(request);
        executor.ScrollIntoView(selector);
        return Response.Ok(request.Id, new { scrolled_into_view = selector });
    }

    // ── Window Management ────────────────────────────────────

    private Response HandleWindowList(Request request)
    {
        var windows = _windowManager.ListWindows();
        var list = windows.Select(w => new
        {
            hwnd = w.Hwnd.ToInt64(),
            title = w.Title,
            processId = w.ProcessId,
            processName = w.ProcessName,
        }).ToList();

        return Response.Ok(request.Id, new { windows = list });
    }

    private Response HandleWindowFocus(Request request)
    {
        if (request.ProcessId.HasValue)
        {
            var win = _windowManager.FindWindowByProcessId(request.ProcessId.Value)
                ?? throw new InvalidOperationException($"No window for process {request.ProcessId}");
            _currentRoot = win;
            var hwnd = (nint)win.Current.NativeWindowHandle;
            _windowManager.FocusWindow(hwnd);
            return Response.Ok(request.Id, new { focused = true, processId = request.ProcessId });
        }
        else if (request.Hwnd.HasValue)
        {
            var win = _windowManager.FindWindowByHwnd(request.Hwnd.Value)
                ?? throw new InvalidOperationException($"No window for HWND {request.Hwnd}");
            _currentRoot = win;
            _windowManager.FocusWindow((nint)request.Hwnd.Value);
            return Response.Ok(request.Id, new { focused = true, hwnd = request.Hwnd });
        }

        return Response.Fail(request.Id, "Either 'processId' or 'hwnd' is required");
    }

    private Response HandleWindowClose(Request request)
    {
        if (request.Hwnd.HasValue)
        {
            _windowManager.CloseWindow((nint)request.Hwnd.Value);
            return Response.Ok(request.Id, new { closed = true });
        }
        return Response.Fail(request.Id, "'hwnd' is required for window_close");
    }

    private Response HandleAppLaunch(Request request)
    {
        var path = request.Url ?? throw new InvalidOperationException("'url' (path) is required");
        var process = _processManager.Launch(path);
        process.WaitForInputIdle(5000);
        Thread.Sleep(1000); // Allow UIA tree to populate

        _currentRoot = _windowManager.FindWindowByProcessId(process.Id)
            ?? throw new InvalidOperationException($"Launched but no UIA window found for PID {process.Id}");

        return Response.Ok(request.Id, new
        {
            processId = process.Id,
            windowTitle = _currentRoot.Current.Name,
        });
    }

    private Response HandleScreenshot(Request request)
    {
        var root = GetOrResolveRoot(request);
        var base64 = _screenshotCapture.CaptureScreenshot(root);
        return Response.Ok(request.Id, new { screenshot = base64, format = "png" });
    }

    // ── Helpers ──────────────────────────────────────────────

    private static async Task WriteResponseAsync(NetworkStream stream, Response response)
    {
        var json = response.ToJson() + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }
}
