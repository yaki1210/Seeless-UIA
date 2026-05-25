using System.Diagnostics;
using Xunit;

namespace SeelessUIA.Tests;

public class CliTests
{
    private string ExePath => Path.GetFullPath(Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
        "src", "bin", "Debug", "net10.0-windows", "SeelessUIA.exe"));

    private (string stdout, string stderr, int exitCode) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(ExePath, args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try
        {
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(10000);
            return (stdout, stderr, proc.ExitCode);
        }
        catch (InvalidOperationException)
        {
            // Binary not found
            return ("", "EXE NOT FOUND", -1);
        }
    }

    [Fact]
    public void Help_PrintsUsage()
    {
        KillDaemons();
        Thread.Sleep(500);
        var (stdout, stderr, code) = Run("help");
        Assert.Contains("USAGE", stderr + stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownCommand_PrintsError()
    {
        KillDaemons();
        Thread.Sleep(500);
        var (stdout, stderr, code) = Run("garbage_command_xyz");
        Assert.Contains("Unknown command", stderr + stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires daemon auto-start; flaky in CI due to process timing")]
    public void Windows_ReturnsRefFormat()
    {
        KillDaemons();
        Thread.Sleep(500);
        var (stdout, stderr, code) = Run("windows");
        Assert.Contains("w1", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Snapshot_NoArgs_ShowsError()
    {
        KillDaemons();
        Thread.Sleep(500);
        var (stdout, stderr, code) = Run("snapshot");
        Assert.True(!string.IsNullOrEmpty(stderr + stdout),
            "Expected some output (error or usage) when running snapshot without args");
    }

    [Fact]
    public void EmptyArgs_ShowsUsage()
    {
        KillDaemons();
        Thread.Sleep(500);
        var (stdout, stderr, code) = Run();
        Assert.Contains("Usage", stderr + stdout, StringComparison.OrdinalIgnoreCase);
    }

    private static void KillDaemons()
    {
        try
        {
            var psi = new ProcessStartInfo("taskkill", new[] { "/F", "/IM", "SeelessUIA.exe" })
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
        }
        catch { }
    }
}
