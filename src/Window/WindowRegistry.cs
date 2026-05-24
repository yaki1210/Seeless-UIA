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
        // Check if already registered
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

        // Auto-set active if this is the first window
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

    public void RefreshAll(List<WindowEntry> current)
    {
        _windows.Clear();
        _nextRef = 1;

        foreach (var entry in current)
        {
            var refId = $"w{_nextRef++}";
            _windows[refId] = entry;
        }

        if (_windows.Count > 0 && (ActiveRef == null || !_windows.ContainsKey(ActiveRef)))
            ActiveRef = _windows.Keys.First();

        Save();
    }

    public List<(string RefId, WindowEntry Entry)> ListAll()
    {
        return _windows.Select(kv => (kv.Key, kv.Value)).ToList();
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
            if (_windows.Count > 0)
                _nextRef = _windows.Keys
                    .Select(k => int.TryParse(k[1..], out var n) ? n : 0)
                    .Max() + 1;
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
    }
}

public class WindowEntry
{
    public long Hwnd { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string Title { get; set; } = "";
}
