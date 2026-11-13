using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Platform.CoreAudio;
using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.Platform.Services;
public sealed class AudioDeviceService : IAudioDeviceService, IDisposable
{
    readonly ILogger<AudioDeviceService> _logger;
    readonly IAudioEndpointBackend _backend;
    readonly object _gate = new();
    AudioDevice[] _devices = Array.Empty<AudioDevice>();
    bool _monitoring, _disposed;
    public event EventHandler<DeviceEvent>? DeviceChanged;
    public AudioDeviceService(ILogger<AudioDeviceService> logger) : this(logger, new WindowsAudioEndpointBackend())
    {
    }

    public AudioDeviceService(ILogger<AudioDeviceService> logger, IAudioEndpointBackend backend)
    {
        _logger = logger;
        _backend = backend;
        _backend.Changed += OnNotification;
        Refresh();
    }

    public IReadOnlyList<AudioDevice> GetAllDevices()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            return Array.AsReadOnly(_devices);
        }
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices() => GetAllDevices().Where(d => d.IsConnected).ToArray();
    public AudioDevice? GetDeviceById(string deviceId) => GetAllDevices().FirstOrDefault(d => d.Id == deviceId);
    public AudioDevice? GetDefaultDevice(DeviceRole role = DeviceRole.Default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            try
            {
                var id = _backend.GetDefaultId(role == DeviceRole.Communications ? Role.Communications : Role.Console);
                return _devices.FirstOrDefault(d => d.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Default endpoint read failed");
                return null;
            }
        }
    }

    public bool SetDefaultDevice(string deviceId, DeviceRole role = DeviceRole.Both)
    {
        try
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (string.IsNullOrWhiteSpace(deviceId) || !Enum.IsDefined(role))
                    return false;
                try
                {
                    if (!_backend.Snapshot().Any(d => d.Id == deviceId && d.IsConnected))
                        return false;
                    var roles = role switch
                    {
                        DeviceRole.Default => new[]
                        {
                            Role.Console,
                            Role.Multimedia
                        },
                        DeviceRole.Communications => new[]
                        {
                            Role.Communications
                        },
                        _ => new[]
                        {
                            Role.Console,
                            Role.Multimedia,
                            Role.Communications
                        }
                    };
                    foreach (var r in roles)
                        _backend.SetDefault(deviceId, r);
                    return roles.All(r => _backend.GetDefaultId(r) == deviceId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Policy failed; roles may be partially changed for {DeviceId}", deviceId);
                    return false;
                }
            }
        }
        finally
        {
            // Publish subscriber events only after releasing the mutation lock.
            Refresh();
        }
    }

    public void StartMonitoring()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_monitoring)
                return;
            _backend.StartMonitoring();
            _monitoring = true;
        }

        Refresh();
    }

    public void StopMonitoring()
    {
        lock (_gate)
        {
            if (!_monitoring)
                return;
            _backend.StopMonitoring();
            _monitoring = false;
        }
    }

    void OnNotification()
    {
        lock (_gate)
        {
            if (_disposed || !_monitoring)
                return;
        }

        Refresh();
    }

    void Refresh()
    {
        List<DeviceEvent> events = new();
        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                var next = _backend.Snapshot().ToArray();
                var previous = _devices;
                _devices = next;
                foreach (var item in next)
                {
                    var old = previous.FirstOrDefault(d => d.Id == item.Id);
                    if (old == null)
                        events.Add(new(item.IsConnected ? DeviceEventType.Connected : DeviceEventType.Disconnected, item, DateTime.UtcNow));
                    else
                    {
                        if (old.IsConnected != item.IsConnected)
                            events.Add(new(item.IsConnected ? DeviceEventType.Connected : DeviceEventType.Disconnected, item, DateTime.UtcNow));
                        if (old.IsDefault != item.IsDefault || old.IsCommunicationsDefault != item.IsCommunicationsDefault)
                            events.Add(new(DeviceEventType.DefaultChanged, item, DateTime.UtcNow));
                        if (old.FriendlyName != item.FriendlyName || old.Type != item.Type)
                            events.Add(new(DeviceEventType.PropertyChanged, item, DateTime.UtcNow));
                    }
                }

                foreach (var old in previous.Where(o => !next.Any(n => n.Id == o.Id)))
                    events.Add(new(DeviceEventType.Disconnected, old with { IsConnected = false }, DateTime.UtcNow));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Endpoint refresh failed; retaining snapshot");
            }
        }

        foreach (var item in events)
            foreach (EventHandler<DeviceEvent> handler in DeviceChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
                try
                {
                    handler(this, item);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Device subscriber failed");
                }
    }

    void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            try { StopMonitoring(); }
            finally
            {
                _disposed = true;
                _backend.Changed -= OnNotification;
                _backend.Dispose();
            }
        }
    }
}
