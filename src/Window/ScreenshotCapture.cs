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
}
