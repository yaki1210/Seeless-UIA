using SeelessUIA.Window;
using Xunit;

namespace SeelessUIA.Tests;

public class WindowRegistryTests
{
    [Fact]
    public void Register_AssignsWN()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        var ref1 = reg.Register(0x100, 1234, "notepad", "test.txt");
        var ref2 = reg.Register(0x200, 5678, "calc", "Calculator");

        Assert.Equal("w1", ref1);
        Assert.Equal("w2", ref2);
        Assert.Equal("w1", reg.ActiveRef);
    }

    [Fact]
    public void Register_SameHwnd_ReturnsExistingRef()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        var ref1 = reg.Register(0x100, 1234, "notepad", "test.txt");
        var ref2 = reg.Register(0x100, 1234, "notepad", "test2.txt");

        Assert.Equal(ref1, ref2);
        Assert.Single(reg.ListAll());
    }

    [Fact]
    public void SetActive_SwitchesActiveWindow()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "notepad", "w1-title");
        reg.Register(0x200, 5678, "calc", "w2-title");
        reg.SetActive("w2");

        Assert.Equal("w2", reg.ActiveRef);
        var active = reg.GetActive();
        Assert.NotNull(active);
        Assert.Equal(0x200, active!.Hwnd);
    }

    [Fact]
    public void SetActive_InvalidRef_Throws()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        Assert.Throws<InvalidOperationException>(() => reg.SetActive("w99"));
    }

    [Fact]
    public void RefreshAll_HwndMatch_PreservesRefs()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "notepad", "old-title");
        reg.Register(0x200, 5678, "calc", "calc");

        // Window order changes — w2 appears first, w1 second
        reg.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x200, ProcessId = 5678, ProcessName = "calc", Title = "calc-updated" },
            new() { Hwnd = 0x100, ProcessId = 1234, ProcessName = "notepad", Title = "notepad-updated" },
        });

        var all = reg.ListAll();
        Assert.Equal(2, all.Count);

        // w1 and w2 are preserved (matched by HWND), titles updated
        Assert.Equal("w1", all[0].RefId);
        Assert.Equal("notepad-updated", all[0].Entry.Title);
        Assert.Equal("w2", all[1].RefId);
        Assert.Equal("calc-updated", all[1].Entry.Title);
    }

    [Fact]
    public void RefreshAll_NewWindows_GetFreshRefs()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "notepad", "a");

        // Old window closes, new one opens
        reg.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x200, ProcessId = 5678, ProcessName = "calc", Title = "calc" },
        });

        var all = reg.ListAll();
        Assert.Single(all);

        // w1 is gone (retired), new window gets w2 (never reuse w1)
        Assert.Equal("w2", all[0].RefId);
        Assert.Equal(0x200, all[0].Entry.Hwnd);
    }

    [Fact]
    public void RefreshAll_RemovesClosedWindows()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "notepad", "a");
        reg.Register(0x200, 5678, "calc", "b");
        reg.Register(0x300, 9999, "cmd", "c");

        // Window at 0x200 closes
        reg.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x100, ProcessId = 1234, ProcessName = "notepad", Title = "a" },
            new() { Hwnd = 0x300, ProcessId = 9999, ProcessName = "cmd", Title = "c" },
        });

        var all = reg.ListAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("w1", all[0].RefId);
        Assert.Equal("w3", all[1].RefId); // w2 retired
    }

    [Fact]
    public void RefreshAll_ActiveWindowGone_FallsBackToFirst()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "notepad", "a"); // w1
        reg.Register(0x200, 5678, "calc", "b");    // w2
        reg.SetActive("w2");

        // w2 closes — active must fall back to w1
        reg.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x100, ProcessId = 1234, ProcessName = "notepad", Title = "a" },
        });

        Assert.Equal("w1", reg.ActiveRef);
    }

    [Fact]
    public void RefreshAll_PreservesActiveIfStillPresent()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "notepad", "a");
        reg.Register(0x200, 5678, "calc", "b");
        reg.SetActive("w2");

        reg.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x100, ProcessId = 1234, ProcessName = "notepad", Title = "a" },
            new() { Hwnd = 0x200, ProcessId = 5678, ProcessName = "calc", Title = "b" },
        });

        Assert.Equal("w2", reg.ActiveRef);
    }

    [Fact]
    public void NextRef_PersistsAcrossRecreation()
    {
        using var tmp = new TempDir();
        var reg1 = new WindowRegistry(tmp.Path);
        reg1.Register(0x100, 1234, "notepad", "a");
        reg1.Register(0x200, 5678, "calc", "b");

        // Refresh — w1 closes, w3 opens
        reg1.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x200, ProcessId = 5678, ProcessName = "calc", Title = "b" },
            new() { Hwnd = 0x300, ProcessId = 9999, ProcessName = "cmd", Title = "c" },
        });

        // Recreate registry from persisted state
        var reg2 = new WindowRegistry(tmp.Path);
        var all = reg2.ListAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("w2", all[0].RefId);
        Assert.Equal("w3", all[1].RefId);

        // New window after restart continues numbering
        reg2.Register(0x400, 1111, "explorer", "d");
        Assert.NotNull(reg2.Get("w4"));
    }

    [Fact]
    public void Persistence_SurvivesRecreation()
    {
        using var tmp = new TempDir();
        var reg1 = new WindowRegistry(tmp.Path);
        reg1.Register(0x100, 1234, "notepad", "test");
        reg1.Register(0x200, 5678, "calc", "calc");
        reg1.SetActive("w2");

        var reg2 = new WindowRegistry(tmp.Path);
        Assert.Equal("w2", reg2.ActiveRef);
        var active = reg2.GetActive();
        Assert.NotNull(active);
        Assert.Equal(0x200, active!.Hwnd);
        Assert.Equal(2, reg2.ListAll().Count);
    }

    [Fact]
    public void Get_ReturnsNullForUnknownId()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        Assert.Null(reg.Get("w99"));
    }

    private class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "seeless-uia-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, true); } catch { }
        }
    }
}
