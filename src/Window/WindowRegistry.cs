using System.Text.Json;

namespace SeelessUIA.Window;

public class WindowRegistry
{
    private readonly string _filePath;
    private readonly Dictionary<string, WindowEntry> _windows = new();
    private int _nextRef = 1;

    public string? ActiveRef { get; private set; }

    public WindowRegistry(string? dataDir = null)
    {
        dataDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SeelessUIA");
        Directory.CreateDirectory(dataDir);
        _filePath = Path.Combine(dataDir, "windows.json");
        Load();
    }

    public WindowEntry? Get(string refId)
    {
        _windows.TryGetValue(refId, out var entry);
        return entry;
    }

    public WindowEntry? GetActive()
    {
        if (ActiveRef != null && _windows.TryGetValue(ActiveRef, out var entry))
            return entry;
        return null;
    }

    public string Register(long hwnd, int processId, string processName, string title)
    {
        foreach (var (id, entry) in _windows)
        {
            if (entry.Hwnd == hwnd)
                return id;
        }

        var refId = $"w{_nextRef++}";
        _windows[refId] = new WindowEntry
        {
            Hwnd = hwnd,
            ProcessId = processId,
            ProcessName = processName,
            Title = title,
        };

        if (ActiveRef == null)
            ActiveRef = refId;

        Save();
        return refId;
    }

    public void SetActive(string refId)
    {
        if (!_windows.ContainsKey(refId))
            throw new InvalidOperationException($"Window '{refId}' not registered. Run 'windows' first.");
        ActiveRef = refId;
        Save();
    }

    /// <summary>
    /// Refresh the window list from the current set of visible windows.
    /// Matches existing windows by HWND — preserves their wN refs.
    /// Removes windows that are no longer visible (retires their wN).
    /// Assigns new wN to windows that appear for the first time.
    /// wN numbers are never reused after retirement — _nextRef only increments.
    /// </summary>
    public void RefreshAll(List<WindowEntry> current)
    {
        var currentHwnds = new HashSet<long>();
        var matched = new HashSet<string>();

        foreach (var entry in current)
        {
            currentHwnds.Add(entry.Hwnd);

            // Find existing entry by HWND — preserve its wN
            var existing = _windows.FirstOrDefault(kv => kv.Value.Hwnd == entry.Hwnd);
            if (existing.Key != null)
            {
                // Update name/title in place
                existing.Value.ProcessName = entry.ProcessName;
                existing.Value.Title = entry.Title;
                existing.Value.ProcessId = entry.ProcessId;
                matched.Add(existing.Key);
            }
            else
            {
                // New window — assign fresh wN (never reuse)
                var refId = $"w{_nextRef++}";
                _windows[refId] = entry;
                matched.Add(refId);
            }
        }

        // Remove windows that are no longer visible (retire their wN permanently)
        var toRemove = _windows.Keys.Where(k => !matched.Contains(k)).ToList();
        foreach (var key in toRemove)
            _windows.Remove(key);

        // Update active if current active window is gone
        if (ActiveRef != null && !_windows.ContainsKey(ActiveRef))
        {
            ActiveRef = _windows.Keys.FirstOrDefault();
        }
        else if (_windows.Count > 0 && ActiveRef == null)
        {
            ActiveRef = _windows.Keys.First();
        }

        Save();
    }

    public List<(string RefId, WindowEntry Entry)> ListAll()
    {
        return _windows.OrderBy(kv =>
        {
            // Sort by wN numeric value
            var num = int.TryParse(kv.Key[1..], out var n) ? n : 0;
            return num;
        }).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<RegistryData>(json);
            if (data == null) return;
            _windows.Clear();
            foreach (var (refId, entry) in data.Windows)
                _windows[refId] = entry;
            ActiveRef = data.Active;
            _nextRef = data.NextRef > 0 ? data.NextRef : _nextRef;
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var data = new RegistryData
            {
                Windows = _windows,
                Active = ActiveRef,
                NextRef = _nextRef,
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private class RegistryData
    {
        public Dictionary<string, WindowEntry> Windows { get; set; } = new();
        public string? Active { get; set; }
        public int NextRef { get; set; }
    }
}

public class WindowEntry
{
    public long Hwnd { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string Title { get; set; } = "";
}
