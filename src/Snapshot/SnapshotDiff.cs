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
    /// Matching by (role, name) pair — precise enough for interactive UIs.
    /// </summary>
    public static List<DiffEntry> Compare(RefMap? prev, RefMap curr)
    {
        var changes = new List<DiffEntry>();

        if (prev == null)
            return changes;

        var prevSet = new HashSet<(string, string)>();
        foreach (var (_, entry) in prev.EntriesSorted())
            prevSet.Add((entry.Role, entry.Name));

        var currSet = new HashSet<(string, string)>();
        foreach (var (_, entry) in curr.EntriesSorted())
            currSet.Add((entry.Role, entry.Name));

        // Added: in current but not previous
        foreach (var (refId, entry) in curr.EntriesSorted())
        {
            if (!prevSet.Contains((entry.Role, entry.Name)))
                changes.Add(new DiffEntry(refId, entry.Role, entry.Name, "added"));
        }

        // Removed: in previous but not current
        foreach (var (refId, entry) in prev.EntriesSorted())
        {
            if (!currSet.Contains((entry.Role, entry.Name)))
                changes.Add(new DiffEntry(refId, entry.Role, entry.Name, "removed"));
        }

        return changes;
    }
}
