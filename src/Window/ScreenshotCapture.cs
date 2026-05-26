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

    private const int PW_CLIENTONLY = 0x00000001;

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
    /// </summary>
    public string CaptureScreenshot(AutomationElement windowElement)
    {
        var hwnd = (nint)windowElement.Current.NativeWindowHandle;

        if (!GetWindowRect(hwnd, out var rect))
            throw new InvalidOperationException("Failed to get window rect");

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("Window has zero size");

        var hdcWindow = GetWindowDC(hwnd);
        var hdcMem = CreateCompatibleDC(hdcWindow);
        var hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
        var hOld = SelectObject(hdcMem, hBitmap);

        PrintWindow(hwnd, hdcMem, PW_CLIENTONLY);

        using var bitmap = Image.FromHbitmap(hBitmap);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var bytes = ms.ToArray();

        // Cleanup
        SelectObject(hdcMem, hOld);
        DeleteObject(hBitmap);
        DeleteDC(hdcMem);
        ReleaseDC(hwnd, hdcWindow);

        return Convert.ToBase64String(bytes);
    }

    public string CaptureFullScreenshot(AutomationElement windowElement)
    {
        var hwnd = (nint)windowElement.Current.NativeWindowHandle;
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
