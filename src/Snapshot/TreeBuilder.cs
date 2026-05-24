using System.Windows;
using System.Windows.Automation;

namespace SeelessUIA.Snapshot;

public class TreeBuilder
{
    private readonly int _maxDepth;
    private readonly bool _rawView;

    /// <summary>LegacyIAccessible (MSAA) property IDs — may be null if not registered.</summary>
    private static readonly AutomationProperty? MsaaNameProperty = SafeLookup(30092);
    private static readonly AutomationProperty? MsaaValueProperty = SafeLookup(30093);
    private static readonly AutomationProperty? MsaaDescriptionProperty = SafeLookup(30094);
    private static readonly AutomationProperty? MsaaHelpProperty = SafeLookup(30097);

    internal static string CleanClassName(string? raw)
    {
        var c = raw ?? "";
        if (c.StartsWith("class ")) c = c[6..];
        return c;
    }

    private static AutomationProperty? SafeLookup(int id)
    {
        try { return AutomationProperty.LookupById(id); }
        catch { return null; }
    }

    public TreeBuilder(int maxDepth = 16, bool rawView = false)
    {
        _maxDepth = maxDepth;
        _rawView = rawView;
    }

    public (List<UiaNode> Nodes, List<int> RootIndices) BuildTree(AutomationElement rootElement)
    {
        var nodes = new List<UiaNode>();

        var cacheRequest = new CacheRequest
        {
            TreeScope = TreeScope.Subtree,
            AutomationElementMode = AutomationElementMode.Full,
        };

        // Basic properties (batched into Cached struct)
        cacheRequest.Add(AutomationElement.RuntimeIdProperty);
        cacheRequest.Add(AutomationElement.AutomationIdProperty);
        cacheRequest.Add(AutomationElement.ControlTypeProperty);
        cacheRequest.Add(AutomationElement.NameProperty);
        cacheRequest.Add(AutomationElement.ClassNameProperty);
        cacheRequest.Add(AutomationElement.FrameworkIdProperty);
        cacheRequest.Add(AutomationElement.IsEnabledProperty);
        cacheRequest.Add(AutomationElement.IsOffscreenProperty);
        cacheRequest.Add(AutomationElement.IsKeyboardFocusableProperty);
        cacheRequest.Add(AutomationElement.BoundingRectangleProperty);

        // Pattern availability (batched into Cached struct)
        cacheRequest.Add(AutomationElement.IsInvokePatternAvailableProperty);
        cacheRequest.Add(AutomationElement.IsTogglePatternAvailableProperty);
        cacheRequest.Add(AutomationElement.IsValuePatternAvailableProperty);
        cacheRequest.Add(AutomationElement.IsSelectionItemPatternAvailableProperty);
        cacheRequest.Add(AutomationElement.IsExpandCollapsePatternAvailableProperty);
        cacheRequest.Add(AutomationElement.IsScrollPatternAvailableProperty);
        cacheRequest.Add(AutomationElement.IsTextPatternAvailableProperty);

        // LegacyIAccessible (MSAA) properties — use correct property IDs.
        // Not exposed as named constants in .NET 10 managed API.
        // May be null if not registered — guard with conditional add.
        if (MsaaNameProperty != null) cacheRequest.Add(MsaaNameProperty);
        if (MsaaValueProperty != null) cacheRequest.Add(MsaaValueProperty);
        if (MsaaDescriptionProperty != null) cacheRequest.Add(MsaaDescriptionProperty);
        if (MsaaHelpProperty != null) cacheRequest.Add(MsaaHelpProperty);

        // UIA HelpText
        cacheRequest.Add(AutomationElement.HelpTextProperty);

        // Raw view mode: use unfiltered tree
        if (_rawView)
            cacheRequest.TreeFilter = Automation.RawViewCondition;

        using (cacheRequest.Activate())
        {
            var updatedRoot = rootElement.GetUpdatedCache(cacheRequest);
            WalkCached(updatedRoot, nodes, 0);
        }

        return (nodes, FindRootIndices(nodes));
    }

    private void WalkCached(AutomationElement element, List<UiaNode> nodes, int depth)
    {
        if (depth > _maxDepth) return;

        int[] runtimeId = [];
        try { runtimeId = (int[]?)element.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty) ?? []; }
        catch { }

        var node = CreateNodeFromCache(element, depth, runtimeId);
        int nodeIdx = nodes.Count;
        nodes.Add(node);

        // Children are pre-cached via TreeScope.Subtree — no COM call
        var children = element.CachedChildren;
        if (children == null || children.Count == 0) return;

