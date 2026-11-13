using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace SmartAudioSwitcher.Platform.Services;
/// <summary>Routes are tracked per PID. Removing a route restores only a previously observed endpoint,
/// not a persisted Windows preference (the Core contract has no get/clear preference API).</summary>
public sealed class SonicFlowEngine : ISonicFlowEngine, IDisposable
{
    readonly ILogger<SonicFlowEngine> _logger;
    readonly IAudioDeviceService _devices;
    readonly IAudioSessionService _sessions;
    readonly IProcessMonitorService? _processMonitor;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Dictionary<int, Tracked> _routes = new();
    bool _disposed;
    sealed record Tracked(ActiveRoute Route, string? PreviousDevice, DateTime ProcessStart);
    public event EventHandler<RouteAppliedEventArgs>? RouteApplied;
    public event EventHandler<RouteErrorEventArgs>? RouteError;
    public SonicFlowEngine(ILogger<SonicFlowEngine> logger, IAudioDeviceService deviceService, IAudioSessionService sessionService, IProcessMonitorService processMonitor, IProfileEngine profileEngine)
    {
        _logger = logger;
        _devices = deviceService;
        _sessions = sessionService;
        _processMonitor = processMonitor;
    }

    public async Task<int> ApplyRoutesAsync(IReadOnlyList<VisualRoute> routes, IReadOnlyList<AudioDevice> availableDevices, IReadOnlyList<ProcessInfo> runningProcesses, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var validation = ValidateRoutes(routes, availableDevices, runningProcesses);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                Error("", "", error);
            return 0;
        }

        int applied = 0;
        foreach (var resolved in validation.ResolvedRoutes.Where(r => r.IsResolved))
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool success = false;
            await _gate.WaitAsync(cancellationToken);
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var process = resolved.MatchedProcess!;
                var target = resolved.MatchedDevice!;
                var existing = _routes.GetValueOrDefault(process.ProcessId);
                var observedEndpoints = _sessions.GetActiveSessions().Where(s => s.ProcessId == process.ProcessId).Select(s => s.CurrentDeviceId).Distinct().ToArray();
                var previous = existing != null && existing.ProcessStart == process.StartTime ? existing.PreviousDevice : observedEndpoints.Length == 1 ? observedEndpoints[0] : null;
                success = _sessions.SetSessionDevice(process.ProcessId, target.Id);
                if (success)
                {
                    _routes[process.ProcessId] = new(new(process.ProcessId, process.ProcessName, target.Id, target.FriendlyName, DateTime.UtcNow, true), previous, process.StartTime);
                    applied++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Route application failed");
                Error(resolved.VisualRoute.SourceTitle, resolved.VisualRoute.TargetTitle, ex.Message);
            }
            finally
            {
                _gate.Release();
            }

