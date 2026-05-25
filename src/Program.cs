using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using SeelessUIA.Daemon;
using SeelessUIA.Element;
using SeelessUIA.Snapshot;
using SeelessUIA.Window;

namespace SeelessUIA;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        var remainingArgs = args.Skip(1).ToArray();

        switch (command)
        {
            case "test":
                await RunTestAsync(remainingArgs);
                return 0;
            case "calc-test":
                await RunCalculatorTestAsync();
                return 0;
            case "snapshot":
            case "windows":
            case "window":
            case "app":
            case "launch":
            case "get":
            case "is":
            case "wait":
                return await RunDaemonActionAsync(command, remainingArgs);
            case "daemon":
                return await RunDaemonAsync(remainingArgs);
            case "help":
            case "--help":
            case "-h":
                PrintHelp();
                return 0;

            case "click":
            case "dblclick":
            case "fill":
            case "type":
            case "press":
            case "hover":
            case "scroll":
            case "check":
            case "uncheck":
            case "focus":
            case "expand":
            case "collapse":
            case "select":
            case "scrollintoview":
            case "scroll-into-view":
            case "close":
            case "screenshot":
                return await RunDaemonActionAsync(command, remainingArgs);
            default:
                Console.Error.WriteLine($"Unknown command: {command}");
                Console.Error.WriteLine("Use 'seeless-uia help' for available commands.");
                return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("SeelessUIA - Windows UI Automation CLI");
        Console.Error.WriteLine("Usage: seeless-uia <command> [options]");
        Console.Error.WriteLine("Use 'seeless-uia help' for full command listing.");
    }

    // ── Daemon action dispatch (all interaction commands) ────────

    private static async Task<int> RunDaemonActionAsync(string action, string[] args)
    {
        int port = 9222;
        int? processId = null;
        long? hwnd = null;
        string? windowRef = null;
        string? selector = null;
        string? value = null;
        string? text = null;
        string? key = null;
        string? button = "left";
        int clickCount = 1;
        string? direction = null;
        double? amount = null;
        int delay = 0;
        string? screenshotPath = null;
        bool interactive = false;
        bool compact = false;
        bool rawView = false;
        bool jsonMode = false;
        int? depth = null;
        int? timeout = null;

        int i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
                case "--pid" when i + 1 < args.Length: processId = int.Parse(args[++i]); break;
                case "--hwnd" when i + 1 < args.Length: hwnd = long.Parse(args[++i]); break;
                case "-i": interactive = true; break;
                case "-c": compact = true; break;
                case "--raw": rawView = true; break;
                case "-d" when i + 1 < args.Length: depth = int.Parse(args[++i]); break;
                case "--depth" when i + 1 < args.Length: depth = int.Parse(args[++i]); break;
                case "-r": break; // showRefs handled by daemon
                case "--all": break;
                case "--button" when i + 1 < args.Length: button = args[++i]; break;
                case "--click-count" when i + 1 < args.Length: clickCount = int.Parse(args[++i]); break;
                case "--direction" when i + 1 < args.Length: direction = args[++i]; break;
                case "--amount" when i + 1 < args.Length: amount = double.Parse(args[++i]); break;
                case "--delay" when i + 1 < args.Length: delay = int.Parse(args[++i]); break;
                case "-o" when i + 1 < args.Length: screenshotPath = args[++i]; break;
                case "--json": jsonMode = true; break;
                case "--timeout" when i + 1 < args.Length: timeout = int.Parse(args[++i]); break;
                case "--verbose": break;
                default:
                    if (!args[i].StartsWith('-'))
                    {
                        // First positional: window ref (w1, w2) or selector
                        if (args[i].StartsWith('w') && args[i].Length >= 2
                            && int.TryParse(args[i][1..], out _) && windowRef == null && selector == null)
                        {
                            windowRef = args[i];
                        }
                        else if (selector == null)
                            selector = args[i];
                        else if (value == null && text == null)
                            { value = args[i]; text = args[i]; }
                        else if (screenshotPath == null)
                            screenshotPath = args[i];
                    }
                    break;
            }
            i++;
        }

        // Normalize commands
        if (action == "dblclick") { action = "click"; clickCount = 2; }
        if (action == "scroll-into-view") action = "scroll_into_view";
        if (action == "app" || action == "launch") action = "app_launch";
        // "app launch calc" → selector="launch", value="calc". Fix: move value to selector
        if (action == "app_launch" && selector == "launch" && value != null)
        {
            selector = value;
            value = null;
            text = null;
        }

        // Normalize get/is subcommands
        if (action == "get" && selector != null)
        {
            var sub = selector.ToLowerInvariant();
            action = sub switch
            {
                "text" => "get_text",
                "value" => "get_value",
                "box" => "get_box",
                "count" => "get_count",
                _ => action
            };
            if (action != "get") { selector = value; value = null; }
        }
        if (action == "is" && selector != null)
        {
            var sub = selector.ToLowerInvariant();
            action = sub switch
            {
                "visible" => "is_visible",
                "enabled" => "is_enabled",
                "checked" => "is_checked",
                _ => action
            };
            if (action != "is") { selector = value; value = null; }
        }

        // Handle local-only snapshot (standalone, no daemon needed)
        if (action == "snapshot")
        {
            AutomationElement? root = null;
            if (processId.HasValue)
            {
                root = FindWindowByPid(processId.Value);
            }
            else if (windowRef != null)
            {
                var reg = new WindowRegistry();
                var entry = reg.Get(windowRef);
                if (entry != null)
                    root = AutomationElement.FromHandle((nint)entry.Hwnd);
            }

            if (root != null)
            {
                SnapshotAndPrint(root, interactive, false, depth, rawView, compact);
                return 0;
            }
        }

        var request = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["id"] = Guid.NewGuid().ToString("N")[..8],
        };

        if (processId.HasValue) request["processId"] = processId;
        if (hwnd.HasValue) request["hwnd"] = hwnd;
        if (windowRef != null) request["windowRef"] = windowRef;
        if (selector != null) request["ref"] = selector;
        if (interactive) request["interactive"] = true;
        if (compact) request["compact"] = true;
        if (rawView) request["raw"] = true;
        if (timeout.HasValue) request["timeout"] = timeout;

        switch (action)
        {
            case "click":
                request["button"] = button;
                request["clickCount"] = clickCount;
                break;
            case "fill":
                request["value"] = value ?? "";
                break;
            case "type":
                request["text"] = text ?? "";
                request["delay"] = delay;
                break;
            case "press":
                request["key"] = key ?? (selector ?? throw new ArgumentException("key required"));
                break;
            case "scroll":
                if (direction != null)
                {
                    request["direction"] = direction;
                    request["amount"] = amount ?? 300.0;
                }
                break;
            case "screenshot":
                break;
            case "close":
                break;
            case "app_launch":
                request["url"] = selector ?? "";
                break;
        }

        try
        {
            var client = new DaemonClient(port);
            var response = await client.SendAsync(request);
            return await HandleResponseAsync(response, action, screenshotPath, jsonMode);
        }
        catch (SocketException)
        {
            // Auto-start daemon
            Console.Error.Write("Starting daemon... ");
            var started = await EnsureDaemonAsync(port);
            if (!started)
            {
                Console.Error.WriteLine("FAILED");
                Console.Error.WriteLine("Daemon could not be started. Run 'SeelessUIA daemon' manually.");
                return 1;
            }
            Console.Error.WriteLine("OK");

            // Retry the request
            try
            {
                var client2 = new DaemonClient(port);
                var response2 = await client2.SendAsync(request);
                // ... re-run the response handling logic
                return await HandleResponseAsync(response2, action, screenshotPath);
            }
            catch (Exception ex2)
            {
                Console.Error.WriteLine($"Error after daemon start: {ex2.Message}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<bool> EnsureDaemonAsync(int port)
    {
        var exePath = Environment.ProcessPath ?? "SeelessUIA.exe";
        var psi = new ProcessStartInfo(exePath, $"daemon --port {port}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var proc = Process.Start(psi);
        if (proc == null) return false;

        // Wait up to 5 seconds for daemon to be ready
        for (int i = 0; i < 50; i++)
        {
            await Task.Delay(100);
            if (proc.HasExited) return false;
            try
            {
                using var tcp = new TcpClient();
                await tcp.ConnectAsync("127.0.0.1", port);
                return true;
            }
            catch { }
        }
        return false;
    }

    private static async Task<int> HandleResponseAsync(JsonElement response, string action, string? screenshotPath, bool jsonMode = false)
    {
        if (response.TryGetProperty("success", out var success) && success.GetBoolean())
        {
            if (response.TryGetProperty("data", out var data))
            {
                // --json mode: print raw JSON
                if (jsonMode)
                {
                    Console.WriteLine(response.ToString());
                    return 0;
                }

                if (action == "screenshot" && screenshotPath != null)
                {
                    var b64 = data.GetProperty("screenshot").GetString() ?? "";
                    File.WriteAllBytes(screenshotPath, Convert.FromBase64String(b64));
                    Console.WriteLine($"Screenshot saved: {screenshotPath}");
                }
                else if (action == "windows" && data.TryGetProperty("windows", out var winList))
                {
                    string? active = null;
                    if (data.TryGetProperty("active", out var a))
                        active = a.GetString();

                    Console.WriteLine("Windows:");
                    Console.WriteLine($"  {"Ref",-6} {"Process",-20} Title");
                    Console.WriteLine("  " + new string('-', 60));
                    foreach (var w in winList.EnumerateArray())
                    {
                        var refId = w.GetProperty("refId").GetString() ?? "";
                        var pName = w.GetProperty("processName").GetString() ?? "";
                        var title = w.GetProperty("title").GetString() ?? "";
                        var marker = refId == active ? "->" : "  ";
                        Console.WriteLine($"{marker} {refId,-4} {pName,-20} {title}");
                    }
                }
                else if (action == "app_launch" && data.TryGetProperty("refId", out var launchRef))
                {
                    Console.WriteLine($"{launchRef.GetString()}");
                }
                else if (action == "snapshot" && data.TryGetProperty("snapshot", out var snap))
                {
                    Console.WriteLine(snap.GetString());
                }
                else if (action == "get_text" && data.TryGetProperty("text", out var t))
                {
                    Console.WriteLine(t.GetString());
                }
                else if (action == "get_value" && data.TryGetProperty("value", out var v))
                {
                    Console.WriteLine(v.GetString());
                }
                else if (action == "get_box" && data.TryGetProperty("x", out var bx))
                {
                    Console.WriteLine($"x:{bx.GetDouble()} y:{data.GetProperty("y").GetDouble()} width:{data.GetProperty("width").GetDouble()} height:{data.GetProperty("height").GetDouble()}");
                }
                else if (action == "get_count" && data.TryGetProperty("count", out var c))
                {
                    Console.WriteLine(c.GetInt32());
                }
                else if (action == "is_visible" && data.TryGetProperty("visible", out var vis))
                {
                    Console.WriteLine(vis.GetBoolean());
                }
                else if (action == "is_enabled" && data.TryGetProperty("enabled", out var en))
                {
                    Console.WriteLine(en.GetBoolean());
                }
                else if (action == "is_checked" && data.TryGetProperty("checked", out var chk))
                {
                    Console.WriteLine(chk.GetBoolean());
                }
                else if (action == "wait" && data.TryGetProperty("appeared", out var ap))
                {
                    Console.WriteLine("true");
                }
                else
                {
                    Console.WriteLine(data.ToString());
                }
            }
            return 0;
        }
        else
        {
            var err = "unknown error";
            if (response.TryGetProperty("error", out var errorProp))
                err = errorProp.GetString() ?? err;
            Console.Error.WriteLine($"Error: {err}");
            return 1;
        }
    }

    // ── test ──────────────────────────────────────────────────────

    private static async Task RunTestAsync(string[] args)
    {
        string appName = "code";
        bool interactive = false;
        bool showRefs = false;
        bool rawView = false;
        int? depth = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--app" when i + 1 < args.Length: appName = args[++i]; break;
                case "-i": interactive = true; break;
                case "-r": showRefs = true; break;
                case "--raw": rawView = true; break;
                case "--depth" when i + 1 < args.Length: depth = int.Parse(args[++i]); break;
            }
        }

        var (exePath, friendlyName) = ResolveApp(appName);
        Console.Error.WriteLine($"Launching {friendlyName} ({exePath})...");

        var psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
        var process = Process.Start(psi)!;

        try
        {
            process.WaitForInputIdle(10000);
            await Task.Delay(3000);

            AutomationElement? window = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(500);
                window = FindWindowByPid(process.Id);
                if (window != null) break;
            }

            if (window == null)
            {
                Console.Error.WriteLine("Could not find UIA window after 10s.");
                return;
            }

            Console.Error.WriteLine($"Window: '{window.Current.Name}'\n");

            var refMap = new RefMap();
            var options = new SnapshotOptions
            {
                Interactive = interactive,
                Structured = !interactive,
                Depth = depth,
                ShowRefs = showRefs,
                RawView = rawView,
            };

            var pipeline = new SnapshotPipeline(options, refMap);
            var snapshot = pipeline.TakeSnapshot(window);

            Console.WriteLine(snapshot);

            if (showRefs && refMap.Count > 0)
                PrintRefs(refMap);
        }
        finally
        {
            try { process.Kill(); } catch { }
        }
    }

    // ── snapshot ──────────────────────────────────────────────────

    private static async Task RunSnapshotAsync(string[] args)
    {
        int? processId = null;
        int windowIndex = 0;
        long? hwnd = null;
        bool interactive = false;
        bool showRefs = false;
        bool allWindows = false;
        bool rawView = false;
        bool compact = false;
        int? depth = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid" when i + 1 < args.Length:
                    var pidArg = args[++i];
                    var colon = pidArg.IndexOf(':');
                    if (colon > 0)
                    {
                        processId = int.Parse(pidArg[..colon]);
                        windowIndex = int.Parse(pidArg[(colon + 1)..]);
                    }
                    else
                    {
                        processId = int.Parse(pidArg);
                    }
                    break;
                case "--hwnd" when i + 1 < args.Length: hwnd = long.Parse(args[++i]); break;
                case "-i": interactive = true; break;
                case "-c": compact = true; break;
                case "-r": showRefs = true; break;
                case "--all": allWindows = true; break;
                case "--raw": rawView = true; break;
                case "-d" when i + 1 < args.Length: depth = int.Parse(args[++i]); break;
                case "--depth" when i + 1 < args.Length: depth = int.Parse(args[++i]); break;
            }
        }

        AutomationElement? root = null;

        if (processId.HasValue)
        {
            if (allWindows)
            {
                var windows = FindAllWindowsByPid(processId.Value);
                if (windows.Count == 0)
                {
                    Console.Error.WriteLine($"No windows found for PID {processId}");
                    return;
                }
                Console.Error.WriteLine($"Found {windows.Count} window(s) for PID {processId}:");
                for (int i = 0; i < windows.Count; i++)
                {
                    var w = windows[i];
                    Console.Error.WriteLine($"  [{i}] '{w.Current.Name}'");
                }
                Console.Error.WriteLine();

                for (int i = 0; i < windows.Count; i++)
                {
                    var w = windows[i];
                    Console.Error.WriteLine($"--- Window {i}: '{w.Current.Name}' ---");
                    SnapshotAndPrint(w, interactive, showRefs, depth, rawView, compact);
                    Console.WriteLine();
                }
                return;
            }

            var allWins = FindAllWindowsByPid(processId.Value);
            if (allWins.Count == 0)
            {
                Console.Error.WriteLine($"No windows found for PID {processId}");
                return;
            }
            if (windowIndex >= allWins.Count)
            {
                Console.Error.WriteLine($"Window index {windowIndex} out of range (0-{allWins.Count - 1})");
                return;
            }
            root = allWins[windowIndex];
        }
        else if (hwnd.HasValue)
        {
            root = AutomationElement.FromHandle((nint)hwnd.Value);
        }

        if (root == null)
        {
            Console.Error.WriteLine("No window found. Use --pid <id>, --pid <id> --all, or --hwnd <hex>.");
            return;
        }

        SnapshotAndPrint(root, interactive, showRefs, depth, rawView, compact);
    }

    private static void SnapshotAndPrint(AutomationElement root, bool interactive,
                                          bool showRefs, int? depth, bool rawView = false,
                                          bool compact = false)
    {
        Console.Error.WriteLine($"Window: '{root.Current.Name}'");
        var refMap = new RefMap();
        var options = new SnapshotOptions
        {
            Interactive = interactive,
            Structured = !interactive,
            Depth = depth,
            ShowRefs = showRefs,
            RawView = rawView,
            Compact = compact,
        };

        var pipeline = new SnapshotPipeline(options, refMap);
        var snapshot = pipeline.TakeSnapshot(root);

        Console.WriteLine(snapshot);

        if (showRefs && refMap.Count > 0)
            PrintRefs(refMap);
    }

    // ── windows ───────────────────────────────────────────────────

    private static void ListWindows()
    {
        var wm = new WindowManager();
        var windows = wm.ListWindows();

        var byPid = windows.GroupBy(w => w.ProcessId).OrderBy(g => g.Key);
        Console.WriteLine($"{"PID",-8} {"[n]",-4} {"Process",-22} {"HWND",-12} Title");
        Console.WriteLine(new string('-', 90));
        foreach (var group in byPid)
        {
            var list = group.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var w = list[i];
                Console.WriteLine($"{w.ProcessId,-8} [{i}]   {w.ProcessName,-22} 0x{w.Hwnd.ToInt64():X8}  {w.Title}");
            }
        }
    }

    // ── daemon ────────────────────────────────────────────────────

    private static async Task<int> RunDaemonAsync(string[] args)
    {
        int port = 9222;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
                port = int.Parse(args[++i]);
        }

        var daemon = new DaemonServer(port);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Console.Error.WriteLine("Shutting down...");
            Environment.Exit(0);
        };
        await daemon.RunAsync();
        return 0;
    }

    // ── helpers ───────────────────────────────────────────────────

    private static void PrintRefs(RefMap refMap)
    {
        Console.Error.WriteLine($"\n--- Refs ({refMap.Count}) ---");
        foreach (var (refId, entry) in refMap.EntriesSorted())
            Console.Error.WriteLine($"  {refId}: {entry.Role} \"{entry.Name}\"");
    }

    private static AutomationElement? FindWindowByPid(int pid)
    {
        var desktop = AutomationElement.RootElement;
        foreach (AutomationElement child in desktop.FindAll(
            TreeScope.Children,
            System.Windows.Automation.Condition.TrueCondition))
        {
            try { if (child.Current.ProcessId == pid) return child; } catch { }
        }

        try
        {
            var cond = new System.Windows.Automation.PropertyCondition(
                AutomationElement.ProcessIdProperty, pid);
            var el = desktop.FindFirst(TreeScope.Descendants, cond);
            if (el != null) return el;
        }
        catch { }

        try
        {
            foreach (AutomationElement child in desktop.FindAll(
                TreeScope.Children,
                System.Windows.Automation.Condition.TrueCondition))
            {
                try
                {
                    if (!string.IsNullOrEmpty(child.Current.Name) && child.Current.IsEnabled)
                        return child;
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private static List<AutomationElement> FindAllWindowsByPid(int pid)
    {
        var result = new List<AutomationElement>();
        var desktop = AutomationElement.RootElement;
        foreach (AutomationElement child in desktop.FindAll(
            TreeScope.Children,
            System.Windows.Automation.Condition.TrueCondition))
        {
            try
            {
                if (child.Current.ProcessId == pid
                    && !string.IsNullOrEmpty(child.Current.Name))
                    result.Add(child);
            }
            catch { }
        }
        return result;
    }

    private static (string path, string name) ResolveApp(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "notepad" or "notepad.exe" => ("notepad.exe", "Notepad"),
            "calc" or "calculator" or "calc.exe" => ("calc.exe", "Calculator"),
            "cmd" or "cmd.exe" => ("cmd.exe", "Command Prompt"),
            "explorer" or "explorer.exe" => ("explorer.exe", "File Explorer"),
            "code" or "vscode" => FindInPath("code") is { } p ? (p, "VS Code") : ("code", "VS Code"),
            "opencode" => FindInPath("opencode") is { } p2 ? (p2, "OpenCode") : ("opencode", "OpenCode"),
            _ => (name, name),
        };
    }

    private static string? FindInPath(string exe)
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? [];
        foreach (var dir in paths)
        {
            var full = Path.Combine(dir, $"{exe}.cmd");
            if (File.Exists(full)) return full;
            full = Path.Combine(dir, $"{exe}.exe");
            if (File.Exists(full)) return full;
        }
        return null;
    }

    // ── calc-test: interaction closed-loop test ──────────────────

    private static async Task RunCalculatorTestAsync()
    {
        Console.Error.WriteLine("=== Interaction Closed-Loop Test ===");

        var psi = new ProcessStartInfo("calc.exe") { UseShellExecute = true };
        var process = Process.Start(psi)!;
        Thread.Sleep(3000);

        AutomationElement? window = null;
        foreach (var p in Process.GetProcessesByName("CalculatorApp"))
        {
            try
            {
                window = FindWindowByPid(p.Id);
                if (window != null) break;
            }
            catch { }
        }

        if (window == null)
        {
            Console.Error.WriteLine("FAIL: Could not find Calculator window");
            process.Kill();
            return;
        }
        Console.Error.WriteLine($"Window: '{window.Current.Name}'");

        var refMap = new RefMap();
        var opts = new SnapshotOptions { Interactive = true };
        var pipeline = new SnapshotPipeline(opts, refMap);
        var snap1 = pipeline.TakeSnapshot(window);

        Console.Error.WriteLine($"Initial snapshot: {snap1.Split('\n').Length} lines");
        Console.Error.WriteLine(snap1);

        var idToRef = new Dictionary<string, string>();
        foreach (var line in snap1.Split('\n'))
        {
            var refMatch = System.Text.RegularExpressions.Regex.Match(line, @"ref=(e\d+)");
            var autoMatch = System.Text.RegularExpressions.Regex.Match(line, @"automationId=([^\],\s]+)");
            if (refMatch.Success && autoMatch.Success)
                idToRef[autoMatch.Groups[1].Value] = refMatch.Groups[1].Value;
        }

        var buttons = new[] { "num5Button", "plusButton", "num3Button", "equalButton" };
        foreach (var btnId in buttons)
        {
            if (!idToRef.TryGetValue(btnId, out var refId))
            {
                Console.Error.WriteLine($"FAIL: No ref for automationId '{btnId}'");
                goto cleanup;
            }

            Console.Error.Write($"Clicking {btnId} ({refId})... ");
            try
            {
                var resolver = new ElementResolver(refMap, window);
                var executor = new Interaction.ActionExecutor(resolver);
                executor.Click(refId);
                Console.Error.WriteLine("OK");
                Thread.Sleep(300);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"FAIL: {ex.Message}");
            }
        }

        refMap.Clear();
        var snap2 = pipeline.TakeSnapshot(window);
        Console.Error.WriteLine($"\nVerification snapshot: {snap2.Split('\n').Length} lines");
        Console.Error.WriteLine(snap2);

        bool pass = snap2.Contains("8") || snap2.Contains("\"8\"");
        Console.Error.WriteLine(pass ? "\nPASS: Result confirmed" : "\nWARN: Could not verify '8'");

        cleanup:
        foreach (var p in Process.GetProcessesByName("CalculatorApp"))
            try { p.Kill(); } catch { }
    }

    private static void PrintHelp()
    {
        Console.Error.WriteLine("""
SeelessUIA - Windows UI Automation CLI

USAGE:
  seeless-uia <command> [options] [args]

CORE COMMANDS:
  seeless-uia windows [--verbose]
      List all visible windows with refs (w1, w2, ...).
      --verbose  Show HWND and PID columns.

  seeless-uia snapshot [w1] [-i] [-c] [-r] [--raw] [-d <n>]
      Take snapshot of a window's accessibility tree.
      w1          Target window ref (optional; uses active window if omitted).
                  If daemon is running, snapshot is routed through daemon.
      -i          Interactive mode (flat, ref-only)
      -c          Compact mode (remove empty structural elements)
      -r          Include refs list at end
      --raw       Use RawView (include hidden MSAA-only elements)
      -d <n>      Limit tree depth

  seeless-uia window w2
      Switch active window to w2.

  seeless-uia app launch <name>
      Launch an application and register its window. Returns wN ref.
      Apps: notepad, calc, cmd, code, opencode

INTERACTION COMMANDS (use active window unless wN specified):
  seeless-uia click [w1] <sel>      [--button left|right|middle] [--click-count 1|2]
  seeless-uia dblclick [w1] <sel>
  seeless-uia fill [w1] <sel> <text>
  seeless-uia type [w1] <sel> <text>   [--delay <ms>]
  seeless-uia press <key>
  seeless-uia hover [w1] <sel>
  seeless-uia scroll [w1] <dir>    [--amount <px>]
  seeless-uia check [w1] <sel>
  seeless-uia uncheck [w1] <sel>
  seeless-uia focus [w1] <sel>
  seeless-uia expand [w1] <sel>
  seeless-uia collapse [w1] <sel>
  seeless-uia select [w1] <sel>
  seeless-uia scrollintoview [w1] <sel>
  seeless-uia screenshot [w1] [path]
  seeless-uia close [w1]

GET / IS / WAIT:
  seeless-uia get text <sel>           [w1]
  seeless-uia get value <sel>          [w1]
  seeless-uia get box <sel>            [w1]
  seeless-uia get count <sel>          [w1]
  seeless-uia is visible <sel>         [w1]
  seeless-uia is enabled <sel>         [w1]
  seeless-uia is checked <sel>         [w1]
  seeless-uia wait <sel>               [w1]  [--timeout <ms>]

OPTIONS:
  --json       Machine-readable JSON output for all commands

DAEMON:
  seeless-uia daemon [--port <port>]
      Start daemon manually. Interaction commands auto-start it.

NOTES:
  The daemon auto-starts on first interaction command.
  Window refs (w1, w2) are assigned by the 'windows' command.
  Without a window ref, commands use the last active window.
  Press supports "Control+a", "Shift+Enter" chord notation.

UNIMPLEMENTED (compared to agent-browser):
  get text/value/box/attr/count       - element property queries
  is visible/enabled/checked          - element state checks
  find role/text/label/placeholder    - semantic locators
  wait <sel>/<ms>/--text/--url        - wait for conditions
  drag <src> <tgt>                    - drag and drop
  highlight <sel>                     - visual highlight
  keyboard type/inserttext            - raw keyboard input
  keydown/keyup <key>                 - key hold/release
  mouse move/down/up/wheel            - raw mouse control
""");
    }
}
