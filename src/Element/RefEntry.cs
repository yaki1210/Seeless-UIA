namespace SeelessUIA.Element;

/// <summary>
/// A single ref entry mapping a ref ID (e.g. "e1") to element metadata.
/// Corresponds to agent-browser's RefEntry (element.rs:8-16).
/// </summary>
public class RefEntry
{
    public int[] RuntimeId { get; set; } = [];
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
    public int? Nth { get; set; }
    public string AutomationId { get; set; } = "";
    public bool? Expanded { get; set; }
    public string Checked { get; set; } = "";
    public bool? Selected { get; set; }
}
