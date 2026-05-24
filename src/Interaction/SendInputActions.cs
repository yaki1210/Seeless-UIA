using System.Runtime.InteropServices;
using System.Windows;
using SeelessUIA.Element;

namespace SeelessUIA.Interaction;

/// <summary>
/// Fallback action execution via Win32 SendInput.
/// Used when UIA Patterns are unavailable on a target element.
/// Corresponds to CDP mouse/keyboard event dispatch in agent-browser's interaction.rs.
/// </summary>
public unsafe class SendInputActions
{
    private readonly ElementResolver _resolver;

    public SendInputActions(ElementResolver resolver)
    {
        _resolver = resolver;
    }

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;
    private const int MOUSEEVENTF_MOVE = 0x0001;
    private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const int MOUSEEVENTF_LEFTUP = 0x0004;
    private const int MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const int MOUSEEVENTF_RIGHTUP = 0x0010;
    private const int MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const int MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const int MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const int MOUSEEVENTF_WHEEL = 0x0800;
    private const int KEYEVENTF_KEYDOWN = 0x0000;
    private const int KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public MOUSEKEYBDHARDWAREUNION u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct MOUSEKEYBDHARDWAREUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public int dwFlags;
        public int time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public short wVk;
        public short wScan;
        public int dwFlags;
        public int time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public void Click(string selectorOrRef, string button = "left", int clickCount = 1)
    {
        var (x, y) = _resolver.ResolveCenter(selectorOrRef);
        int screenX = (int)Math.Round(x);
        int screenY = (int)Math.Round(y);

        var (downFlag, upFlag) = button.ToLowerInvariant() switch
        {
            "right" => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "middle" => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        int total = 1 + clickCount + clickCount;
        var inputs = new INPUT[total];
        int idx = 0;

        inputs[idx++] = CreateMouseInput(screenX, screenY, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE, 0);

        for (int i = 0; i < clickCount; i++)
        {
            inputs[idx++] = CreateMouseInput(screenX, screenY, downFlag, 0);
            inputs[idx++] = CreateMouseInput(screenX, screenY, upFlag, 0);
        }

        SendInput((uint)total, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Fill(string selectorOrRef, string text)
    {
        var element = _resolver.ResolveElement(selectorOrRef);
        try { element.SetFocus(); } catch { }
        Thread.Sleep(50);

        // Select all and clear before typing (matches agent-browser's fill 3-step flow)
        KeyDown(0x11); // VK_CONTROL
        PressKey(0x41); // 'A' (Select All)
        KeyUp(0x11);
        Thread.Sleep(20);
        PressKey(0x2E); // VK_DELETE (clear)
        Thread.Sleep(20);

        TypeText(text, 0);
    }

    /// <summary>
    /// Type text character by character (with optional delay).
    /// Corresponds to type_text_into_active_context (interaction.rs:208-274).
    /// </summary>
    public void TypeText(string text, int delayMs = 0)
    {
        foreach (char c in text)
        {
            if (c == '\n' || c == '\r')
            {
                PressKey(0x0D); // VK_RETURN
            }
            else if (c == '\t')
            {
                PressKey(0x09); // VK_TAB
            }
            else
            {
                short vk = VkKeyScan(c);
                byte keyCode = (byte)(vk & 0xFF);
                bool needShift = (vk & 0x100) != 0;

                if (needShift)
                    KeyDown(0x10); // VK_SHIFT down

                PressKey(keyCode);

                if (needShift)
                    KeyUp(0x10); // VK_SHIFT up
            }

            if (delayMs > 0)
                Thread.Sleep(delayMs);
        }
    }

    /// <summary>
    /// Hover the mouse over an element.
    /// Corresponds to hover via CDP mouseMoved (interaction.rs:48-81).
    /// </summary>
    public void Hover(string selectorOrRef)
    {
        var (x, y) = _resolver.ResolveCenter(selectorOrRef);

        var inputs = new INPUT[1];
        inputs[0] = CreateMouseInput(
            (int)Math.Round(x),
            (int)Math.Round(y),
            MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
            0);

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Press and release a key (keydown + keyup pair).
    /// </summary>
    public void PressKey(short vkCode)
    {
        uint scanCode = MapVirtualKey((uint)vkCode, 0);

        var inputs = new INPUT[2];

        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vkCode;
        inputs[0].u.ki.wScan = (short)scanCode;
        inputs[0].u.ki.dwFlags = KEYEVENTF_KEYDOWN;

        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = vkCode;
        inputs[1].u.ki.wScan = (short)scanCode;
        inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Press a key down without releasing (for modifier hold).
    /// </summary>
    public void KeyDown(short vkCode)
    {
        uint scanCode = MapVirtualKey((uint)vkCode, 0);

        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vkCode;
        inputs[0].u.ki.wScan = (short)scanCode;
        inputs[0].u.ki.dwFlags = KEYEVENTF_KEYDOWN;

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Release a key (for modifier release).
    /// </summary>
    public void KeyUp(short vkCode)
    {
        uint scanCode = MapVirtualKey((uint)vkCode, 0);

        var inputs = new INPUT[1];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = vkCode;
        inputs[0].u.ki.wScan = (short)scanCode;
        inputs[0].u.ki.dwFlags = KEYEVENTF_KEYUP;

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Scroll by mouse wheel delta.
    /// Positive = scroll up, negative = scroll down.
    /// </summary>
    public void MouseWheel(int delta)
    {
        var inputs = new INPUT[1];
        inputs[0] = new INPUT
        {
            type = INPUT_MOUSE,
            u = new MOUSEKEYBDHARDWAREUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = 0,
                    dy = 0,
                    mouseData = delta,
                    dwFlags = MOUSEEVENTF_WHEEL,
                    time = 0,
                    dwExtraInfo = nint.Zero,
                }
            }
        };

        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT CreateMouseInput(int x, int y, int flags, int mouseData)
    {
        return new INPUT
        {
            type = INPUT_MOUSE,
            u = new MOUSEKEYBDHARDWAREUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = x * 65535 / GetSystemMetrics(0),
                    dy = y * 65535 / GetSystemMetrics(1),
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = nint.Zero,
                }
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
