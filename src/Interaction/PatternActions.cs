using System.Windows;
using System.Windows.Automation;
using SeelessUIA.Element;

namespace SeelessUIA.Interaction;

/// <summary>
/// Executes actions using UIA Patterns (preferred path).
/// Corresponds to agent-browser's interaction.rs Pattern equivalents.
/// </summary>
public class PatternActions
{
    private readonly ElementResolver _resolver;

    public PatternActions(ElementResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Click via InvokePattern.
    /// Corresponds to click via CDP mouse events (interaction.rs:9-27).
    /// </summary>
    public void Click(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<InvokePattern>(element, InvokePattern.Pattern, out var invokePattern))
        {
            invokePattern.Invoke();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support InvokePattern. Use SendInputActions.Click as fallback.");
    }

    /// <summary>
    /// Fill a value via ValuePattern.SetValue.
    /// Corresponds to fill via focus+selectAll+clear+insertText (interaction.rs:83-147).
    /// </summary>
    public void Fill(string selectorOrRef, string value)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        // Try SetFocus then ValuePattern
        try { element.SetFocus(); } catch { }

        if (TryGetPattern<ValuePattern>(element, ValuePattern.Pattern, out var valuePattern))
        {
            valuePattern.SetValue(value);
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support ValuePattern. Use SendInputActions.Fill as fallback.");
    }

    /// <summary>
    /// Toggle a checkbox/radio via TogglePattern.
    /// Corresponds to check/uncheck (interaction.rs:439-539).
    /// </summary>
    public void Toggle(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<TogglePattern>(element, TogglePattern.Pattern, out var togglePattern))
        {
            togglePattern.Toggle();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support TogglePattern. Use SendInputActions.Click as fallback.");
    }

    /// <summary>
    /// Select a list item via SelectionItemPattern.
    /// </summary>
    public void Select(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<SelectionItemPattern>(element, SelectionItemPattern.Pattern, out var selectionPattern))
        {
            selectionPattern.Select();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support SelectionItemPattern. Use SendInputActions.Click as fallback.");
    }

    /// <summary>
    /// Expand or collapse via ExpandCollapsePattern.
    /// </summary>
    public void Expand(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expandPattern))
        {
            expandPattern.Expand();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support ExpandCollapsePattern.");
    }

    public void Collapse(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<ExpandCollapsePattern>(element, ExpandCollapsePattern.Pattern, out var expandPattern))
        {
            expandPattern.Collapse();
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support ExpandCollapsePattern.");
    }

    /// <summary>
    /// Scroll via ScrollPattern.
    /// </summary>
    public void Scroll(string selectorOrRef, double horizontalPercent, double verticalPercent)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<ScrollPattern>(element, ScrollPattern.Pattern, out var scrollPattern))
        {
            // Try to scroll to percentage
            if (scrollPattern.Current.VerticallyScrollable)
            {
                scrollPattern.SetScrollPercent(
                    double.IsNaN(horizontalPercent) ? ScrollPattern.NoScroll : horizontalPercent,
                    verticalPercent);
            }
            return;
        }

        throw new InvalidOperationException(
            $"Element '{selectorOrRef}' does not support ScrollPattern.");
    }

    /// <summary>
    /// Set focus on an element.
    /// </summary>
    public void Focus(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);
        element.SetFocus();
    }

    /// <summary>
    /// Scroll element into view via ScrollItemPattern.
    /// </summary>
    public void ScrollIntoView(string selectorOrRef)
    {
        var element = _resolver.ResolveElement(selectorOrRef);

        if (TryGetPattern<ScrollItemPattern>(element, ScrollItemPattern.Pattern, out var scrollItemPattern))
        {
            scrollItemPattern.ScrollIntoView();
            return;
        }

        // Fallback: just set focus (may trigger auto-scroll in some frameworks)
        try { element.SetFocus(); } catch { }
    }

    internal static bool TryGetPattern<T>(
        AutomationElement element,
        AutomationPattern pattern,
        out T patternObject) where T : BasePattern
    {
        var obj = element.GetCurrentPattern(pattern);
        if (obj is T typed)
        {
            patternObject = typed;
            return true;
        }

        patternObject = default!;
        return false;
    }

    internal static bool TryGetTogglePattern(AutomationElement element, out TogglePattern tp)
    {
        var obj = element.GetCurrentPattern(TogglePattern.Pattern);
        if (obj is TogglePattern t)
        {
            tp = t;
            return true;
        }
        tp = default!;
        return false;
    }
}
