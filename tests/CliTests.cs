using System.Diagnostics;
using Xunit;

namespace SeelessUIA.Tests;

public class CliTests
{
    private static string? _exePath;

    private static string GetExePath()
    {
        if (_exePath != null) return _exePath;
        var srcDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var exe = Path.Combine(srcDir, "src", "bin", "Debug", "net10.0-windows", "SeelessUIA.exe");
        if (File.Exists(exe))
        {
            _exePath = exe;
            return exe;
        }
        // Also look relative to test binary
        foreach (var p in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "SeelessUIA.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", "Debug", "net10.0-windows", "SeelessUIA.exe"),
        })
        {
            if (File.Exists(p)) { _exePath = p; return p; }
        }
        throw new FileNotFoundException("SeelessUIA.exe not found. Build first.");
    }

    private static (string stdout, string stderr, int exitCode) RunCli(params string[] args)
    {
        // Kill any existing daemon to avoid port conflicts
        try { Process.Start("taskkill", "/F /IM SeelessUIA.exe").WaitForExit(1000); } catch { }

        var psi = new ProcessStartInfo(GetExePath())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(5000);
        return (stdout.Trim(), stderr.Trim(), proc.ExitCode);
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        var (stdout, stderr, code) = RunCli("help");
        var output = stdout + stderr;
        Assert.Contains("SeelessUIA", output);
        Assert.Contains("USAGE", output);
        Assert.Equal(0, code);
    }

    [Fact]
    public void NoArgs_PrintsUsage()
    {
        var (stdout, stderr, code) = RunCli();
        var output = stdout + stderr;
        Assert.Contains("Usage", output);
        Assert.NotEqual(0, code);
    }

    [Fact]
    public void Snapshot_WithPid_Works()
    {
        Process proc;
        try
        {
            proc = Process.Start("mspaint.exe")!;
        }
        catch
        {
            // mspaint not available, skip
            return;
        }
        proc.WaitForInputIdle(3000);
        Thread.Sleep(500);

        var (stdout, stderr, code) = RunCli("snapshot", "--pid", proc.Id.ToString());
        try { proc.Kill(); } catch { }

        Assert.Equal(0, code);
        Assert.True(stdout.Length > 0, "Snapshot should produce output");
    }

    [Fact]
    public void UnknownCommand_PrintsError()
    {
        var (stdout, stderr, code) = RunCli("garbage123");
        Assert.Contains("Unknown command", stderr);
        Assert.NotEqual(0, code);
    }

    [Fact]
    public void Help_Flag_Works()
    {
        var (stdout, stderr, code) = RunCli("--help");
        Assert.Contains("USAGE", stderr);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Help_ShortFlag_Works()
    {
        var (stdout, stderr, code) = RunCli("-h");
        Assert.Contains("USAGE", stderr);
        Assert.Equal(0, code);
    }

    [Fact]
    public void Click_WithoutDaemon_ErrorOrAutoStart()
    {
        var (stdout, stderr, code) = RunCli("click", "e1");
        // Should either auto-start daemon or return error about missing window
        Assert.True(code != 0 || stderr.Contains("Starting daemon") || stderr.Length > 0);
    }
}
