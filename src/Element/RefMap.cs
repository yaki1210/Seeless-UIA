namespace SeelessUIA.Element;

/// <summary>
/// Maps ref IDs (e.g. "e1", "e42") to element metadata.
/// Corresponds to agent-browser's RefMap (element.rs:18-122).
/// </summary>
public class RefMap
{
    private readonly Dictionary<string, RefEntry> _map = new();
    private int _nextRef = 1;

    public void Add(string refId, int[] runtimeId, string role, string name, int? nth,
        string automationId = "", bool? expanded = null, string checkedState = "", bool? selected = null)
    {
        _map[refId] = new RefEntry
        {
            RuntimeId = runtimeId,
            Role = role,
            Name = name,
            Nth = nth,
            AutomationId = automationId,
            Expanded = expanded,
            Checked = checkedState,
            Selected = selected,
        };
    }

    public RefEntry? Get(string refId)
    {
        return _map.GetValueOrDefault(refId);
    }

    public int NextRefNum()
    {
        return _nextRef;
    }

    public void SetNextRefNum(int num)
    {
        _nextRef = num;
    }

    public string AssignNextRef(int[] runtimeId, string role, string name, int? nth,
        string automationId = "", bool? expanded = null, string checkedState = "", bool? selected = null)
    {
        var refId = $"e{_nextRef}";
        _nextRef++;
        Add(refId, runtimeId, role, name, nth, automationId, expanded, checkedState, selected);
        return refId;
    }

    public List<KeyValuePair<string, RefEntry>> EntriesSorted()
    {
        return _map
            .OrderBy(kv =>
            {
                var numStr = kv.Key.StartsWith('e') ? kv.Key[1..] : kv.Key;
                return int.TryParse(numStr, out var n) ? n : int.MaxValue;
            })
            .ToList();
    }

    public void Clear()
    {
        _map.Clear();
        _nextRef = 1;
    }

    public int Count => _map.Count;
}
