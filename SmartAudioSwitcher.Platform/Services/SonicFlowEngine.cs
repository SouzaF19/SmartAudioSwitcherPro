using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace SmartAudioSwitcher.Platform.Services;

/// <summary>
/// Implementation of ISonicFlowEngine that translates visual routes to actual audio routing
/// </summary>
public class SonicFlowEngine : ISonicFlowEngine, IDisposable
{
    private readonly ILogger<SonicFlowEngine> _logger;
    private readonly IAudioDeviceService _deviceService;
    private readonly IAudioSessionService _sessionService;
    private readonly IProcessMonitorService _processMonitor;
    private readonly IProfileEngine _profileEngine;
    
    private readonly Dictionary<string, ActiveRoute> _activeRoutes = new();
    private readonly SemaphoreSlim _routesLock = new(1, 1);
    private bool _disposed = false;

    public event EventHandler<RouteAppliedEventArgs>? RouteApplied;
    public event EventHandler<RouteErrorEventArgs>? RouteError;

    public SonicFlowEngine(
        ILogger<SonicFlowEngine> logger,
        IAudioDeviceService deviceService,
        IAudioSessionService sessionService,
        IProcessMonitorService processMonitor,
        IProfileEngine profileEngine)
    {
        _logger = logger;
        _deviceService = deviceService;
        _sessionService = sessionService;
        _processMonitor = processMonitor;
        _profileEngine = profileEngine;
    }

