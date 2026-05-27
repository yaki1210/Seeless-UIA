using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace SeelessUIA.Window;

/// <summary>
/// Screenshot capture for windows.
/// </summary>
public class ScreenshotCapture
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hWnd, nint hdcBlt, int nFlags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    private const int PW_RENDERFULLCONTENT = 0x00000002;
    private const int SW_RESTORE = 9;
    private const int SW_MINIMIZE = 6;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Take a screenshot of a window and return base64-encoded PNG.
    /// First tries silent capture via DWM buffer (PW_RENDERFULLCONTENT) without
    /// disturbing the user. If the result is all-black (minimized/offscreen fail)
    /// or an exception occurs, falls back to restoring and foregrounding the window.
    /// </summary>
    public string CaptureScreenshot(AutomationElement windowElement)
    {
        var hwnd = (nint)windowElement.Current.NativeWindowHandle;

        var isMinimized = IsIconic(hwnd);
        if (!isMinimized)
        {
            try
            {
                if (windowElement.TryGetCurrentPattern(System.Windows.Automation.WindowPattern.Pattern, out var wp))
                {
                    var wps = (System.Windows.Automation.WindowPattern)wp;
                    isMinimized = wps.Current.WindowVisualState == System.Windows.Automation.WindowVisualState.Minimized;
                }
            }
            catch { }
        }

        if (!isMinimized)
        {
            // 1st attempt: silent, no window manipulation
            var (success, base64, isAllBlack) = TryCaptureWindow(hwnd);

            if (success && !isAllBlack)
                return base64;
        }

        // 2nd attempt: restore and bring to foreground
        Console.Error.WriteLine("[screenshot] Window is minimized or off-screen — restoring to foreground...");
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
        Thread.Sleep(500);

        var (s2, b64, _) = TryCaptureWindow(hwnd);
        return b64;
    }

    private static (bool success, string base64, bool isAllBlack) TryCaptureWindow(nint hwnd)
    {
        try
        {
            if (!GetWindowRect(hwnd, out var rect))
                return (false, string.Empty, false);

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
                return (false, string.Empty, false);

            var hdcWindow = GetWindowDC(hwnd);
            var hdcMem = CreateCompatibleDC(hdcWindow);
            var hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
            var hOld = SelectObject(hdcMem, hBitmap);

            PrintWindow(hwnd, hdcMem, PW_RENDERFULLCONTENT);

            using var bitmap = Image.FromHbitmap(hBitmap);
            bool allBlack = IsAllBlack(bitmap);

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var bytes = ms.ToArray();

            // Cleanup
            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(hwnd, hdcWindow);

            return (true, Convert.ToBase64String(bytes), allBlack);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[screenshot] Silent capture failed: {ex.Message}");
            return (false, string.Empty, false);
        }
    }

    private static bool IsAllBlack(Bitmap bitmap)
    {
        // Sample the first row, last row, and middle row — full scan is too slow
        int h = bitmap.Height;
        int w = bitmap.Width;
        int[] sampleRows = { 0, h / 2, h - 1 };

        foreach (int y in sampleRows)
        {
            if (y < 0 || y >= h) continue;
            for (int x = 0; x < w; x += 10) // every 10th pixel
            {
                var p = bitmap.GetPixel(x, y);
                if (p.R != 0 || p.G != 0 || p.B != 0)
                    return false;
            }
        }
        return true;
    }

    public string CaptureFullScreenshot(AutomationElement windowElement)
    {
        var hwnd = (nint)windowElement.Current.NativeWindowHandle;

        var isMinimized = IsIconic(hwnd);
        if (!isMinimized)
        {
            try
            {
                if (windowElement.TryGetCurrentPattern(System.Windows.Automation.WindowPattern.Pattern, out var wp))
                {
                    var wps = (System.Windows.Automation.WindowPattern)wp;
                    isMinimized = wps.Current.WindowVisualState == System.Windows.Automation.WindowVisualState.Minimized;
                }
            }
            catch { }
        }

        if (isMinimized)
        {
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
            Thread.Sleep(500);
        }

        if (!GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("Failed to get window rect");

        int viewW = rect.Right - rect.Left;
        int viewH = rect.Bottom - rect.Top;

        int totalH = viewH;
        System.Windows.Automation.ScrollPattern? scrollPattern = null;
        try
        {
            var all = windowElement.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
            foreach (AutomationElement el in all)
            {
                try
                {
                    var sp = el.GetCurrentPattern(System.Windows.Automation.ScrollPattern.Pattern)
                        as System.Windows.Automation.ScrollPattern;
                    if (sp?.Current.VerticallyScrollable == true)
                    {
                        scrollPattern = sp;
                        totalH = (int)(sp.Current.VerticalViewSize);
                        break;
                    }
                }
                catch { }
            }
        }
        catch { }

        if (totalH <= viewH || scrollPattern == null)
            return CaptureScreenshot(windowElement);

        int pages = (totalH + viewH - 1) / viewH;
        var images = new List<Bitmap>();

        for (int i = 0; i < pages; i++)
        {
            var b64 = CaptureScreenshot(windowElement);
            using var ms = new MemoryStream(Convert.FromBase64String(b64));
            images.Add(new Bitmap(ms));

            if (i < pages - 1)
            {
                try { scrollPattern.ScrollVertical(System.Windows.Automation.ScrollAmount.LargeIncrement); Thread.Sleep(100); }
                catch { break; }
            }
        }

        var full = new Bitmap(viewW, totalH);
        using var g = Graphics.FromImage(full);
        int offsetY = 0;
        for (int i = 0; i < images.Count && offsetY < totalH; i++)
        {
            int h = Math.Min(images[i].Height, totalH - offsetY);
            g.DrawImage(images[i], 0, offsetY, viewW, h);
            offsetY += h;
            images[i].Dispose();
        }

        using var resultMs = new MemoryStream();
        full.Save(resultMs, ImageFormat.Png);
        full.Dispose();
        return Convert.ToBase64String(resultMs.ToArray());
    }
}
