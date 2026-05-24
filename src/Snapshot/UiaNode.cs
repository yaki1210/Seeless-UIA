using System.Windows;

namespace SeelessUIA.Snapshot;

/// <summary>
/// Internal IR node representing a UIA element in the snapshot tree.
/// Corresponds to agent-browser's TreeNode (snapshot.rs:86-104).
/// </summary>
public class UiaNode
{
    // === Identity (maps to TreeNode.backend_node_id) ===
    public int[] RuntimeId { get; set; } = [];
    public string AutomationId { get; set; } = "";
    public string HelpText { get; set; } = "";
    public string Description { get; set; } = "";

    // === Semantics (maps to TreeNode.role + TreeNode.name) ===
    public string Role { get; set; } = "";
    public string ControlTypeName { get; set; } = "";
    public string Name { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string FrameworkId { get; set; } = "";

    // === State (maps to TreeNode.disabled, checked, expanded, selected) ===
    public bool IsEnabled { get; set; } = true;
    public bool IsOffscreen { get; set; }
    public bool IsKeyboardFocusable { get; set; }
    public string Checked { get; set; } = "";        // "true"/"false"/"mixed"
    public bool? Expanded { get; set; }
    public bool? Selected { get; set; }

    // === Value (maps to TreeNode.value_text) ===
    public string Value { get; set; } = "";

    // === Geometry ===
    public Rect? BoundingRect { get; set; }

    // === Capability flags (UIA-specific, replaces cursor_info JS scan) ===
    public bool HasInvokePattern { get; set; }
    public bool HasTogglePattern { get; set; }
    public bool HasValuePattern { get; set; }
    public bool HasSelectionItemPattern { get; set; }
    public bool HasExpandCollapsePattern { get; set; }
    public bool HasScrollPattern { get; set; }
    public bool HasTextPattern { get; set; }

    // === Tree relationships (exact match to TreeNode) ===
    public List<int> Children { get; set; } = [];
    public int? ParentIdx { get; set; }
    public int Depth { get; set; }

    // === Output markers (exact match to TreeNode) ===
    public bool HasRef { get; set; }
    public string RefId { get; set; } = "";
    public string CursorKind { get; set; } = "";
    public List<string> CursorHints { get; set; } = [];

    // === Marked for removal (maps to TreeNode empty/clear pattern) ===
    public bool IsCleared { get; set; }

    public static UiaNode Cleared()
    {
        return new UiaNode { IsCleared = true };
    }

    public void Clear()
    {
        Role = "";
        Name = "";
        ControlTypeName = "";
        Value = "";
        AutomationId = "";
        HelpText = "";
        Description = "";
        ClassName = "";
        FrameworkId = "";
        Checked = "";
        CursorKind = "";
        RefId = "";
        IsCleared = true;
        HasRef = false;
        IsEnabled = true;
        IsOffscreen = false;
        IsKeyboardFocusable = false;
        HasInvokePattern = false;
        HasTogglePattern = false;
        HasValuePattern = false;
        HasSelectionItemPattern = false;
        HasExpandCollapsePattern = false;
        HasScrollPattern = false;
        HasTextPattern = false;
        Expanded = null;
        Selected = null;
        BoundingRect = null;
        CursorHints.Clear();
    }
}