        for (int i = 0; i < children.Count; i++)
        {
            try
            {
                int childIdx = nodes.Count;
                node.Children.Add(childIdx);
                WalkCached(children[i], nodes, depth + 1);
                nodes[childIdx].ParentIdx = nodeIdx;
            }
            catch
            {
                // Skip disconnected
            }
        }
    }

    private static UiaNode CreateNodeFromCache(AutomationElement element, int depth, int[] runtimeId)
    {
        var cached = element.Cached;
        var controlType = cached.ControlType ?? ControlType.Custom;
        var controlTypeId = controlType.Id;
        var role = RoleMapping.GetRole(controlTypeId);
        bool isStructural = RoleMapping.IsStructural(role);

        static bool GetCachedBool(AutomationElement element, AutomationProperty property)
        {
            try { return (bool?)element.GetCachedPropertyValue(property) ?? false; }
            catch { return false; }
        }

        static string GetCachedString(AutomationElement element, AutomationProperty property)
        {
            try { return (string?)element.GetCachedPropertyValue(property) ?? ""; }
            catch { return ""; }
        }

        bool hasInvoke = GetCachedBool(element, AutomationElement.IsInvokePatternAvailableProperty);
        bool hasToggle = GetCachedBool(element, AutomationElement.IsTogglePatternAvailableProperty);
        bool hasValue = GetCachedBool(element, AutomationElement.IsValuePatternAvailableProperty);
        bool hasSelectionItem = GetCachedBool(element, AutomationElement.IsSelectionItemPatternAvailableProperty);
        bool hasExpandCollapse = GetCachedBool(element, AutomationElement.IsExpandCollapsePatternAvailableProperty);
        bool hasScroll = GetCachedBool(element, AutomationElement.IsScrollPatternAvailableProperty);
        bool hasText = GetCachedBool(element, AutomationElement.IsTextPatternAvailableProperty);

        // State detail — read from cache (0 COM), queried during CacheRequest setup
        string checkedStr = "";
        bool? expanded = null;
        bool? selected = null;
        string value = "";

        if (!isStructural && hasToggle)
        {
            try
            {
                var state = (ToggleState?)element.GetCachedPropertyValue(TogglePattern.ToggleStateProperty);
                checkedStr = state switch
                {
                    ToggleState.On => "true", ToggleState.Off => "false",
                    ToggleState.Indeterminate => "mixed", _ => ""
                };
            }
            catch { }
        }

        if (!isStructural && hasExpandCollapse)
        {
            try
            {
                var state = (ExpandCollapseState?)element.GetCachedPropertyValue(ExpandCollapsePattern.ExpandCollapseStateProperty);
                expanded = state == ExpandCollapseState.Expanded;
            }
            catch { }
        }

        if (!isStructural && hasSelectionItem)
        {
            try
            {
                selected = (bool?)element.GetCachedPropertyValue(SelectionItemPattern.IsSelectedProperty);
            }
            catch { }
        }

        if (!isStructural && hasValue)
        {
            try
            {
                value = (string?)element.GetCachedPropertyValue(ValuePattern.ValueProperty) ?? "";
            }
            catch { }
        }

        var rect = cached.BoundingRectangle;

        // Name fallback chain: UIA Name → MSAA accName → MSAA accDescription → MSAA accHelp → UIA HelpText
        var name = (cached.Name ?? "").Trim();
        string msaaDesc = "";
        if (string.IsNullOrEmpty(name) && MsaaNameProperty != null)
        {
            try { name = (string?)element.GetCachedPropertyValue(MsaaNameProperty) ?? ""; } catch { }
        }
        if (string.IsNullOrEmpty(name) && MsaaDescriptionProperty != null)
        {
            try { msaaDesc = (string?)element.GetCachedPropertyValue(MsaaDescriptionProperty) ?? ""; } catch { }
            if (!string.IsNullOrEmpty(msaaDesc)) name = msaaDesc;
        }
        if (string.IsNullOrEmpty(name) && MsaaHelpProperty != null)
        {
            try { name = (string?)element.GetCachedPropertyValue(MsaaHelpProperty) ?? ""; } catch { }
        }
        if (string.IsNullOrEmpty(name))
        {
            try { name = (string?)element.GetCachedPropertyValue(AutomationElement.HelpTextProperty) ?? ""; } catch { }
        }

        return new UiaNode
        {
            RuntimeId = runtimeId,
            AutomationId = cached.AutomationId ?? "",
            HelpText = GetCachedString(element, AutomationElement.HelpTextProperty),
            Description = msaaDesc,
            Role = role,
            ControlTypeName = controlType.ProgrammaticName ?? "",
            Name = name,
            ClassName = CleanClassName(cached.ClassName),
            FrameworkId = cached.FrameworkId ?? "",

            IsEnabled = cached.IsEnabled,
            IsOffscreen = cached.IsOffscreen,
            IsKeyboardFocusable = cached.IsKeyboardFocusable,
            Checked = checkedStr,
            Expanded = expanded,
            Selected = selected,
            Value = value,

            BoundingRect = rect.IsEmpty ? null : rect,

            HasInvokePattern = hasInvoke,
            HasTogglePattern = hasToggle,
            HasValuePattern = hasValue,
            HasSelectionItemPattern = hasSelectionItem,
            HasExpandCollapsePattern = hasExpandCollapse,
            HasScrollPattern = hasScroll,
            HasTextPattern = hasText,

            Depth = depth,
        };
    }

    private static List<int> FindRootIndices(List<UiaNode> nodes)
    {
        var isChild = new bool[nodes.Count];
        foreach (var node in nodes)
            foreach (var child in node.Children)
                if (child < isChild.Length) isChild[child] = true;

        var roots = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
            if (!isChild[i]) roots.Add(i);
        return roots;
    }

}
