using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.Platform.Services;

public class AudioSessionService : IAudioSessionService, IDisposable
{
    private readonly ILogger<AudioSessionService> _logger;
    private readonly MMDeviceEnumerator _enumerator;
    private bool _disposed;

    public event EventHandler<AudioSessionEvent>? SessionChanged;

    public AudioSessionService(ILogger<AudioSessionService> logger)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
    }

    public IReadOnlyList<AudioSession> GetActiveSessions()
    {
        return Array.Empty<AudioSession>();
    }

    public AudioSession? GetSessionByProcessId(int processId)
    {
        return null;
    }

    public bool SetSessionDevice(int processId, string deviceId)
    {
        _logger.LogInformation("Per-app audio routing not yet implemented");
        return false;
    }

    public void StartMonitoring()
    {
        _logger.LogDebug("Audio session monitoring started");
    }

    public void StopMonitoring()
    {
        _logger.LogDebug("Audio session monitoring stopped");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _enumerator.Dispose();
            _disposed = true;
        }
    }
}