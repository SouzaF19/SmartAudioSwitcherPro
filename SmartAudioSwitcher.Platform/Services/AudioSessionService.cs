using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using SmartAudioSwitcher.Core.Interfaces;

namespace SmartAudioSwitcher.Platform.Services;
/// <summary>Read-only Core Audio session enumeration and polling. Never changes session volume or routing.
/// Persisted per-app policy is deliberately fail-closed; see CoreAudio/INTEGRATION-NOTES.md.</summary>
public sealed class AudioSessionService : IAudioSessionService, IDisposable
{
    readonly ILogger<AudioSessionService> _logger;
    readonly Func<IReadOnlyList<AudioSession>> _snapshot;
    readonly TimeSpan _interval;
    readonly object _gate = new();
    Timer? _timer;
    AudioSession[] _last = Array.Empty<AudioSession>();
    bool _disposed;
    long _generation;
    public event EventHandler<AudioSessionEvent>? SessionChanged;
    public AudioSessionService(ILogger<AudioSessionService> logger) : this(logger, EnumerateSessions)
    {
    }

    public AudioSessionService(ILogger<AudioSessionService> logger, Func<IReadOnlyList<AudioSession>> snapshot, TimeSpan? pollInterval = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _interval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        if (_interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public IReadOnlyList<AudioSession> GetActiveSessions()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                return _snapshot().ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session enumeration failed");
                return Array.Empty<AudioSession>();
            }
        }
    }

    public AudioSession? GetSessionByProcessId(int processId) => GetActiveSessions().FirstOrDefault(s => s.ProcessId == processId);
    public bool SetSessionDevice(int processId, string deviceId)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _logger.LogWarning("Per-app routing UNSUPPORTED for PID {Pid}, endpoint {Endpoint}: persisted Windows policy ABI is undocumented/version-dependent and has not been hardware validated. No routing change was made", processId, deviceId);
            return false;
        }
    }

    public void StartMonitoring()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_timer != null)
                return;
            _last = GetActiveSessions().ToArray();
            var generation = ++_generation;
            _timer = new Timer(_ => Poll(generation), null, _interval, _interval);
        }
    }

    public void StopMonitoring()
    {
        lock (_gate)
        {
            ++_generation;
            _timer?.Dispose();
            _timer = null;
        }
    }

    void Poll(long generation)
    {
        List<AudioSessionEvent> changes;
        lock (_gate)
        {
            if (_disposed || _timer == null || generation != _generation)
                return;
            AudioSession[] current;
            try
            {
                current = _snapshot().ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Session polling failed; retaining previous snapshot");
                return;
            }

            changes = new List<AudioSessionEvent>();
            // Core model has no session-instance identifier. Preserve all entries; match by PID/endpoint/display plus ordinal.
            var moved = current.GroupBy(s => s.ProcessId).Where(g => g.Count() == 1).Select(g => g.First()).Where(item => _last.Count(s => s.ProcessId == item.ProcessId) == 1 && _last.Single(s => s.ProcessId == item.ProcessId).CurrentDeviceId != item.CurrentDeviceId).ToArray();
            foreach (var item in moved)
                changes.Add(new(AudioSessionEventType.DeviceChanged, item));
            var movedPids = moved.Select(s => s.ProcessId).ToHashSet();
            var old = Index(_last.Where(s => !movedPids.Contains(s.ProcessId)));
            var next = Index(current.Where(s => !movedPids.Contains(s.ProcessId)));
            foreach (var pair in next)
            {
                if (!old.TryGetValue(pair.Key, out var previous))
                {
                    changes.Add(new(AudioSessionEventType.Created, pair.Value));
                    continue;
                }

                var item = pair.Value;
                if (previous.Volume != item.Volume)
                    changes.Add(new(AudioSessionEventType.VolumeChanged, item));
                if (previous.IsMuted != item.IsMuted)
                    changes.Add(new(AudioSessionEventType.MuteChanged, item));
                if (previous.DisplayName != item.DisplayName)
                    changes.Add(new(AudioSessionEventType.DisplayNameChanged, item));
            }

            foreach (var pair in old)
                if (!next.ContainsKey(pair.Key))
                    changes.Add(new(AudioSessionEventType.Disposed, pair.Value));
            _last = current;
        }
        // Never invoke subscribers under the snapshot lock. In-flight callbacks may finish after Stop.
        foreach (var change in changes)
        {
            lock (_gate)
            {
                if (_disposed || generation != _generation) return;
            }
            foreach (EventHandler<AudioSessionEvent> handler in SessionChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                try { handler(this, change); }
                catch (Exception ex) { _logger.LogError(ex, "Session subscriber failed"); }
        }
    }

    static Dictionary<(int Pid, string Endpoint, int Ordinal), AudioSession> Index(IEnumerable<AudioSession> sessions)
    {
        var result = new Dictionary<(int, string, int), AudioSession>();
        foreach (var group in sessions.GroupBy(s => (s.ProcessId, s.CurrentDeviceId)))
        {
            int ordinal = 0;
            foreach (var item in group)
                result[(group.Key.ProcessId, group.Key.CurrentDeviceId, ordinal++)] = item;
        }

        return result;
    }

    static IReadOnlyList<AudioSession> EnumerateSessions()
    {
        var result = new List<AudioSession>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device)
            {
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        try
                        {
                            if (session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                                continue;
                            int pid = checked((int)session.GetProcessID);
                            bool system = session.IsSystemSoundsSession;
                            string processName = system ? "System Sounds" : "Unknown";
                            if (pid > 0)
                                try
                                {
                                    using var process = Process.GetProcessById(pid);
                                    processName = process.ProcessName;
                                }
                                catch (ArgumentException)
                                {
                                }
                                catch (System.ComponentModel.Win32Exception)
                                {
                                }
                                catch (InvalidOperationException)
                                {
                                }

                            var display = session.DisplayName;
                            result.Add(new(pid, processName, string.IsNullOrWhiteSpace(display) ? processName : display, session.IconPath, system, session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute, device.ID));
                        }
                        catch (System.Runtime.InteropServices.COMException)
                        { /* Session may expire during enumeration. */
                        }
                    }
                }
                catch (System.Runtime.InteropServices.COMException)
                { /* Endpoint may disappear during enumeration. */
                }
            }

        return result;
    }

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            StopMonitoring();
            _disposed = true;
            _last = Array.Empty<AudioSession>();
        }
    }
}
