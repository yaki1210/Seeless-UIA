namespace SeelessUIA.Element;

/// <summary>
/// Tracks occurrences of role:name combinations for disambiguation.
/// Corresponds to agent-browser's RoleNameTracker (snapshot.rs:185-214).
/// </summary>
public class RoleNameTracker
{
    private readonly Dictionary<string, int> _counts = new();
    private readonly Dictionary<string, int> _totalCounts = new();

    /// <summary>
    /// Register a role:name pair and return its occurrence index (0-based).
    /// </summary>
    public int Track(string role, string name)
    {
        var key = $"{role}:{name}";
        _counts.TryGetValue(key, out var count);
        _counts[key] = count + 1;
        _totalCounts[key] = _totalCounts.GetValueOrDefault(key) + 1;
        return count;
    }

    /// <summary>
    /// Returns keys that appear more than once.
    /// </summary>
    public Dictionary<string, int> GetDuplicates()
    {
        return _totalCounts
            .Where(kv => kv.Value > 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
