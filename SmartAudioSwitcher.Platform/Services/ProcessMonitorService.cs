using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using System.Diagnostics;
using System.Management;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.Platform.Services;

public class ProcessMonitorService : IProcessMonitorService, IDisposable
{
    private readonly ILogger<ProcessMonitorService> _logger;
    private readonly ManagementEventWatcher _processStartWatcher;
    private readonly ManagementEventWatcher _processStopWatcher;
    private readonly Timer _pollTimer;
    private readonly HashSet<int> _knownProcesses = new();
    private bool _disposed;

    public event EventHandler<ProcessEvent>? ProcessEvent;

    public ProcessMonitorService(ILogger<ProcessMonitorService> logger)
    {
        _logger = logger;

        // WMI watchers for process creation/termination
        _processStartWatcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
        _processStartWatcher.EventArrived += OnProcessStarted;

        _processStopWatcher = new ManagementEventWatcher(
            new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
        _processStopWatcher.EventArrived += OnProcessStopped;

        // Polling timer for window focus/title changes (WMI doesn't provide these reliably)
        _pollTimer = new Timer(PollProcesses, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    public IReadOnlyList<ProcessInfo> GetRunningProcesses()
    {
        var processes = new List<ProcessInfo>();

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.HasExited) continue;
                    
                    var info = new ProcessInfo(
                        ProcessId: process.Id,
                        ProcessName: process.ProcessName,
                        WindowTitle: process.MainWindowTitle,
                        MainModulePath: GetMainModulePath(process),
                        StartTime: process.StartTime,
                        HasMainWindow: !string.IsNullOrEmpty(process.MainWindowTitle)
                    );
                    
                    processes.Add(info);
                    _knownProcesses.Add(process.Id);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Access denied or process exited - skip
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error getting info for process {ProcessId}", process.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get running processes");
        }

        return processes.AsReadOnly();
    }

    public ProcessInfo? GetProcessById(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return new ProcessInfo(
                ProcessId: process.Id,
                ProcessName: process.ProcessName,
                WindowTitle: process.MainWindowTitle,
                MainModulePath: GetMainModulePath(process),
                StartTime: process.StartTime,
                HasMainWindow: !string.IsNullOrEmpty(process.MainWindowTitle)
            );
        }
        catch
        {
            return null;
        }
    }

    public bool IsProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public ProcessInfo? GetForegroundProcess()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            GetWindowThreadProcessId(hwnd, out uint processId);
            return GetProcessById((int)processId);
        }
        catch
        {
            return null;
        }
    }

    public void StartMonitoring()
    {
        try
        {
            _processStartWatcher.Start();
            _processStopWatcher.Start();
            _logger.LogDebug("Process monitoring started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process monitoring");
        }
    }

    public void StopMonitoring()
    {
        try
        {
            _processStartWatcher.Stop();
            _processStopWatcher.Stop();
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _logger.LogDebug("Process monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop process monitoring");
        }
    }

    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
            
            _logger.LogDebug("Process started: {ProcessName} (PID: {ProcessId})", processName, processId);
            
            var info = GetProcessById(processId);
            if (info != null)
            {
                ProcessEvent?.Invoke(this, new ProcessEvent(
                    ProcessEventType.Started,
                    processId,
                    processName,
                    info.WindowTitle,
                    DateTime.Now
                ));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling process start event");
        }
    }

    private void OnProcessStopped(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processId = Convert.ToInt32(e.NewEvent.Properties["ProcessID"].Value);
            var processName = e.NewEvent.Properties["ProcessName"].Value?.ToString() ?? "";
            
            _logger.LogDebug("Process stopped: {ProcessName} (PID: {ProcessId})", processName, processId);
            
            ProcessEvent?.Invoke(this, new ProcessEvent(
                ProcessEventType.Exited,
                processId,
                processName,
                null,
                DateTime.Now
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling process stop event");
        }
    }

    private void PollProcesses(object? state)
    {
        try
        {
            // Check for window focus/title changes
            var foreground = GetForegroundProcess();
            if (foreground != null)
            {
                // Could track focus changes here
            }

            // Check for title changes in known processes
            foreach (var pid in _knownProcesses.ToList())
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (process.HasExited)
                    {
                        _knownProcesses.Remove(pid);
                        continue;
                    }

                    var currentTitle = process.MainWindowTitle;
                    // In a real implementation, we'd track previous titles and detect changes
                }
                catch
                {
                    _knownProcesses.Remove(pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error in process polling");
        }
    }

    private string? GetMainModulePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _processStartWatcher?.Dispose();
            _processStopWatcher?.Dispose();
            _pollTimer?.Dispose();
            _disposed = true;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}