using SeelessUIA.Element;

namespace SeelessUIA.Snapshot;

/// <summary>
/// Computes the difference between two snapshot states (RefMaps).
/// Used for --diff mode and auto-diff after interactions.
/// </summary>
public static class SnapshotDiff
{
    public record DiffEntry(string Ref, string Role, string Name, string Kind);

    /// <summary>
    /// Compare two RefMaps and return changed elements.
    /// Matching by (role, name, expanded, checked) — detects both add/remove and state changes.
    /// </summary>
    public static List<DiffEntry> Compare(RefMap? prev, RefMap curr)
    {
        var changes = new List<DiffEntry>();

        if (prev == null)
            return changes;

        // Build identity maps: role+name → entry for prev and curr
        var prevMap = new Dictionary<(string, string), RefEntry>();
        foreach (var (_, entry) in prev.EntriesSorted())
        {
            var key = (entry.Role, entry.Name);
            if (!prevMap.ContainsKey(key))
                prevMap[key] = entry;
        }

        var currMap = new Dictionary<(string, string), RefEntry>();
        foreach (var (refId, entry) in curr.EntriesSorted())
        {
            var key = (entry.Role, entry.Name);
            if (!currMap.ContainsKey(key))
                currMap[key] = entry;
        }

        var prevKeys = new HashSet<(string, string)>(prevMap.Keys);
        var currKeys = new HashSet<(string, string)>(currMap.Keys);

        // Added: in current but not previous
        foreach (var key in currKeys)
        {
            if (!prevKeys.Contains(key))
                changes.Add(new DiffEntry("", currMap[key].Role, currMap[key].Name, "added"));
        }

        // Removed: in previous but not current
        foreach (var key in prevKeys)
        {
            if (!currKeys.Contains(key))
                changes.Add(new DiffEntry("", prevMap[key].Role, prevMap[key].Name, "removed"));
        }

        // Modified: same identity but state changed (expanded/collapsed, checked state)
        foreach (var key in currKeys)
        {
            if (prevMap.TryGetValue(key, out var prevEntry) && currMap.TryGetValue(key, out var currEntry))
            {
                if (prevEntry.Expanded != currEntry.Expanded
                    || prevEntry.Checked != currEntry.Checked
                    || prevEntry.Selected != currEntry.Selected)
                {
                    changes.Add(new DiffEntry("", currEntry.Role, currEntry.Name, "modified"));
                }
            }
        }

        return changes;
    }
}
