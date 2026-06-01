using System.Windows.Automation;

namespace SeelessUIA.Snapshot;

/// <summary>
/// Cleaning pipeline for the UIA node tree.
/// Mirrors agent-browser's build_tree post-processing (snapshot.rs:978-1028)
/// Strategy:
///   1. Basic filtering (offscreen, zero-size, chrome)
///   2. Container collapsing (Pane/Group with single child)
///   3. Consecutive Text aggregation
///   4. Parent-child name deduplication
/// </summary>
public class TreeCleaner
{
    /// <summary>
    /// Run the full cleaning pipeline on the node list.
    /// Mutates the list in place.
    /// </summary>
    public void Clean(List<UiaNode> nodes, bool interactive)
    {
        DeduplicateChildren(nodes);
        FilterBasic(nodes);
        RelabelContainersByClass(nodes);
        RelabelContainersHeuristically(nodes);
        CollapseContainers(nodes);
        CollapseGenericChains(nodes);
        AggregateConsecutiveText(nodes);
        DeduplicateName(nodes);
    }

    /// <summary>
    /// Step 1: Basic filtering.
    /// Marks nodes as cleared for: offscreen, zero-size, chrome elements.
    /// Corresponds to InlineTextBox/ignored filtering (snapshot.rs:938).
    /// </summary>
    private static void FilterBasic(List<UiaNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsCleared)
                continue;

            // Zero-size elements — keep if they have semantic identity (name or automationId)
            if (node.BoundingRect.HasValue)
            {
                var r = node.BoundingRect.Value;
                if ((r.Width <= 0 || r.Height <= 0)
                    && string.IsNullOrEmpty(node.Name)
                    && string.IsNullOrEmpty(node.AutomationId))
                {
                    node.Clear();
                    continue;
                }
            }

