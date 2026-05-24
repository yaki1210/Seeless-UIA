using System.Diagnostics;

namespace SeelessUIA.Window;

/// <summary>
/// Process launching and termination.
/// </summary>
public class ProcessManager
{
    /// <summary>
    /// Launch an application by path.
    /// </summary>
    public Process Launch(string path, string? arguments = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = arguments ?? "",
            UseShellExecute = true,
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {path}");

        return process;
    }

    /// <summary>
    /// Kill a process by PID.
    /// </summary>
    public void Kill(int processId)
    {
        var process = Process.GetProcessById(processId);
        process.Kill();
        process.WaitForExit(5000);
    }
}
