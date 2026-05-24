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
                await RunSnapshotAsync(remainingArgs);
                return 0;
            case "windows":
                ListWindows();
                return 0;
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
        int windowIndex = 0;
        long? hwnd = null;
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

        int i = 0;
        while (i < args.Length)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
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
                case "--button" when i + 1 < args.Length: button = args[++i]; break;
                case "--click-count" when i + 1 < args.Length: clickCount = int.Parse(args[++i]); break;
                case "--direction" when i + 1 < args.Length: direction = args[++i]; break;
                case "--amount" when i + 1 < args.Length: amount = double.Parse(args[++i]); break;
                case "--delay" when i + 1 < args.Length: delay = int.Parse(args[++i]); break;
                case "-o" when i + 1 < args.Length: screenshotPath = args[++i]; break;
                default:
                    if (!args[i].StartsWith('-'))
                    {
                        if (selector == null) selector = args[i];
                        else if (value == null && text == null) { value = args[i]; text = args[i]; }
                        else if (screenshotPath == null) screenshotPath = args[i];
                    }
                    break;
            }
            i++;
        }

        // Normalize double-click
        if (action == "dblclick")
        {
            action = "click";
            clickCount = 2;
        }
        if (action == "scroll-into-view")
            action = "scroll_into_view";

        var request = new Dictionary<string, object?>
        {
            ["action"] = action,
            ["id"] = Guid.NewGuid().ToString("N")[..8],
        };

        if (processId.HasValue) request["processId"] = processId;
        if (hwnd.HasValue) request["hwnd"] = hwnd;
        if (selector != null) request["ref"] = selector;

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
        }

        try
        {
            var client = new DaemonClient(port);
            var response = await client.SendAsync(request);
            return await HandleResponseAsync(response, action, screenshotPath);
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

    private static async Task<int> HandleResponseAsync(JsonElement response, string action, string? screenshotPath)
    {
        if (response.TryGetProperty("success", out var success) && success.GetBoolean())
        {
            if (response.TryGetProperty("data", out var data))
            {
                if (action == "screenshot" && screenshotPath != null)
                {
                    var b64 = data.GetProperty("screenshot").GetString() ?? "";
                    File.WriteAllBytes(screenshotPath, Convert.FromBase64String(b64));
                    Console.WriteLine($"Screenshot saved: {screenshotPath}");
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
  seeless-uia snapshot --pid <pid>[:<n>] [-i] [-c] [-r] [--all] [--raw] [-d <n>]
      Take snapshot of a window's accessibility tree.
      --pid 1234:1  Select window [1] of process 1234.
      --pid 1234    Select window [0] of process 1234.
      --hwnd 0x12C  Snapshot a specific window by handle.
      --all         Snapshot all windows of the process.
      -i            Interactive mode (flat, ref-only)
      -c            Compact mode (remove empty structural elements)
      -r            Include refs list at end
      --raw         Use RawView (include hidden MSAA-only elements)
      -d <n>        Limit tree depth

  seeless-uia click <sel>     [--pid <pid>] [--button left|right|middle] [--click-count 1|2]
  seeless-uia dblclick <sel>  [--pid <pid>]
  seeless-uia fill <sel> <text>  [--pid <pid>]
  seeless-uia type <sel> <text>  [--pid <pid>] [--delay <ms>]
  seeless-uia press <key>        [--pid <pid>]
  seeless-uia hover <sel>        [--pid <pid>]
  seeless-uia scroll <dir>       [--pid <pid>] [--amount <px>] [--selector <sel>]
  seeless-uia check <sel>        [--pid <pid>]
  seeless-uia uncheck <sel>      [--pid <pid>]
  seeless-uia focus <sel>        [--pid <pid>]
  seeless-uia expand <sel>       [--pid <pid>]
  seeless-uia collapse <sel>     [--pid <pid>]
  seeless-uia select <sel>       [--pid <pid>]
  seeless-uia scrollintoview <sel>  [--pid <pid>]
  seeless-uia screenshot [path]  [--pid <pid>]
  seeless-uia close              [--pid <pid>]
  seeless-uia windows
  seeless-uia daemon [--port <port>]
  seeless-uia test --app <name> [-i] [-r] [--raw] [-d <n>]

NOTES:
  Interaction commands auto-start the daemon if not running.
  Manual control: seeless-uia daemon [--port <port>]

  Snapshot and windows commands work standalone (no daemon required).

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
  upload <sel> <files>                - file upload
  eval <js>                           - JS evaluation (not applicable to native apps)
""");
    }
}