            // Offscreen elements — keep if they have semantic identity
            if (node.IsOffscreen
                && string.IsNullOrEmpty(node.Name)
                && string.IsNullOrEmpty(node.AutomationId))
            {
                node.Clear();
                continue;
            }
        }
    }

    /// <summary>
    /// Step 2: Container collapsing.
    /// Panes and Groups with a single non-structural child get penetrated.
    /// Panes and Groups with no name, no patterns, and no interactive children get penetrated.
    /// Corresponds to generic skip logic in snapshot.rs:1071.
    /// </summary>
    private static void CollapseContainers(List<UiaNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsCleared || node.Children.Count == 0)
                continue;

            if (!RoleMapping.IsTransparent(ControlTypeLookup.GetId(node.ControlTypeName)))
                continue;

            // Collapse single-child transparent containers (not for custom/generic — those wrap meaningful subtrees)
            if (node.Children.Count == 1
                && ControlTypeLookup.GetId(node.ControlTypeName) != ControlType.Custom.Id)
            {
                int childIdx = node.Children[0];
                var child = nodes[childIdx];

                if (!child.IsCleared && !RoleMapping.IsStructural(child.Role))
                {
                    // Promote child to parent position
                    node.Clear();
                    continue;
                }
            }

            // Collapse empty-name containers with no interactive children
            if (string.IsNullOrEmpty(node.Name)
                && !node.IsKeyboardFocusable
                && !HasAnyPattern(node)
                && !HasInteractiveChildren(nodes, node))
            {
                node.Clear();
                continue;
            }
        }
    }

    /// <summary>
    /// Step 2.5: Collapse consecutive empty-generic chains.
    /// If parent and its only child are both empty-name generics,
    /// promote the grandchild chain up and clear the child.
    /// Iterates to convergence (handles 3+ level chains).
    /// </summary>
    private static void CollapseGenericChains(List<UiaNode> nodes)
    {
        bool changed;
        do
        {
            changed = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                var parent = nodes[i];
                if (parent.IsCleared) continue;
                if (parent.Role != "generic" || !string.IsNullOrEmpty(parent.Name)) continue;
                if (parent.Children.Count != 1) continue;

                int childIdx = parent.Children[0];
                if (childIdx >= nodes.Count) continue;
                var child = nodes[childIdx];
                if (child.IsCleared) continue;
                if (child.Role != "generic" || !string.IsNullOrEmpty(child.Name)) continue;

                // Both are empty generic — only collapse if subtree beneath child has no content
                if (SubtreeHasContent(nodes, child)) continue;

                // Safe to collapse: promote grandchildren, clear child
                parent.Children.Clear();
                foreach (var gc in child.Children)
                    parent.Children.Add(gc);
                child.Clear();
                changed = true;
            }
        } while (changed);
    }

    /// <summary>
    /// Step 3: Consecutive Text aggregation.
    /// Merges consecutive Text/Image nodes under the same parent.
    /// Corresponds to StaticText aggregation (snapshot.rs:989-1019).
    /// </summary>
    private static void AggregateConsecutiveText(List<UiaNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var parent = nodes[i];
            if (parent.IsCleared || parent.Children.Count < 2)
                continue;

            var children = new List<int>(parent.Children);
            int start = 0;

            while (start < children.Count)
            {
                int end = start;
                while (end < children.Count)
                {
                    var child = nodes[children[end]];
                    if (!child.IsCleared && IsTextLike(child))
                    {
                        end++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (end > start + 1)
                {
                    // Aggregate names into first text node
                    var first = nodes[children[start]];
                    var sb = new System.Text.StringBuilder();
                    for (int j = start; j < end; j++)
                    {
                        sb.Append(nodes[children[j]].Name);
                    }
                    first.Name = sb.ToString();

                    // Clear remaining text nodes
                    for (int j = start + 1; j < end; j++)
                    {
                        nodes[children[j]].Clear();
                    }
                }

                start = end + 1;
            }
        }
    }

    /// <summary>
    /// Step 4: Parent-child name deduplication.
    /// If a parent has exactly one Text child with the same name, clear the child.
    /// Corresponds to snapshot.rs:1022-1028.
    /// </summary>

    /// <summary>
    /// Step 3.5: Remove duplicate children under the same parent.
    /// UIA providers can report the same element multiple times as siblings.
    /// Cross-parent dedup is handled in TreeBuilder via RuntimeId tracking.
    /// </summary>
    private static void DeduplicateChildren(List<UiaNode> nodes)
    {
        foreach (var parent in nodes)
        {
            if (parent.IsCleared || parent.Children.Count < 2)
                continue;

            var seen = new HashSet<string>();
            var deduped = new List<int>();

            foreach (var childIdx in parent.Children)
            {
                if (childIdx >= nodes.Count)
                    continue;

            var child = nodes[childIdx];
            // Don't deduplicate elements with no name and no AutomationId — can't reliably identify duplicates.
            // Two unnamed lists with the same child count are NOT the same element.
            if (string.IsNullOrEmpty(child.Name) && string.IsNullOrEmpty(child.AutomationId))
            {
                deduped.Add(childIdx);
                continue;
            }
            var key = $"{child.ControlTypeName}|{child.Name}|{child.AutomationId}|{child.ClassName}|{child.Children.Count}";
                if (seen.Add(key))
                {
                    deduped.Add(childIdx);
                }
                else
                {
                    child.Clear();
                }
            }

            parent.Children.Clear();
            parent.Children.AddRange(deduped);
        }
    }

    private static void DeduplicateName(List<UiaNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var parent = nodes[i];
            if (parent.IsCleared || string.IsNullOrEmpty(parent.Name))
                continue;

            var activeChildren = parent.Children
                .Select(idx => nodes[idx])
                .Where(c => !c.IsCleared)
                .ToList();

            if (activeChildren.Count == 1)
            {
                var child = activeChildren[0];
                if (IsTextLike(child) && child.Name == parent.Name)
                {
                    child.Clear();
                }
            }
        }
    }

    private static bool IsTextLike(UiaNode node)
    {
        return node.Role is "text" or "image";
    }

    private static bool HasAnyPattern(UiaNode node)
    {
        return node.HasInvokePattern
            || node.HasTogglePattern
            || node.HasValuePattern
            || node.HasSelectionItemPattern
            || node.HasExpandCollapsePattern
            || node.HasScrollPattern;
    }

    private static bool HasInteractiveChildren(List<UiaNode> nodes, UiaNode parent)
    {
        foreach (var childIdx in parent.Children)
        {
            if (childIdx >= nodes.Count) continue;
            var child = nodes[childIdx];
            if (child.IsCleared) continue;
            if (RoleMapping.IsInteractive(child.Role) || HasAnyPattern(child) || child.IsKeyboardFocusable)
                return true;
            if (HasInteractiveChildren(nodes, child))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Relabel empty generic/custom containers based on their framework ClassName.
    /// Maps common Qt, Win32, WPF class names to semantic UIA roles.
    /// </summary>
    private static void RelabelContainersByClass(List<UiaNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsCleared) continue;

            if (node.Role != "generic" && node.Role != "custom" && !RoleMapping.IsTransparent(ControlTypeLookup.GetId(node.ControlTypeName)))
                continue;

            var cls = node.ClassName ?? "";
            if (string.IsNullOrEmpty(cls)) continue;

            if (cls.Contains("MenuBar", StringComparison.OrdinalIgnoreCase))
                node.Role = "menu";
            else if (cls.Contains("ToolBar", StringComparison.OrdinalIgnoreCase) || cls.Contains("Toolbar", StringComparison.OrdinalIgnoreCase))
                node.Role = "toolbar";
            else if (cls.Contains("StatusBar", StringComparison.OrdinalIgnoreCase) || cls.Contains("Statusbar", StringComparison.OrdinalIgnoreCase))
                node.Role = "statusbar";
            else if (cls.Contains("TabBar", StringComparison.OrdinalIgnoreCase) || cls.Contains("Tabbar", StringComparison.OrdinalIgnoreCase) || cls.Contains("TabWidget", StringComparison.OrdinalIgnoreCase))
                node.Role = "tablist";
            else if (cls.Contains("SysTabControl32", StringComparison.OrdinalIgnoreCase))
                node.Role = "tablist";
            else if (node.FrameworkId == "WPF")
            {
                if (cls == "Menu" || cls == "ContextMenu")
                    node.Role = "menu";
                else if (cls == "TabControl")
                    node.Role = "tablist";
            }
        }
    }

    /// <summary>
    /// Heuristically relabel empty generic/custom containers based on their active children distribution.
    /// </summary>
    private static void RelabelContainersHeuristically(List<UiaNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsCleared) continue;

            if (node.Role != "generic" && node.Role != "custom" && !RoleMapping.IsTransparent(ControlTypeLookup.GetId(node.ControlTypeName)))
                continue;

            var activeChildren = node.Children
                .Select(idx => idx < nodes.Count ? nodes[idx] : null)
                .Where(c => c != null && !c!.IsCleared)
                .ToList();

            if (activeChildren.Count == 0) continue;

            // 1. Menu: All children are menuitems
            if (activeChildren.All(c => c!.Role == "menuitem"))
            {
                node.Role = "menu";
                continue;
            }

            // 2. Tab List: All children are tabs/tabitems
            if (activeChildren.All(c => c!.Role == "tab"))
            {
                node.Role = "tablist";
                continue;
            }

            // 3. List: All children are listitems
            if (activeChildren.All(c => c!.Role == "listitem"))
            {
                node.Role = "list";
                continue;
            }

            // 3.5. Tree: All children are treeitems
            if (activeChildren.All(c => c!.Role == "treeitem"))
            {
                node.Role = "tree";
                continue;
            }

            // 4. Toolbar: Contains multiple buttons and no input fields (textboxes/comboboxes)
            var buttonCount = activeChildren.Count(c => c!.Role == "button");
            var inputCount = activeChildren.Count(c => c!.Role == "textbox" || c!.Role == "combobox");
            if (buttonCount >= 2 && inputCount == 0 && activeChildren.All(c => c!.Role == "button" || c!.Role == "separator" || c!.Role == "image" || c!.Role == "generic"))
            {
                node.Role = "toolbar";
                continue;
            }
        }
    }

    /// <summary>
    /// Check if the subtree rooted at this node contains any meaningful content
    /// (named elements, interactive elements, or elements with patterns).
    /// Used to prevent collapsing generic wrappers that protect content-bearing subtrees.
    /// </summary>
    private static bool SubtreeHasContent(List<UiaNode> nodes, UiaNode node)
    {
        if (!string.IsNullOrEmpty(node.Name) || !string.IsNullOrEmpty(node.Value)
            || HasAnyPattern(node) || node.IsKeyboardFocusable)
            return true;
        foreach (var childIdx in node.Children)
        {
            if (childIdx >= nodes.Count) continue;
            var child = nodes[childIdx];
            if (child.IsCleared) continue;
            if (SubtreeHasContent(nodes, child)) return true;
        }
        return false;
    }
}
