using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
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
    private readonly WindowRegistry _registry = new();

    // Per-connection state
    private RefMap _refMap = new();
    private AutomationElement? _currentRoot;

    // Last snapshot baseline for --diff and auto-diff
    private RefMap? _baselineRefMap;

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

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var acceptCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

                try
                {
                    var client = await listener.AcceptTcpClientAsync(acceptCts.Token);
                    _ = Task.Run(async () =>
                    {
                        try { await HandleConnectionAsync(client); }
                        catch (Exception ex) { Console.Error.WriteLine($"[daemon] error: {ex.Message}"); }
                    });
                }
                catch (OperationCanceledException)
                {
                    // No connection, continue loop
                }
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

                    // Execute with timeout (default 30s, configurable via request.timeout)
                    var timeoutMs = request.Timeout ?? 30000;
                    var execTask = Task.Run(() => ExecuteCommand(request));
                    var timeoutTask = Task.Delay(timeoutMs);
                    var completed = await Task.WhenAny(execTask, timeoutTask);
                    Response response;
                    if (completed == execTask)
                    {
                        response = execTask.Result;
                    }
                    else
                    {
                        response = Response.Fail(request.Id, $"Request timed out after {timeoutMs}ms");
                    }
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
                "get_text" => HandleGetText(request),
                "get_value" => HandleGetValue(request),
                "get_box" => HandleGetBox(request),
                "get_count" => HandleGetCount(request),
                "is_visible" => HandleIsVisible(request),
                "is_enabled" => HandleIsEnabled(request),
                "is_checked" => HandleIsChecked(request),
                "wait" => HandleWait(request),
                "keydown" => HandleKeyDown(request),
                "keyup" => HandleKeyUp(request),
                "mouse_move" => HandleMouseMove(request),
                "mouse_down" => HandleMouseDown(request),
                "mouse_up" => HandleMouseUp(request),
                "mouse_wheel" => HandleMouseWheel(request),
                "keyboard_type" => HandleKeyboardType(request),
                "drag" => HandleDrag(request),
                "get_attr" => HandleGetAttr(request),
                "scroll_amount" => HandleScrollAmount(request),
                "ping" => HandlePing(request),
                "clipboard_read" => HandleClipboardRead(request),
                "clipboard_write" => HandleClipboardWrite(request),
                "clipboard_copy" => HandleClipboardCopy(request),
                "clipboard_paste" => HandleClipboardPaste(request),
                "find_execute" => HandleFindExecute(request),
                "wait_text" => HandleWaitText(request),
                "window_list" => HandleWindowList(request),
                "windows" => HandleWindowList(request),
                "window_focus" => HandleWindowFocus(request),
                "window_close" => HandleWindowClose(request),
                "window" => HandleWindowSwitch(request),
                "app_launch" => HandleAppLaunch(request),
                "screenshot" => HandleScreenshot(request),
                "close" => Response.Ok(request.Id, new { message = "Shutting down" }), // daemon shutdown (internal)
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
        // Explicit targets always win over cached _currentRoot
        if (!string.IsNullOrEmpty(request.WindowRef))
            return ResolveWindowRef(request.WindowRef);

        if (request.ProcessId.HasValue)
        {
            _currentRoot = _windowManager.FindWindowByProcessId(request.ProcessId.Value)
                ?? throw new InvalidOperationException($"No window found for process ID {request.ProcessId}");
            return _currentRoot;
        }

        if (request.Hwnd.HasValue)
        {
            _currentRoot = _windowManager.FindWindowByHwnd(request.Hwnd.Value)
                ?? throw new InvalidOperationException($"No window found for HWND {request.Hwnd}");
            return _currentRoot;
        }

        if (_currentRoot != null)
            return _currentRoot;

        // Implicit: registry active window
        var activeEntry = _registry.GetActive();
        if (activeEntry != null)
        {
            try
            {
                _currentRoot = _windowManager.FindWindowByHwnd(activeEntry.Hwnd);
                if (_currentRoot != null) return _currentRoot;
            }
            catch { }
        }

        _currentRoot = AutomationElement.RootElement;
        return _currentRoot;
    }

    /// <summary>
    /// Refresh the window registry from the current set of visible windows.
    /// Existing HWNDs keep their wN; closed windows are retired; new windows get fresh wN.
    /// </summary>
    private void RefreshWindowRegistry()
    {
        var entries = _windowManager.ListWindows()
            .Select(w => new WindowEntry
            {
                Hwnd = w.Hwnd.ToInt64(),
                ProcessId = (int)w.ProcessId,
                ProcessName = w.ProcessName,
                Title = w.Title,
            })
            .ToList();
        _registry.RefreshAll(entries);
    }

    /// <summary>
    /// Resolve wN to a live AutomationElement. Refreshes registry on miss or stale HWND.
    /// </summary>
    private AutomationElement ResolveWindowRef(string windowRef)
    {
        WindowEntry? entry = _registry.Get(windowRef);
        if (entry == null)
        {
            RefreshWindowRegistry();
            entry = _registry.Get(windowRef)
                ?? throw new InvalidOperationException(
                    $"Window '{windowRef}' not found. Run 'windows' to list available windows.");
        }

        var root = _windowManager.FindWindowByHwnd(entry.Hwnd);
        if (root == null)
        {
            RefreshWindowRegistry();
            entry = _registry.Get(windowRef)
                ?? throw new InvalidOperationException(
                    $"Window '{windowRef}' is no longer available (ref retired). Run 'windows' to get a new ref.");
            root = _windowManager.FindWindowByHwnd(entry.Hwnd)
                ?? throw new InvalidOperationException(
                    $"Window '{windowRef}' is no longer available.");
        }

        _currentRoot = root;
        _registry.SetActive(windowRef);
        return root;
    }

    private string GetWindowTitle()
    {
        if (_currentRoot != null)
        {
            try
            {
                var name = _currentRoot.Current.Name;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            catch { }
        }
        var active = _registry.GetActive();
        if (active != null && !string.IsNullOrEmpty(active.Title))
            return active.Title;
        return "Desktop";
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

        // Save previous snapshot as baseline before taking new one
        var prevBaseline = request.Diff == true ? CloneRefMap() : null;

        var options = new SnapshotOptions
        {
            Interactive = request.Interactive ?? false,
            Structured = !(request.Interactive ?? false),
            Compact = request.Compact ?? false,
            Depth = request.Depth,
            RawView = false,
            NoClean = request.NoClean ?? false,
        };

        const int maxRetries = 3;
        const int retryDelayMs = 50;
        const int minRefs = 5;

        string snapshotText = "";
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            _refMap.Clear();
            var pipeline = new SnapshotPipeline(options, _refMap);
            snapshotText = pipeline.TakeSnapshot(root);

            if (_refMap.Count > minRefs)
                break;

            if (attempt < maxRetries)
                Thread.Sleep(retryDelayMs);
        }

        var refsObj = new Dictionary<string, object>();
        foreach (var (refId, entry) in _refMap.EntriesSorted())
        {
            refsObj[refId] = new { role = entry.Role, name = entry.Name };
        }

        // Update baseline for future diffs
        _baselineRefMap = CloneRefMap();

        // --diff mode: return only changes
        if (prevBaseline != null)
        {
            var changes = SnapshotDiff.Compare(prevBaseline, _refMap);
            return Response.Ok(request.Id, new
            {
                snapshot = snapshotText,
                refs = refsObj,
                changes = changes.Select(c => new { c.Ref, c.Role, c.Name, c.Kind }).ToList()
            });
        }

        return Response.Ok(request.Id, new
        {
            snapshot = snapshotText,
            refs = refsObj,
        });
    }

    private RefMap? CloneRefMap()
    {
        if (_refMap.Count == 0) return null;
        var clone = new RefMap();
        foreach (var (refId, entry) in _refMap.EntriesSorted())
            clone.Add(refId, entry.RuntimeId, entry.Role, entry.Name, entry.Nth);
        return clone;
    }

    // ── Actions ──────────────────────────────────────────────

    /// <summary>
    /// After an action that potentially changes the UI, take a fresh snapshot
    /// and compute changes against the previous baseline.
    /// Returns diff entries for inclusion in the action response.
    /// </summary>
    private object? DiffPostAction(AutomationElement root, bool interactive = true)
    {
        if (_baselineRefMap == null) return null;

        var options = new SnapshotOptions
        {
            Interactive = interactive,
            Structured = !interactive,
            Compact = false,
            RawView = false,
            NoClean = false,
        };

        _refMap.Clear();
        var pipeline = new SnapshotPipeline(options, _refMap);
        pipeline.TakeSnapshot(root);

        var changes = SnapshotDiff.Compare(_baselineRefMap, _refMap);
        _baselineRefMap = CloneRefMap();

        if (changes.Count == 0) return null;

        return changes.Select(c => new { c.Ref, c.Role, c.Name, c.Kind }).ToList();
    }

    /// <summary>
    /// Execute an action and return its response, with auto-diff changes appended.
    /// </summary>
    private Response ExecuteWithDiff(Request request, Action execute, object data)
    {
        var root = GetOrResolveRoot(request);
        execute();
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, data);
    }

    /// <summary>
    /// Create a foreground activation callback for SendInput fallback paths.
    /// Pattern-only operations never call this; it's only invoked when the
    /// UIA Pattern path fails and a SendInput fallback is necessary.
    /// </summary>
    private Action? GetActivateCallback(AutomationElement root)
    {
        try
        {
            var hwnd = (nint)root.Current.NativeWindowHandle;
            if (hwnd != nint.Zero)
                return () => { _windowManager.FocusWindow(hwnd); Thread.Sleep(30); };
        }
        catch { }
        return null;
    }

    private Response HandleClick(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var button = request.Button ?? "left";
        var clickCount = request.ClickCount ?? 1;
        executor.Click(selector, button, clickCount);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { clicked = selector, button, clickCount, windowTitle = GetWindowTitle(), changes });
    }

    private Response HandleFill(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var value = request.Value ?? "";
        executor.Fill(selector, value);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { filled = selector, value, windowTitle = GetWindowTitle(), changes });
    }

    private Response HandleType(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var text = request.Text ?? "";
        var delay = request.Delay ?? 0;
        executor.Type(selector, text, delay);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { typed = selector, text, windowTitle = GetWindowTitle(), changes });
    }

    private Response HandleHover(Request request)
    {
        var root = GetOrResolveRoot(request);
        
            var resolver = new ElementResolver(_refMap, root);
            var executor = new ActionExecutor(resolver, GetActivateCallback(root));
            var selector = GetSelectorOrRef(request);
            executor.Hover(selector);
            return Response.Ok(request.Id, new { hovered = selector, windowTitle = GetWindowTitle() });
        
    }

    private Response HandleScroll(Request request)
    {
        var root = GetOrResolveRoot(request);
        
            var resolver = new ElementResolver(_refMap, root);
            var executor = new ActionExecutor(resolver, GetActivateCallback(root));

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

            // When a selector is given, try Pattern-based scroll on the element.
            // When no selector, use mouse wheel simulation at window center.
            string? selector = request.Ref ?? request.Selector;
            if (!string.IsNullOrEmpty(selector))
            {
                executor.Scroll(selector, horizontalPercent, verticalPercent);
            }
            else
            {
                // Send mouse wheel at window center (no element resolution needed)
                var rect = root.Current.BoundingRectangle;
                int cx = (int)(rect.Left + rect.Width / 2);
                int cy = (int)(rect.Top + rect.Height / 2);
                executor.ScrollWindow((int)(horizontalPercent * 1000), (int)(verticalPercent * 1000), cx, cy);
            }

            return Response.Ok(request.Id, new { scrolled = selector ?? "window", windowTitle = GetWindowTitle() });
        
    }

    private Response HandleCheck(Request request)
    {
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.Check(selector);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { checked_target = selector, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandleUncheck(Request request)
    {
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.Uncheck(selector);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { unchecked_target = selector, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandleFocus(Request request)
    {
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.Focus(selector);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { focused = selector, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandlePress(Request request)
    {
        var key = request.Key ?? throw new InvalidOperationException("'key' is required");
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.Press(key);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { pressed = key, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandleExpand(Request request)
    {
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.Expand(selector);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { expanded = selector, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandleCollapse(Request request)
    {
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.Collapse(selector);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { collapsed = selector, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandleSelect(Request request)
    {
        var root = GetOrResolveRoot(request);
        
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.Select(selector);
        var changes = DiffPostAction(root);
        return Response.Ok(request.Id, new { selected = selector, windowTitle = GetWindowTitle(), changes });
        
    }

    private Response HandleScrollIntoView(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        executor.ScrollIntoView(selector);
        return Response.Ok(request.Id, new { scrolled_into_view = selector, windowTitle = GetWindowTitle() });
    }

    private Response HandleGetText(Request request)
    {
        var root = GetOrResolveRoot(request);

        // --search mode: scan all text elements for matching content
        if (!string.IsNullOrEmpty(request.SearchText))
        {
            var search = request.SearchText;
            var allText = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
            foreach (AutomationElement el in allText)
            {
                try
                {
                    var name = el.Current.Name ?? "";
                    if (name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    {
                        var resolver = new ElementResolver(_refMap, root);
                        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
                        var text = executor.GetText("name:" + name);
                        return Response.Ok(request.Id, new { text, name, matched = search });
                    }
                }
                catch { }
            }
            return Response.Fail(request.Id, $"No text element containing '{search}' found.");
        }

        var selector = GetSelectorOrRef(request);
        var resolver2 = new ElementResolver(_refMap, root);
        var executor2 = new ActionExecutor(resolver2);
        var textResult = executor2.GetText(selector);
        return Response.Ok(request.Id, new { text = textResult });
    }

    private Response HandleGetValue(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var value = executor.GetValue(selector);
        return Response.Ok(request.Id, new { value });
    }

    private Response HandleGetBox(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var rect = executor.GetBox(selector);
        return Response.Ok(request.Id, new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height });
    }

    private Response HandleGetCount(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var count = executor.GetCount(selector);
        return Response.Ok(request.Id, new { count });
    }

    private Response HandleIsVisible(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var visible = executor.IsVisible(selector);
        return Response.Ok(request.Id, new { visible });
    }

    private Response HandleIsEnabled(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var enabled = executor.IsEnabled(selector);
        return Response.Ok(request.Id, new { enabled });
    }

    private Response HandleIsChecked(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var isChecked = executor.IsChecked(selector);
        return Response.Ok(request.Id, new { @checked = isChecked });
    }

    private Response HandleWait(Request request)
    {
        var root = GetOrResolveRoot(request);
        var selector = GetSelectorOrRef(request);
        var timeoutMs = request.Timeout ?? 30000;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var resolver = new ElementResolver(_refMap, root);
                resolver.ResolveElement(selector);
                return Response.Ok(request.Id, new { appeared = true });
            }
            catch { }
            Thread.Sleep(100);
        }
        return Response.Fail(request.Id, $"Timed out after {timeoutMs}ms waiting for {selector}");
    }

    private Response HandleKeyDown(Request request)
    {
        var root = GetOrResolveRoot(request);
        var key = request.Key ?? throw new InvalidOperationException("'key' required");
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.KeyDown(key);
        return Response.Ok(request.Id, new { keydown = key, windowTitle = GetWindowTitle() });
    }

    private Response HandleKeyUp(Request request)
    {
        var root = GetOrResolveRoot(request);
        var key = request.Key ?? throw new InvalidOperationException("'key' required");
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.KeyUp(key);
        return Response.Ok(request.Id, new { keyup = key, windowTitle = GetWindowTitle() });
    }

    private Response HandleMouseMove(Request request)
    {
        var root = GetOrResolveRoot(request);
        var x = (int)(request.X ?? 0);
        var y = (int)(request.Y ?? 0);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.MouseMove(x, y);
        return Response.Ok(request.Id, new { x, y, windowTitle = GetWindowTitle() });
    }

    private Response HandleMouseDown(Request request)
    {
        var root = GetOrResolveRoot(request);
        var button = request.Button ?? "left";
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.MouseDown(button);
        return Response.Ok(request.Id, new { mousedown = button, windowTitle = GetWindowTitle() });
    }

    private Response HandleMouseUp(Request request)
    {
        var root = GetOrResolveRoot(request);
        var button = request.Button ?? "left";
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.MouseUp(button);
        return Response.Ok(request.Id, new { mouseup = button, windowTitle = GetWindowTitle() });
    }

    private Response HandleMouseWheel(Request request)
    {
        var root = GetOrResolveRoot(request);
        var dy = (int)(request.Dy ?? -120);
        var activate = GetActivateCallback(root);
        activate?.Invoke();
        var resolver = new ElementResolver(_refMap, root);
        new SendInputActions(resolver).MouseWheel(dy);
        return Response.Ok(request.Id, new { delta = dy, windowTitle = GetWindowTitle() });
    }

    private Response HandleKeyboardType(Request request)
    {
        var root = GetOrResolveRoot(request);
        var text = request.Text ?? "";
        var delay = request.Delay ?? 0;
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.KeyboardType(text, delay);
        return Response.Ok(request.Id, new { typed = text, windowTitle = GetWindowTitle() });
    }

    private Response HandleDrag(Request request)
    {
        var src = request.Ref ?? throw new InvalidOperationException("'ref' required for source");
        var tgt = request.Selector ?? throw new InvalidOperationException("'selector' required for target");
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        executor.Drag(src, tgt);
        return Response.Ok(request.Id, new { dragged = src, target = tgt, windowTitle = GetWindowTitle() });
    }

    private Response HandleGetAttr(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var attr = request.Attr ?? throw new InvalidOperationException("'attr' required");
        var value = executor.GetAttr(selector, attr);
        return Response.Ok(request.Id, new { value });
    }

    private Response HandleScrollAmount(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var executor = new ActionExecutor(resolver, GetActivateCallback(root));
        var selector = GetSelectorOrRef(request);
        var large = true;
        executor.ScrollByAmount(selector, large);
        return Response.Ok(request.Id, new { scrolled = selector, windowTitle = GetWindowTitle() });
    }

    private Response HandlePing(Request request)
    {
        var uptime = (DateTime.UtcNow - _startTime).TotalSeconds;
        return Response.Ok(request.Id, new { pong = true, uptime = (int)uptime });
    }

    private Response HandleClipboardRead(Request request)
    {
        var result = "";
        var t = new Thread(() => { result = System.Windows.Clipboard.GetText(); });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(5000);
        return Response.Ok(request.Id, new { text = result });
    }

    private Response HandleClipboardWrite(Request request)
    {
        var text = request.Value ?? "";
        var t = new Thread(() => { System.Windows.Clipboard.SetText(text); });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(5000);
        return Response.Ok(request.Id, new { written = true, windowTitle = GetWindowTitle() });
    }

    private Response HandleClipboardCopy(Request request)
    {
        var resolver = new ElementResolver(_refMap, GetOrResolveRoot(request));
        new SendInputActions(resolver).KeyDown(0x11); // Ctrl
        new SendInputActions(resolver).PressKey(0x43); // 'C'
        new SendInputActions(resolver).KeyUp(0x11);
        return Response.Ok(request.Id, new { copied = true, windowTitle = GetWindowTitle() });
    }

    private Response HandleClipboardPaste(Request request)
    {
        var resolver = new ElementResolver(_refMap, GetOrResolveRoot(request));
        new SendInputActions(resolver).KeyDown(0x11); // Ctrl
        new SendInputActions(resolver).PressKey(0x56); // 'V'
        new SendInputActions(resolver).KeyUp(0x11);
        return Response.Ok(request.Id, new { pasted = true, windowTitle = GetWindowTitle() });
    }

    private Response HandleFindExecute(Request request)
    {
        var root = GetOrResolveRoot(request);
        var resolver = new ElementResolver(_refMap, root);
        var nameFilter = request.Key ?? "";
        // Store action type temporarily, swap name filter into Text for FindByLocator
        var actionType = request.Text ?? "click";
        if (!string.IsNullOrEmpty(nameFilter))
            request.Text = nameFilter;
        var element = resolver.FindByLocator(request.Value ?? "", request);
        request.Text = actionType; // restore
        if (element == null)
            return Response.Fail(request.Id, $"No element found matching the locator");

        var subAction = actionType;
        var subValue = request.Selector ?? "";

        switch (subAction)
        {
            case "click":
                try
                {
                    if (PatternActions.TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out var ip))
                        ip.Invoke();
                    else
                    {
                        var r = element.Current.BoundingRectangle;
                        new SendInputActions(resolver).Click("name:" + (element.Current.Name ?? ""));
                    }
                }
                catch { }
                break;
            case "fill":
                try { element.SetFocus(); } catch { }
                if (PatternActions.TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var vp))
                    vp.SetValue(subValue);
                else
                    new SendInputActions(resolver).Fill("name:" + (element.Current.Name ?? ""), subValue);
                break;
            case "type":
                try { element.SetFocus(); } catch { }
                new SendInputActions(resolver).TypeText(subValue);
                break;
            case "hover":
                var r2 = element.Current.BoundingRectangle;
                new SendInputActions(resolver).Hover("name:" + (element.Current.Name ?? ""));
                break;
            case "focus":
                try { element.SetFocus(); } catch { }
                break;
            case "check":
                try
                {
                    if (PatternActions.TryGetTogglePattern(element, out var tp))
                        tp.Toggle();
                }
                catch { }
                break;
            case "uncheck":
                try
                {
                    if (PatternActions.TryGetTogglePattern(element, out var tp2))
                        tp2.Toggle();
                }
                catch { }
                break;
            case "text":
                var t = "";
                try
                {
                    if (PatternActions.TryGetPattern<TextPattern>(element, TextPattern.Pattern, out var txtp))
                        t = txtp.DocumentRange.GetText(-1);
                }
                catch { }
                if (string.IsNullOrEmpty(t))
                    t = element.Current.Name ?? "";
                return Response.Ok(request.Id, new { text = t });
            default:
                break;
        }

        return Response.Ok(request.Id, new { found = true, action = subAction, windowTitle = GetWindowTitle() });
    }

    private Response HandleWaitText(Request request)
    {
        var root = GetOrResolveRoot(request);
        var text = request.Value ?? throw new InvalidOperationException("'value' (text to wait for) required");
        var timeoutMs = request.Timeout ?? 30000;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
                try
                {
                    var allElements = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                    foreach (AutomationElement el in allElements)
                    {
                        try
                        {
                            var name = el.Current.Name ?? "";
                            if (name.Contains(text, StringComparison.OrdinalIgnoreCase))
                                return Response.Ok(request.Id, new { appeared = true, in_text = name });
                            if (PatternActions.TryGetPattern<ValuePattern>(el, ValuePattern.Pattern, out var vp))
                            {
                                var val = vp.Current.Value ?? "";
                                if (val.Contains(text, StringComparison.OrdinalIgnoreCase))
                                    return Response.Ok(request.Id, new { appeared = true, in_text = val });
                            }
                        }
                        catch { }
                    }
                }
                catch (ElementNotAvailableException)
                {
                    return Response.Fail(request.Id, "Window closed while waiting for text");
                }
                catch { }
            Thread.Sleep(100);
        }
        return Response.Fail(request.Id, $"Timed out after {timeoutMs}ms waiting for text '{text}'");
    }

    private readonly DateTime _startTime = DateTime.UtcNow;

    // ── Window Management ────────────────────────────────────

    private Response HandleWindowList(Request request)
    {
        RefreshWindowRegistry();

        var list = _registry.ListAll().Select(kv => new
        {
            refId = kv.RefId,
            hwnd = kv.Entry.Hwnd,
            processId = kv.Entry.ProcessId,
            processName = kv.Entry.ProcessName,
            title = kv.Entry.Title,
        }).ToList();

        // Set current root to active window
        var active = _registry.GetActive();
        if (active != null)
            _currentRoot = _windowManager.FindWindowByHwnd(active.Hwnd);

        return Response.Ok(request.Id, new
        {
            windows = list,
            active = _registry.ActiveRef,
        });
    }

    private Response HandleWindowFocus(Request request)
    {
        if (!string.IsNullOrEmpty(request.WindowRef))
        {
            var root = ResolveWindowRef(request.WindowRef);
            var entry = _registry.Get(request.WindowRef)!;
            _windowManager.FocusWindow((nint)entry.Hwnd);
            return Response.Ok(request.Id, new { focused = request.WindowRef, windowTitle = entry.Title });
        }
        else if (request.ProcessId.HasValue)
        {
            var win = _windowManager.FindWindowByProcessId(request.ProcessId.Value)
                ?? throw new InvalidOperationException($"No window for process {request.ProcessId}");
            _currentRoot = win;
            var hwnd = (nint)win.Current.NativeWindowHandle;
            _windowManager.FocusWindow(hwnd);
            return Response.Ok(request.Id, new { focused = true, processId = request.ProcessId, windowTitle = GetWindowTitle() });
        }
        else if (request.Hwnd.HasValue)
        {
            var win = _windowManager.FindWindowByHwnd(request.Hwnd.Value)
                ?? throw new InvalidOperationException($"No window for HWND {request.Hwnd}");
            _currentRoot = win;
            _windowManager.FocusWindow((nint)request.Hwnd.Value);
            return Response.Ok(request.Id, new { focused = true, hwnd = request.Hwnd, windowTitle = GetWindowTitle() });
        }

        return Response.Fail(request.Id, "'windowRef', 'processId', or 'hwnd' is required");
    }

    private Response HandleWindowClose(Request request)
    {
        if (request.Hwnd.HasValue)
        {
            _windowManager.CloseWindow((nint)request.Hwnd.Value);
            _currentRoot = null;
            return Response.Ok(request.Id, new { closed = true, windowTitle = GetWindowTitle() });
        }
        if (!string.IsNullOrEmpty(request.WindowRef))
        {
            var entry = _registry.Get(request.WindowRef)
                ?? throw new InvalidOperationException($"Window '{request.WindowRef}' not found");
            _windowManager.CloseWindow((nint)entry.Hwnd);
            _currentRoot = null;
            var closedTitle = entry.Title;
            return Response.Ok(request.Id, new { closed = request.WindowRef, windowTitle = closedTitle });
        }
        var active = _registry.GetActive();
        if (active != null)
        {
            _windowManager.CloseWindow((nint)active.Hwnd);
            _currentRoot = null;
            var closedActiveTitle = active.Title;
            return Response.Ok(request.Id, new { closed = _registry.ActiveRef, windowTitle = closedActiveTitle });
        }
        return Response.Fail(request.Id, "No window to close. Use 'close wN' or set active window.");
    }

    private Response HandleWindowSwitch(Request request)
    {
        var refId = request.WindowRef ?? request.Ref
            ?? throw new InvalidOperationException("'windowRef' is required");
        ResolveWindowRef(refId);
        var entry = _registry.Get(refId);
        return Response.Ok(request.Id, new { active = refId, windowTitle = entry?.Title ?? GetWindowTitle() });
    }

    private Response HandleAppLaunch(Request request)
    {
        var path = request.Url ?? throw new InvalidOperationException("'url' (path) is required");
        var process = _processManager.Launch(path);
        try { process.WaitForInputIdle(3000); } catch { }
        Thread.Sleep(1500);

        // Try to find window by the launched process PID first
        _currentRoot = _windowManager.FindWindowByProcessId(process.Id);

        // UWP apps (like Calculator) launch via a stub — window uses a different PID
        if (_currentRoot == null)
        {
            var procName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var altNames = procName switch
            {
                "calc" => new[] { "CalculatorApp", "ApplicationFrameHost" },
                _ => Array.Empty<string>(),
            };
            foreach (var alt in altNames)
            {
                foreach (var p in Process.GetProcessesByName(alt))
                {
                    _currentRoot = _windowManager.FindWindowByProcessId(p.Id);
                    if (_currentRoot != null) break;
                }
                if (_currentRoot != null) break;
            }
        }

        // Last resort: wait and retry a few times
        if (_currentRoot == null)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(500);
                _currentRoot = _windowManager.FindWindowByProcessId(process.Id);
                if (_currentRoot != null) break;
                foreach (var p in Process.GetProcessesByName("CalculatorApp"))
                {
                    _currentRoot = _windowManager.FindWindowByProcessId(p.Id);
                    if (_currentRoot != null) break;
                }
                if (_currentRoot != null) break;
            }
        }

        if (_currentRoot == null)
            throw new InvalidOperationException($"Launched {path} but no UIA window was found after 5s");

        var title = _currentRoot.Current.Name ?? "";
        var hwnd = (long)_currentRoot.Current.NativeWindowHandle;
        var actualPid = _currentRoot.Current.ProcessId;
        var pName = Process.GetProcessById(actualPid)?.ProcessName ?? path;
        var refId = _registry.Register(hwnd, actualPid, pName, title);
        _registry.SetActive(refId);

        return Response.Ok(request.Id, new
        {
            refId,
            processId = actualPid,
            windowTitle = title,
        });
    }

    private Response HandleScreenshot(Request request)
    {
        _currentRoot = null;
        var root = GetOrResolveRoot(request);
        var base64 = _screenshotCapture.CaptureScreenshot(root);
        if (string.IsNullOrEmpty(base64))
            return Response.Fail(request.Id, "Screenshot capture failed (window may be minimized, off-screen, or not found)");
        return Response.Ok(request.Id, new { screenshot = base64, format = "png", windowTitle = GetWindowTitle() });
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

