namespace SeelessUIA.Snapshot;

/// <summary>
/// Options for snapshot acquisition.
/// Corresponds to agent-browser's SnapshotOptions (snapshot.rs:77-84).
/// </summary>
public class SnapshotOptions
{
    public string? Selector { get; set; }

    /// <summary>Interactive mode: skip all non-ref nodes, flat output.</summary>
    public bool Interactive { get; set; }

    /// <summary>Structured mode (default): show list/group/generic as grouping containers.</summary>
    public bool Structured { get; set; } = true;

    /// <summary>Compact mode: keep only lines with ref= or : value and their ancestors.</summary>
    public bool Compact { get; set; }

    /// <summary>Include refs list in output.</summary>
    public bool ShowRefs { get; set; }

    /// <summary>Raw mode: use RawViewCondition instead of ControlViewCondition.</summary>
    public bool RawView { get; set; }

    public int? Depth { get; set; }
}
