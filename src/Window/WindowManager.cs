using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace SeelessUIA.Window;

/// <summary>
/// Window enumeration, activation, and closing.
/// </summary>
public class WindowManager
{
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    private const uint WM_CLOSE = 0x0010;

    /// <summary>
    /// Represents a top-level window.
    /// </summary>
    public class WindowInfo
    {
        public nint Hwnd { get; set; }
        public string Title { get; set; } = "";
        public uint ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public bool IsVisible { get; set; }
    }

    /// <summary>
    /// Enumerate all visible top-level windows.
    /// Corresponds to agent-browser's tab_list.
    /// </summary>
    public List<WindowInfo> ListWindows()
    {
        var windows = new List<WindowInfo>();

        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            var sb = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, sb, length + 1);
            var title = sb.ToString();

            if (string.IsNullOrWhiteSpace(title))
                return true;

            GetWindowThreadProcessId(hWnd, out uint processId);

            string processName = "";
            try
            {
                var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch { }

            windows.Add(new WindowInfo
            {
                Hwnd = hWnd,
                Title = title,
                ProcessId = processId,
                ProcessName = processName,
                IsVisible = true,
            });

            return true;
        }, nint.Zero);

        return windows;
    }

    /// <summary>
    /// Activate/focus a window by HWND.
    /// </summary>
    public void FocusWindow(nint hwnd)
    {
        SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Close a window by HWND.
    /// </summary>
    public void CloseWindow(nint hwnd)
    {
        PostMessage(hwnd, WM_CLOSE, nint.Zero, nint.Zero);
    }

    /// <summary>
    /// Find a top-level window by process ID.
    /// Returns the first matching AutomationElement.
    /// </summary>
    public AutomationElement? FindWindowByProcessId(int processId)
    {
        var desktop = AutomationElement.RootElement;
        var condition = new PropertyCondition(
            AutomationElement.ProcessIdProperty, processId);

        // Walk top-level windows
        var windows = desktop.FindAll(TreeScope.Children, Condition.TrueCondition);
        foreach (AutomationElement window in windows)
        {
            try
            {
                if (window.Current.ProcessId == processId
                    && !string.IsNullOrEmpty(window.Current.Name))
                {
                    return window;
                }
            }
            catch { }
        }

        // Fallback: any element with this ProcessId
        var element = desktop.FindFirst(TreeScope.Descendants, condition);
        if (element != null)
            return element;

        // Walk top-level windows for the process
        foreach (AutomationElement window in windows)
        {
            try
            {
                if (window.Current.ProcessId == processId)
                    return window;
            }
            catch { }
        }

        // Win32 fallback: use EnumWindows to find HWND, then convert to UIA element
        // (UIA desktop enumeration sometimes misses freshly created windows)
        nint foundHwnd = nint.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            GetWindowThreadProcessId(hWnd, out uint wndProcessId);
            if (wndProcessId == processId)
            {
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    foundHwnd = hWnd;
                    return false;
                }
            }
            return true;
        }, nint.Zero);

        if (foundHwnd != nint.Zero)
        {
            try
            {
                return AutomationElement.FromHandle(foundHwnd);
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Find a top-level window by HWND.
    /// </summary>
    public AutomationElement? FindWindowByHwnd(long hwnd)
    {
        return AutomationElement.FromHandle((nint)hwnd);
    }
}
