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
    public void RefreshAll_ReplacesWindows()
    {
        using var tmp = new TempDir();
        var reg = new WindowRegistry(tmp.Path);
        reg.Register(0x100, 1234, "old", "old");

        reg.RefreshAll(new List<WindowEntry>
        {
            new() { Hwnd = 0x300, ProcessId = 9999, ProcessName = "new", Title = "new-title" },
        });

        var all = reg.ListAll();
        Assert.Single(all);
        Assert.All(all, kv => Assert.NotEqual(0x100, kv.Entry.Hwnd));
        Assert.Contains(all, kv => kv.Entry.Hwnd == 0x300);
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

    // Helper: temp directory that self-cleans
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
