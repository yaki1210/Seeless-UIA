namespace SeelessUIA.Snapshot;

/// <summary>
/// Utility to resolve ControlType programmatic names to integer IDs.
/// </summary>
public static class ControlTypeLookup
{
    private static readonly Dictionary<string, int> _nameToId = new();

    static ControlTypeLookup()
    {
        // Register known control types by their Id constant values
        Register(50000, "ControlType.Button");
        Register(50004, "ControlType.Edit");
        Register(50005, "ControlType.Hyperlink");
        Register(50002, "ControlType.CheckBox");
        Register(50003, "ControlType.RadioButton");
        Register(50006, "ControlType.ComboBox");
        Register(50008, "ControlType.List");
        Register(50009, "ControlType.ListItem");
        Register(50011, "ControlType.MenuItem");
        Register(50018, "ControlType.TabItem");
        Register(50019, "ControlType.TreeItem");
        Register(50017, "ControlType.Slider");
        Register(50016, "ControlType.Spinner");
        Register(50020, "ControlType.Text");
        Register(50007, "ControlType.Image");
        Register(50034, "ControlType.Header");
        Register(50035, "ControlType.HeaderItem");
        Register(50029, "ControlType.DataItem");
        Register(50014, "ControlType.ProgressBar");
        Register(50015, "ControlType.ScrollBar");
        Register(50021, "ControlType.Separator");
        Register(50022, "ControlType.ToolBar");
        Register(50023, "ControlType.StatusBar");
        Register(50025, "ControlType.Custom");
        Register(50032, "ControlType.Window");
        Register(50033, "ControlType.Pane");
        Register(50026, "ControlType.Group");
        Register(50037, "ControlType.TitleBar");
        Register(50010, "ControlType.MenuBar");
        Register(50030, "ControlType.ToolTip");
        Register(50028, "ControlType.DataGrid");
        Register(50036, "ControlType.Table");
        Register(50024, "ControlType.Thumb");
        Register(50027, "ControlType.Document");
        Register(50001, "ControlType.Calendar");
        Register(50012, "ControlType.SplitButton");
    }

    private static void Register(int id, string name)
    {
        _nameToId[name] = id;
    }

    public static int GetId(string programmaticName)
    {
        return _nameToId.GetValueOrDefault(programmaticName, 50025); // Custom
    }
}