            if (success)
                RouteApplied?.Invoke(this, new() { ProcessName = resolved.MatchedProcess!.ProcessName, DeviceName = resolved.MatchedDevice!.FriendlyName, Success = true });
            else
                Error(resolved.VisualRoute.SourceTitle, resolved.VisualRoute.TargetTitle, "Session endpoint assignment was rejected or could not be verified");
        }

        return applied;
    }

    public async Task<IReadOnlyList<ActiveRoute>> GetActiveRoutesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var names = _devices.GetOutputDevices().GroupBy(d => d.Id).ToDictionary(g => g.Key, g => g.First().FriendlyName);
            return _sessions.GetActiveSessions().Where(s => !s.IsSystemSounds && names.ContainsKey(s.CurrentDeviceId)).Select(s => new ActiveRoute(s.ProcessId, s.ProcessName, s.CurrentDeviceId, names[s.CurrentDeviceId], _routes.GetValueOrDefault(s.ProcessId)?.Route.AppliedAt ?? DateTime.UtcNow, true)).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveRouteAsync(string sourceProcessName, string targetDeviceId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // The public contract names a process, so apply removal to every tracked instance of that exact name.
            var matches = _routes.Where(p => Normalize(p.Value.Route.ProcessName).Equals(Normalize(sourceProcessName), StringComparison.OrdinalIgnoreCase) && p.Value.Route.TargetDeviceId == targetDeviceId).ToArray();
            if (matches.Length == 0)
                return false;
            bool all = true;
            foreach (var pair in matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tracked = pair.Value;
                var current = _sessions.GetSessionByProcessId(pair.Key);
                if (_processMonitor != null)
                {
                    var process = _processMonitor.GetProcessById(pair.Key);
                    if (process == null || process.StartTime != tracked.ProcessStart || !Normalize(process.ProcessName).Equals(Normalize(tracked.Route.ProcessName), StringComparison.OrdinalIgnoreCase))
                    {
                        all = false;
                        continue;
                    }
                }

                // Do not overwrite another actor's change or claim restoration when no original endpoint is known.
                if (string.IsNullOrWhiteSpace(tracked.PreviousDevice) || current?.CurrentDeviceId != targetDeviceId)
                {
                    all = false;
                    continue;
                }

                try
                {
                    if (!_sessions.SetSessionDevice(pair.Key, tracked.PreviousDevice) || _sessions.GetSessionByProcessId(pair.Key)?.CurrentDeviceId != tracked.PreviousDevice)
                    {
                        all = false;
                        continue;
                    }

                    _routes.Remove(pair.Key);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not restore PID {Pid}", pair.Key);
                    all = false;
                }
            }

            return all;
        }
        finally
        {
            _gate.Release();
        }
    }

    public RouteValidationResult ValidateRoutes(IReadOnlyList<VisualRoute> routes, IReadOnlyList<AudioDevice> availableDevices, IReadOnlyList<ProcessInfo> runningProcesses)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new RouteValidationResult
        {
            IsValid = true
        };
        foreach (var route in routes)
        {
            if (route.SourceType != NodeType.App || route.TargetType != NodeType.Device)
            {
                result.IsValid = false;
                result.Errors.Add("Only app-to-render-device routes are supported");
                continue;
            }

            var pattern = string.IsNullOrWhiteSpace(route.SourceProcessName) ? route.SourceTitle : route.SourceProcessName;
            var processes = runningProcesses.Where(p => Match(p.ProcessName, pattern) || (p.MainModulePath != null && Match(p.MainModulePath, pattern))).DistinctBy(p => p.ProcessId).ToArray();
            var targets = availableDevices.Where(d => !string.IsNullOrEmpty(route.TargetDeviceId) ? d.Id == route.TargetDeviceId : d.FriendlyName.Equals(route.TargetTitle, StringComparison.OrdinalIgnoreCase)).ToArray();
            var target = targets.Length == 1 ? targets[0] : null;
            if (processes.Length == 0 || target == null || !target.IsConnected)
            {
                result.IsValid = false;
                result.Errors.Add($"Unresolved, ambiguous, or disconnected route: {route.SourceTitle} -> {route.TargetTitle}");
                result.ResolvedRoutes.Add(new(route, null, target, false));
                continue;
            }

            foreach (var process in processes)
                result.ResolvedRoutes.Add(new(route, process, target, true));
        }

        return result;
    }

    static string Normalize(string name) => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    static bool Match(string name, string pattern)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pattern))
            return false;
        // A path pattern stays a path; bare exe patterns match Process.ProcessName without suffix.
        if (!pattern.Contains('\\') && !pattern.Contains('/'))
        {
            name = name.Replace('\\', '/').Split('/').Last();
            name = Normalize(name);
            pattern = Normalize(pattern);
        }

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        try
        {
            return Regex.IsMatch(name, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    void Error(string source, string target, string message) => RouteError?.Invoke(this, new() { SourceName = source, TargetName = target, ErrorMessage = message });
    // Keep the semaphore alive: concurrent waiters must receive ObjectDisposedException, not race Release/Dispose.
    public void Dispose()
    {
        _gate.Wait();
        try
        {
            _disposed = true;
            _routes.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }
}