    public async Task<int> ApplyRoutesAsync(
        IReadOnlyList<VisualRoute> routes,
        IReadOnlyList<AudioDevice> availableDevices,
        IReadOnlyList<ProcessInfo> runningProcesses,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SonicFlowEngine));

        var validation = ValidateRoutes(routes, availableDevices, runningProcesses);
        
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                _logger.LogError("Route validation failed: {Error}", error);
                RouteError?.Invoke(this, new RouteErrorEventArgs
                {
                    SourceName = "",
                    TargetName = "",
                    ErrorMessage = error
                });
            }
            return 0;
        }

        var appliedCount = 0;

        foreach (var resolved in validation.ResolvedRoutes.Where(r => r.IsResolved))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var success = await ApplySingleRouteAsync(resolved, cancellationToken);
                
                if (success)
                {
                    appliedCount++;
                    RouteApplied?.Invoke(this, new RouteAppliedEventArgs
                    {
                        ProcessName = resolved.VisualRoute.SourceProcessName ?? resolved.VisualRoute.SourceTitle,
                        DeviceName = resolved.MatchedDevice?.FriendlyName ?? resolved.VisualRoute.TargetTitle,
                        Success = true
                    });
                }
                else
                {
                    RouteError?.Invoke(this, new RouteErrorEventArgs
                    {
                        SourceName = resolved.VisualRoute.SourceTitle,
                        TargetName = resolved.VisualRoute.TargetTitle,
                        ErrorMessage = "Failed to apply route - SetSessionDevice returned false"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error applying route {Source} -> {Target}", 
                    resolved.VisualRoute.SourceTitle, resolved.VisualRoute.TargetTitle);
                
                RouteError?.Invoke(this, new RouteErrorEventArgs
                {
                    SourceName = resolved.VisualRoute.SourceTitle,
                    TargetName = resolved.VisualRoute.TargetTitle,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
            }
        }

        _logger.LogInformation("Applied {Count}/{Total} routes from SonicFlow", appliedCount, routes.Count);
        return appliedCount;
    }

    private async Task<bool> ApplySingleRouteAsync(ResolvedRoute resolved, CancellationToken cancellationToken)
    {
        if (resolved.MatchedProcess == null || resolved.MatchedDevice == null)
            return false;

        // Use the session service to route the process to the device
        var success = _sessionService.SetSessionDevice(
            resolved.MatchedProcess.ProcessId, 
            resolved.MatchedDevice.Id);

        if (success)
        {
            await _routesLock.WaitAsync(cancellationToken);
            try
            {
                var key = $"{resolved.MatchedProcess.ProcessId}:{resolved.MatchedDevice.Id}";
                _activeRoutes[key] = new ActiveRoute(
                    resolved.MatchedProcess.ProcessId,
                    resolved.MatchedProcess.ProcessName,
                    resolved.MatchedDevice.Id,
                    resolved.MatchedDevice.FriendlyName,
                    DateTime.Now,
                    true);
            }
            finally
            {
                _routesLock.Release();
            }
        }

        return success;
    }

    public async Task<IReadOnlyList<ActiveRoute>> GetActiveRoutesAsync(CancellationToken cancellationToken = default)
    {
        await _routesLock.WaitAsync(cancellationToken);
        try
        {
            // Refresh from actual audio sessions
            var sessions = _sessionService.GetActiveSessions();
            var devices = _deviceService.GetOutputDevices();
            var deviceLookup = devices.ToDictionary(d => d.Id, d => d.FriendlyName);

            var activeRoutes = new List<ActiveRoute>();
            foreach (var session in sessions.Where(s => !s.IsSystemSounds))
            {
                if (deviceLookup.TryGetValue(session.CurrentDeviceId, out var deviceName))
                {
                    activeRoutes.Add(new ActiveRoute(
                        session.ProcessId,
                        session.ProcessName,
                        session.CurrentDeviceId,
                        deviceName,
                        DateTime.Now, // Would need to track actual apply time
                        true));
                }
            }

            return activeRoutes;
        }
        finally
        {
            _routesLock.Release();
        }
    }

    public async Task<bool> RemoveRouteAsync(string sourceProcessName, string targetDeviceId, CancellationToken cancellationToken = default)
    {
        // Find and remove the route - would need to restore to default or previous device
        // For now, just remove from tracking
        await _routesLock.WaitAsync(cancellationToken);
        try
        {
            var key = _activeRoutes.Keys.FirstOrDefault(k => 
                k.StartsWith(sourceProcessName) && k.EndsWith(targetDeviceId));
            
            if (key != null)
            {
                _activeRoutes.Remove(key);
                return true;
            }
            return false;
        }
        finally
        {
            _routesLock.Release();
        }
    }

    public RouteValidationResult ValidateRoutes(
        IReadOnlyList<VisualRoute> routes,
        IReadOnlyList<AudioDevice> availableDevices,
        IReadOnlyList<ProcessInfo> runningProcesses)
    {
        var result = new RouteValidationResult { IsValid = true };

        foreach (var route in routes)
        {
            // Resolve source (app/process)
            ProcessInfo? matchedProcess = null;
            if (!string.IsNullOrEmpty(route.SourceProcessName))
            {
                matchedProcess = runningProcesses.FirstOrDefault(p => 
                    MatchesProcessPattern(p.ProcessName, route.SourceProcessName) ||
                    (p.MainModulePath != null && MatchesProcessPattern(p.MainModulePath, route.SourceProcessName)));
            }
            else if (route.SourceType == NodeType.App)
            {
                // Try to match by title if no process name stored
                matchedProcess = runningProcesses.FirstOrDefault(p => 
                    MatchesProcessPattern(p.ProcessName, route.SourceTitle));
            }

            // Resolve target (device)
            AudioDevice? matchedDevice = null;
            if (!string.IsNullOrEmpty(route.TargetDeviceId))
            {
                matchedDevice = availableDevices.FirstOrDefault(d => d.Id == route.TargetDeviceId);
            }
            else if (route.TargetType == NodeType.Device)
            {
                // Try to match by title/name
                matchedDevice = availableDevices.FirstOrDefault(d => 
                    d.FriendlyName.Contains(route.TargetTitle, StringComparison.OrdinalIgnoreCase) ||
                    MatchesDevicePattern(d.FriendlyName, route.TargetTitle));
            }

            var isResolved = matchedProcess != null && matchedDevice != null;

            if (!isResolved)
            {
                result.IsValid = false;
                if (matchedProcess == null)
                    result.Errors.Add($"No running process matches source '{route.SourceTitle}' (pattern: {route.SourceProcessName ?? "none"})");
                if (matchedDevice == null)
                    result.Errors.Add($"No available device matches target '{route.TargetTitle}' (ID: {route.TargetDeviceId ?? "none"})");
            }
            else if (!matchedDevice.IsConnected)
            {
                result.Warnings.Add($"Target device '{matchedDevice.FriendlyName}' is currently disconnected");
            }

            result.ResolvedRoutes.Add(new ResolvedRoute(route, matchedProcess, matchedDevice, isResolved));
        }

        return result;
    }

    private bool MatchesProcessPattern(string processName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (string.IsNullOrEmpty(processName)) return false;

        // Support wildcards like *.exe, Discord*, etc.
        if (pattern == "*") return true;

        var regexPattern = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";

        try
        {
            return Regex.IsMatch(processName, regexPattern, RegexOptions.IgnoreCase);
        }
        catch
        {
            // Fallback to simple contains
            return processName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }

    private bool MatchesDevicePattern(string deviceName, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (string.IsNullOrEmpty(deviceName)) return false;

        return deviceName.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _routesLock.Dispose();
        }
    }
}