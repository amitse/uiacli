using System.Diagnostics;
using Uia.Core.Models;

namespace Uia.Core;

public class ProcessManager
{
    public List<ProcessInfo> ListProcesses()
    {
        var result = new List<ProcessInfo>();
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainWindowHandle == IntPtr.Zero) continue;
                if (string.IsNullOrEmpty(proc.MainWindowTitle)) continue;

                result.Add(new ProcessInfo
                {
                    ProcessId = proc.Id,
                    ProcessName = proc.ProcessName,
                    MainWindowTitle = proc.MainWindowTitle,
                    MainWindowHandle = proc.MainWindowHandle.ToInt64()
                });
            }
            catch { }
        }
        return result;
    }

    public ProcessInfo LaunchApp(string pathOrName)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pathOrName,
            UseShellExecute = true
        };
        var proc = Process.Start(psi) 
            ?? throw new InvalidOperationException($"Failed to start: {pathOrName}");

        // Wait for main window to appear
        for (int i = 0; i < 50; i++) // up to 5 seconds
        {
            Thread.Sleep(100);
            proc.Refresh();
            if (proc.MainWindowHandle != IntPtr.Zero)
                break;
        }

        return new ProcessInfo
        {
            ProcessId = proc.Id,
            ProcessName = proc.ProcessName,
            MainWindowTitle = proc.MainWindowTitle ?? "",
            MainWindowHandle = proc.MainWindowHandle.ToInt64()
        };
    }
}

public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string MainWindowTitle { get; set; } = "";
    public long MainWindowHandle { get; set; }
}
