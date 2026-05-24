using System.Windows.Automation;
using SeelessUIA.Element;

namespace SeelessUIA.Interaction;

public class ActionExecutor
{
    private readonly ElementResolver _resolver;
    private readonly PatternActions _pattern;
    private readonly SendInputActions _sendInput;

    public ActionExecutor(ElementResolver resolver)
    {
        _resolver = resolver;
        _pattern = new PatternActions(resolver);
        _sendInput = new SendInputActions(resolver);
    }

    public void Click(string selectorOrRef, string button = "left", int clickCount = 1)
    {
        try { _pattern.Click(selectorOrRef); return; } catch { }
        _sendInput.Click(selectorOrRef, button, clickCount);
    }

    public void Fill(string selectorOrRef, string value)
    {
        try { _pattern.Fill(selectorOrRef, value); return; } catch { }
        _sendInput.Fill(selectorOrRef, value);
    }

    public void Type(string selectorOrRef, string text, int delayMs = 0)
    {
        var element = _resolver.ResolveElement(selectorOrRef);
        try { element.SetFocus(); } catch { }
        Thread.Sleep(50);
        _sendInput.TypeText(text, delayMs);
    }

    public void Hover(string selectorOrRef)
    {
        _sendInput.Hover(selectorOrRef);
    }

    public void Toggle(string selectorOrRef)
    {
        try { _pattern.Toggle(selectorOrRef); return; } catch { }
        _sendInput.Click(selectorOrRef);
    }

    public void Check(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        ToggleState? currentState = null;
        try
        {
            if (PatternActions.TryGetTogglePattern(element, out var tp))
                currentState = tp.Current.ToggleState;
        }
        catch { }

        if (currentState == ToggleState.On)
            return;

        bool patternWorked = false;
        try
        {
            if (PatternActions.TryGetTogglePattern(element, out var tp))
            {
                tp.Toggle();
                patternWorked = true;
            }
        }
        catch { }

        if (patternWorked)
        {
            try
            {
                if (PatternActions.TryGetTogglePattern(element, out var tp))
                {
                    if (tp.Current.ToggleState == ToggleState.On)
                        return;
                }
            }
            catch { }
        }

        _sendInput.Click(selectorOrRef);

        try
        {
            if (PatternActions.TryGetTogglePattern(element, out var tp))
            {
                if (tp.Current.ToggleState != ToggleState.On)
                    tp.Toggle();
            }
        }
        catch { }
    }

    public void Uncheck(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        ToggleState? currentState = null;
        try
        {
            if (PatternActions.TryGetTogglePattern(element, out var tp))
                currentState = tp.Current.ToggleState;
        }
        catch { }

        if (currentState == ToggleState.Off)
            return;

        bool patternWorked = false;
        try
        {
            if (PatternActions.TryGetTogglePattern(element, out var tp))
            {
                tp.Toggle();
                patternWorked = true;
            }
        }
        catch { }

        if (patternWorked)
        {
            try
            {
                if (PatternActions.TryGetTogglePattern(element, out var tp))
                {
                    if (tp.Current.ToggleState == ToggleState.Off)
                        return;
                }
            }
            catch { }
        }

        _sendInput.Click(selectorOrRef);

        try
        {
            if (PatternActions.TryGetTogglePattern(element, out var tp))
            {
                if (tp.Current.ToggleState != ToggleState.Off)
                    tp.Toggle();
            }
        }
        catch { }
    }

    public void Select(string selectorOrRef)
    {
        try { _pattern.Select(selectorOrRef); return; } catch { }
        _sendInput.Click(selectorOrRef);
    }

    public void Focus(string selectorOrRef)
    {
        _pattern.Focus(selectorOrRef);
    }

    public void Expand(string selectorOrRef)
    {
        _pattern.Expand(selectorOrRef);
    }

    public void Collapse(string selectorOrRef)
    {
        _pattern.Collapse(selectorOrRef);
    }

    public void ScrollIntoView(string selectorOrRef)
    {
        _pattern.ScrollIntoView(selectorOrRef);
    }

    public void Scroll(string selectorOrRef, double horizontalPercent, double verticalPercent)
    {
        _pattern.Scroll(selectorOrRef, horizontalPercent, verticalPercent);
    }

    public void Press(string key)
    {
        var (actualKey, modifiers) = ParseKeyChord(key);

        if ((modifiers & 1) != 0) _sendInput.KeyDown(0x12);
        if ((modifiers & 2) != 0) _sendInput.KeyDown(0x11);
        if ((modifiers & 4) != 0) _sendInput.KeyDown(0x5B);
        if ((modifiers & 8) != 0) _sendInput.KeyDown(0x10);

        if (actualKey.Length == 1)
        {
            _sendInput.TypeText(actualKey, 0);
        }
        else
        {
            short vk = KeyNameToVk(actualKey);
            _sendInput.PressKey(vk);
        }

        if ((modifiers & 8) != 0) _sendInput.KeyUp(0x10);
        if ((modifiers & 4) != 0) _sendInput.KeyUp(0x5B);
        if ((modifiers & 2) != 0) _sendInput.KeyUp(0x11);
        if ((modifiers & 1) != 0) _sendInput.KeyUp(0x12);
    }

    internal static (string key, int modifiers) ParseKeyChord(string input)
    {
        var parts = input.Split('+');
        if (parts.Length < 2)
            return (input, 0);

        int modifiers = 0;
        var keyParts = new List<string>();

        foreach (var part in parts)
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "alt": modifiers |= 1; break;
                case "control":
                case "ctrl": modifiers |= 2; break;
                case "meta":
                case "cmd":
                case "command": modifiers |= 4; break;
                case "shift": modifiers |= 8; break;
                default: keyParts.Add(part); break;
            }
        }

        if (modifiers == 0)
            return (input, 0);

        var actualKey = keyParts.Count == 0 ? input : string.Join("+", keyParts);
        return (actualKey, modifiers);
    }

    internal static short KeyNameToVk(string key)
    {
        return key.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => 0x0D,
            "TAB" => 0x09,
            "ESCAPE" or "ESC" => 0x1B,
            "SPACE" => 0x20,
            "BACKSPACE" or "BACK" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            "CTRL" => 0x11,
            "ALT" => 0x12,
            "SHIFT" => 0x10,
            "WIN" => 0x5B,
            _ => throw new ArgumentException($"Unknown key: {key}")
        };
    }
}
