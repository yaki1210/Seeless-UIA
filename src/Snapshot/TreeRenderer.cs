using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeelessUIA.Snapshot;

/// <summary>
/// Renders the UIA node tree to indented text output.
/// Format: {indent}- {role} "{name}" [attrs] {kind} [{hints}] : {value}
/// </summary>
public class TreeRenderer
{
    private readonly SnapshotOptions _options;

    private static readonly char[] InvisibleChars =
    [
        '\uFEFF', '\u200B', '\u200C', '\u200D', '\u2060', '\u00A0',
    ];

    public TreeRenderer(SnapshotOptions options)
    {
        _options = options;
    }

    private HashSet<int> _rendered = new();

    public string Render(List<UiaNode> nodes, List<int> rootIndices)
    {
        var output = new StringBuilder();
        _rendered.Clear();

        foreach (var rootIdx in rootIndices)
            RenderTree(nodes, rootIdx, 0, output);

        _rendered.Clear();

        var result = output.ToString().TrimEnd('\n');

        if (_options.Compact)
            result = CompactTree(result);

        return string.IsNullOrEmpty(result)
            ? (_options.Interactive ? "(no interactive elements)" : "(empty page)")
            : result;
    }

    private void RenderTree(List<UiaNode> nodes, int idx, int indent, StringBuilder output)
    {
        if (idx < 0 || idx >= nodes.Count) return;
        var node = nodes[idx];

        if (_options.Depth.HasValue && indent > _options.Depth.Value) return;

        if (node.IsCleared)
        {
            foreach (var child in node.Children)
                RenderTree(nodes, child, indent, output);
            return;
        }

        // Cross-parent dedup: skip nodes already rendered via a different parent path
        if (!_rendered.Add(idx)) return;

        var controlTypeId = ControlTypeLookup.GetId(node.ControlTypeName);

        // Skip chrome elements (TitleBar, MenuBar, ToolTip)
        if (RoleMapping.ShouldSkipChildren(controlTypeId)) return;

        // Skip Window wrapper
        if (RoleMapping.ShouldSkip(controlTypeId))
        {
            foreach (var child in node.Children)
                RenderTree(nodes, child, indent, output);
            return;
        }

        // --- Interactive mode: skip non-ref nodes flat ---
        if (_options.Interactive && !node.HasRef)
        {
            foreach (var child in node.Children)
                RenderTree(nodes, child, indent, output);
            return;
        }

        // --- Structured mode: keep grouping containers ---
        if (_options.Structured && !node.HasRef && RoleMapping.IsStructural(node.Role))
        {
            // Only keep structural nodes that have at least one ref descendant
            if (!HasRefDescendant(nodes, node))
            {
                foreach (var child in node.Children)
                    RenderTree(nodes, child, indent, output);
                return;
            }

            // Render as grouping header, then children indented
            RenderLine(node, indent, output, includeRefs: false);
            foreach (var child in node.Children)
                RenderTree(nodes, child, indent + 1, output);
            return;
        }

        // --- Regular node rendering ---
        RenderLine(node, indent, output, includeRefs: true);
        foreach (var child in node.Children)
            RenderTree(nodes, child, indent + 1, output);
    }

