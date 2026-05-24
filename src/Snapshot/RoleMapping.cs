using System.Windows.Automation;

namespace SeelessUIA.Snapshot;

/// <summary>
/// ControlType to role string mapping.
/// Corresponds to agent-browser's INTERACTIVE_ROLES / CONTENT_ROLES / STRUCTURAL_ROLES (snapshot.rs:11-66).
/// Uses ControlType.Id int values as dictionary keys.
/// </summary>
public static class RoleMapping
{
    public static readonly Dictionary<int, string> ControlTypeToRole = new()
    {
        [ControlType.Button.Id]       = "button",
        [ControlType.Edit.Id]         = "textbox",
        [ControlType.Hyperlink.Id]    = "link",
        [ControlType.CheckBox.Id]     = "checkbox",
        [ControlType.RadioButton.Id]  = "radio",
        [ControlType.ComboBox.Id]     = "combobox",
        [ControlType.List.Id]         = "list",
        [ControlType.ListItem.Id]     = "listitem",
        [ControlType.MenuItem.Id]     = "menuitem",
        [ControlType.TabItem.Id]      = "tab",
        [ControlType.TreeItem.Id]     = "treeitem",
        [ControlType.Slider.Id]       = "slider",
        [ControlType.Spinner.Id]      = "spinbutton",
        [ControlType.Text.Id]         = "text",
        [ControlType.Image.Id]        = "image",
        [ControlType.Header.Id]       = "heading",
        [ControlType.HeaderItem.Id]   = "heading",
        [ControlType.DataItem.Id]     = "cell",
        [ControlType.ProgressBar.Id]  = "progressbar",
        [ControlType.ScrollBar.Id]    = "scrollbar",
        [ControlType.Separator.Id]    = "separator",
        [ControlType.ToolBar.Id]      = "toolbar",
        [ControlType.StatusBar.Id]    = "statusbar",
        [ControlType.Custom.Id]       = "custom",
        [ControlType.Document.Id]     = "document",
        [ControlType.Calendar.Id]     = "calendar",
        [ControlType.SplitButton.Id]  = "button",
        [ControlType.Thumb.Id]        = "thumb",
    };

    public static readonly HashSet<string> InteractiveRoles = new()
    {
        "button", "textbox", "link", "checkbox", "radio", "combobox",
        "listitem", "menuitem", "tab", "treeitem", "slider", "spinbutton",
        "thumb"
    };

    public static readonly HashSet<string> ContentRoles = new()
    {
        "heading", "cell", "text", "image", "progressbar", "separator"
    };

    public static readonly HashSet<string> StructuralRoles = new()
    {
        "generic", "group", "list", "table", "menu", "toolbar",
        "statusbar", "pane", "window", "custom", "document", "calendar"
    };

    /// <summary>
    /// ControlTypes that are skipped entirely (chrome elements).
    /// Maps to RootWebArea/WebArea skip logic in snapshot.rs:1090.
    /// </summary>
    public static readonly HashSet<int> SkipControlTypes = new()
    {
        ControlType.Window.Id,
    };

    /// <summary>
    /// ControlTypes whose children are skipped (ephemeral/structural chrome).
    /// </summary>
    public static readonly HashSet<int> SkipChildrenControlTypes = new()
    {
        ControlType.TitleBar.Id,
        ControlType.MenuBar.Id,
        ControlType.ToolTip.Id,
    };

    /// <summary>
    /// ControlTypes that are candidates for transparent container collapsing.
    /// Maps to "generic" handling in snapshot.rs:1071.
    /// </summary>
    public static readonly HashSet<int> TransparentControlTypes = new()
    {
        ControlType.Pane.Id,
        ControlType.Group.Id,
        ControlType.DataGrid.Id,
        ControlType.Table.Id,
    };

    public static string GetRole(int controlTypeId)
    {
        return ControlTypeToRole.GetValueOrDefault(controlTypeId, "generic");
    }

    public static bool IsInteractive(string role)
    {
        return InteractiveRoles.Contains(role);
    }

    public static bool IsContent(string role)
    {
        return ContentRoles.Contains(role);
    }

    public static bool IsStructural(string role)
    {
        return StructuralRoles.Contains(role);
    }

    public static bool ShouldSkip(int controlTypeId)
    {
        return SkipControlTypes.Contains(controlTypeId);
    }

    public static bool ShouldSkipChildren(int controlTypeId)
    {
        return SkipChildrenControlTypes.Contains(controlTypeId);
    }

    public static bool IsTransparent(int controlTypeId)
    {
        return TransparentControlTypes.Contains(controlTypeId);
    }
}
