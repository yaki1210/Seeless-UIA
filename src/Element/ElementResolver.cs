using System.Windows;
using System.Windows.Automation;
using SeelessUIA.Element;
using SeelessUIA.Snapshot;

namespace SeelessUIA.Element;

/// <summary>
/// Resolves a ref ID or property selector to an AutomationElement and its center coordinates.
/// Corresponds to agent-browser's resolve_element_center (element.rs:149-214) and
/// find_node_id_by_role_name (element.rs:340-385).
///
/// Dual-path strategy (matches agent-browser):
///   Path A (Fast): Cached RuntimeId -> FindFirst with RuntimeIdProperty -> BoundingRectangle
///   Path B (Fallback): RuntimeId stale -> TreeWalker with PropertyCondition -> nth match
///   Path C (Selector): PropertyCondition on ControlType/Name/AutomationId
/// </summary>
public class ElementResolver
{
    private readonly RefMap _refMap;
    private readonly AutomationElement _rootScope;

    public ElementResolver(RefMap refMap, AutomationElement rootScope)
    {
        _refMap = refMap;
        _rootScope = rootScope;
    }

    /// <summary>
    /// Parse a ref string. Accepts "@e1", "ref=e1", or "e1".
    /// </summary>
    public static string? ParseRef(string input)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        if (input.StartsWith('@') && input.Length > 1)
        {
            var candidate = input[1..];
            if (IsRefFormat(candidate))
                return candidate;
        }

        if (input.StartsWith("ref=", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = input[4..];
            if (IsRefFormat(candidate))
                return candidate;
        }

        if (IsRefFormat(input))
            return input;

        return null;
    }

    private static bool IsRefFormat(string s)
    {
        if (s.Length < 2 || s[0] != 'e')
            return false;
        for (int i = 1; i < s.Length; i++)
        {
            if (!char.IsDigit(s[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Resolve a ref ID or property selector to element center coordinates.
    /// Returns (centerX, centerY) or throws on failure.
    /// </summary>
    public (double x, double y) ResolveCenter(string selectorOrRef)
    {
        var element = ResolveElement(selectorOrRef);
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty || (rect.Width <= 0 && rect.Height <= 0))
            throw new InvalidOperationException($"Element '{selectorOrRef}' has zero-size bounding rectangle");

        return GetCenter(rect);
    }

    /// <summary>
    /// Resolve a ref ID or property selector to an AutomationElement.
    /// </summary>
    public AutomationElement ResolveElement(string selectorOrRef)
    {
        var refId = ParseRef(selectorOrRef);
        if (refId != null)
            return ResolveByRef(refId);

        return ResolveBySelector(selectorOrRef);
    }

    /// <summary>
    /// Path A (Fast): Use cached RuntimeId from RefMap.
    /// Path B (Fallback): Re-query the UIA tree by role + name + nth.
    /// </summary>
    private AutomationElement ResolveByRef(string refId)
    {
        var entry = _refMap.Get(refId)
            ?? throw new InvalidOperationException($"Ref '{refId}' not found in RefMap. Run snapshot first.");

        // Path A: Try cached RuntimeId
        if (entry.RuntimeId is { Length: > 0 })
        {
            try
            {
                var runtimeIdProp = AutomationElement.RuntimeIdProperty;
                var condition = new PropertyCondition(runtimeIdProp, entry.RuntimeId);
                var element = _rootScope.FindFirst(TreeScope.Descendants, condition);
                if (element != null)
                    return element;
            }
            catch
            {
                // RuntimeId stale, fall through to Path B
            }
        }

        // Path B: Re-query UIA tree
        return FindNodeByRoleName(entry.Role, entry.Name, entry.Nth ?? 0);
    }

    /// <summary>
    /// Path B: Re-query the UIA tree matching role + name + nth occurrence.
    /// Corresponds to find_node_id_by_role_name (element.rs:340-385).
    /// </summary>
    private AutomationElement FindNodeByRoleName(string role, string name, int nth)
    {
        var targetControlType = RoleToControlType(role);

        var condition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            targetControlType);

        var allMatches = _rootScope.FindAll(TreeScope.Descendants, condition);

        int matchCount = 0;
        foreach (AutomationElement candidate in allMatches)
        {
            try
            {
                var candidateName = candidate.Current.Name ?? "";

                if (candidateName == name)
                {
                    if (matchCount == nth)
                        return candidate;
                    matchCount++;
                }
            }
            catch
            {
                // Element disconnected, continue
            }
        }

        throw new InvalidOperationException(
            $"Could not find element with role='{role}' name='{name}' (nth={nth})");
    }

    /// <summary>
    /// Path C: Property-based selector.
    /// Supports: "automationId:xxx", "name:xxx", "name*:xxx", "class:xxx", "control:xxx"
    /// </summary>
    private AutomationElement ResolveBySelector(string selector)
    {
        System.Windows.Automation.Condition condition;

        if (selector.StartsWith("automationId:"))
        {
            var value = selector[14..];
            condition = new PropertyCondition(AutomationElement.AutomationIdProperty, value);
        }
        else if (selector.StartsWith("name*:"))
        {
            var value = selector[6..];
            condition = new PropertyCondition(AutomationElement.NameProperty, value);
        }
        else if (selector.StartsWith("name:"))
        {
            var value = selector[5..];
            condition = new PropertyCondition(AutomationElement.NameProperty, value);
        }
        else if (selector.StartsWith("class:"))
        {
            var value = selector[6..];
            condition = new PropertyCondition(AutomationElement.ClassNameProperty, value);
        }
        else if (selector.StartsWith("control:"))
        {
            var value = selector[8..];
            var ctId = ControlTypeLookup.GetId($"ControlType.{value}");
            var targetCt = ControlType.LookupById(ctId);
            condition = new PropertyCondition(AutomationElement.ControlTypeProperty, targetCt);
        }
        else
        {
            condition = new PropertyCondition(AutomationElement.NameProperty, selector);
        }

        var element = _rootScope.FindFirst(TreeScope.Descendants, condition)
            ?? throw new InvalidOperationException($"Selector '{selector}' did not match any element");

        return element;
    }

    /// <summary>
    /// Map role string back to ControlType object for TreeWalker queries.
    /// </summary>
    private static ControlType RoleToControlType(string role)
    {
        var entry = RoleMapping.ControlTypeToRole
            .FirstOrDefault(kv => kv.Value == role);

        if (entry.Key != 0)
            return ControlType.LookupById(entry.Key);

        return role switch
        {
            "textbox" => ControlType.Edit,
            "button" => ControlType.Button,
            "link" => ControlType.Hyperlink,
            "checkbox" => ControlType.CheckBox,
            "radio" => ControlType.RadioButton,
            "combobox" => ControlType.ComboBox,
            "listitem" => ControlType.ListItem,
            "menuitem" => ControlType.MenuItem,
            "tab" => ControlType.TabItem,
            "treeitem" => ControlType.TreeItem,
            "slider" => ControlType.Slider,
            "text" => ControlType.Text,
            "image" => ControlType.Image,
            "heading" => ControlType.Header,
            _ => ControlType.Custom,
        };
    }

    /// <summary>
    /// Compute center point of a Rect.
    /// Corresponds to box_model_center (element.rs:470-479).
    /// </summary>
    public static (double x, double y) GetCenter(Rect rect)
    {
        return (rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0);
    }
}