    private void RenderLine(UiaNode node, int indent, StringBuilder output, bool includeRefs)
    {
        var prefix = new string(' ', indent * 2);
        var line = new StringBuilder();
        line.Append(prefix);
        line.Append("- ");
        line.Append(node.Role);

        // Name (JSON-escaped)
        var displayName = GetDisplayName(node);
        if (!string.IsNullOrEmpty(displayName))
        {
            var escaped = JsonSerializer.Serialize(displayName, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var cleaned = escaped.Trim(InvisibleChars);
            line.Append(' ');
            line.Append(cleaned);
        }

        // Attributes (only for ref-bearing or explicitly structural nodes)
        var attrs = new List<string>();

        if (!string.IsNullOrEmpty(node.Checked))
            attrs.Add($"checked={node.Checked}");
        if (node.Expanded == true)
            attrs.Add("expanded");
        if (node.Expanded == false)
            attrs.Add("collapsed");
        if (node.Selected == true)
            attrs.Add("selected");
        if (node.HeadingLevel.HasValue)
            attrs.Add($"level={node.HeadingLevel.Value}");
        if (!node.IsEnabled)
            attrs.Add("disabled");
        if (!string.IsNullOrEmpty(node.RefId) && includeRefs)
            attrs.Add($"ref={node.RefId}");
        if (!string.IsNullOrEmpty(node.AutomationId) && node.AutomationId != "RootWebArea")
            attrs.Add(StripClIds(node.AutomationId));
        // ClassName tag — only for containers relabeled by RelabelContainersByClass
        if (!string.IsNullOrEmpty(node.ClassName)
            && node.Role is "menu" or "toolbar" or "statusbar" or "tablist")
        {
            var c = node.ClassName;
            if (c.StartsWith("class ")) c = c[6..];
            attrs.Add($"{c}");
        }

        if (attrs.Count > 0)
        {
            line.Append(" [");
            line.Append(string.Join(", ", attrs));
            line.Append(']');
        }

        // Cursor kind + hints (hints already cleaned of redundant pattern names)
        if (!string.IsNullOrEmpty(node.CursorKind))
        {
            line.Append(' ');
            line.Append(node.CursorKind);
            if (node.CursorHints.Count > 0)
            {
                line.Append(" [");
                line.Append(string.Join(", ", node.CursorHints));
                line.Append(']');
            }
        }

        // Value
        if (!string.IsNullOrEmpty(node.Value) && node.Value != node.Name)
        {
            line.Append(": ");
            line.Append(node.Value);
        }

        output.Append(line);
        output.Append('\n');
    }

    private static string GetDisplayName(UiaNode node)
    {
        var n = node.Name ?? "";
        if (!string.IsNullOrWhiteSpace(n))
        {
            n = StripPua(n.Trim());
            return n;
        }
        var h = node.HelpText ?? "";
        if (!string.IsNullOrWhiteSpace(h))
        {
            h = StripPua(h.Trim());
            return h;
        }
        var d = node.Description ?? "";
        if (!string.IsNullOrWhiteSpace(d))
        {
            d = StripPua(d.Trim());
            return d;
        }
        return "";
    }

    private static string StripPua(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c >= '\uE000' && c <= '\uF8FF') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strip Chromium/Electron internal IDs from automationId.
    /// "radiogroup-cl-13370-item-cl-13372-input" → "radiogroup-item-input"
    /// "session-review-diff-1i1p2w9-trigger" → "session-review-diff-trigger"
    /// </summary>
    private static readonly Regex ClIdRegex = new(@"-cl-\d+|-([a-z0-9]*\d[a-z0-9]{2,})", RegexOptions.Compiled);

    private static string StripClIds(string automationId)
    {
        return ClIdRegex.Replace(automationId, "");
    }

    /// <summary>
    /// Check if any descendant of this node has a ref.
    /// </summary>
    private static bool HasRefDescendant(List<UiaNode> nodes, UiaNode node)
    {
        if (node.HasRef) return true;
        foreach (var childIdx in node.Children)
        {
            if (childIdx >= nodes.Count) continue;
            if (HasRefDescendant(nodes, nodes[childIdx])) return true;
        }
        return false;
    }

    private static string CompactTree(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.None);
        var keepIndices = new HashSet<int>();

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("ref=") || lines[i].Contains(": "))
                keepIndices.Add(i);
        }

        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (!keepIndices.Contains(i)) continue;
            var myIndent = GetIndentLevel(lines[i]);
            for (int j = i - 1; j >= 0; j--)
            {
                var parentIndent = GetIndentLevel(lines[j]);
                if (parentIndent < myIndent)
                {
                    keepIndices.Add(j);
                    myIndent = parentIndent;
                }
            }
        }

        var result = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (keepIndices.Contains(i) && !string.IsNullOrWhiteSpace(lines[i]))
                result.AppendLine(lines[i]);
        }

        var trimmed = result.ToString().TrimEnd('\n');
        return string.IsNullOrEmpty(trimmed) ? "(no interactive elements)" : trimmed;
    }

    private static int GetIndentLevel(string line)
    {
        int spaces = 0;
        foreach (var c in line)
        {
            if (c == ' ') spaces++;
            else break;
        }
        return spaces / 2;
    }
}
