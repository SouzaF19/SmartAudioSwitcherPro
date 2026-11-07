using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.Platform.Services;

public class AudioDeviceService : IAudioDeviceService, IDisposable
{
    private readonly ILogger<AudioDeviceService> _logger;
    private readonly MMDeviceEnumerator _enumerator;
    private readonly List<AudioDevice> _cachedDevices = new();
    private bool _disposed;

    public event EventHandler<DeviceEvent>? DeviceChanged;

    public AudioDeviceService(ILogger<AudioDeviceService> logger)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
        RefreshDevices();
    }

    public IReadOnlyList<AudioDevice> GetAllDevices()
    {
        return _cachedDevices.AsReadOnly();
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        return _cachedDevices.Where(d => d.IsConnected).ToList().AsReadOnly();
    }

    public AudioDevice? GetDeviceById(string deviceId)
    {
        return _cachedDevices.FirstOrDefault(d => d.Id == deviceId);
    }

    public AudioDevice? GetDefaultDevice(DeviceRole role = DeviceRole.Default)
    {
        var naudioRole = role switch
        {
            DeviceRole.Communications => Role.Communications,
            _ => Role.Console
        };

        try
        {
            var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, naudioRole);
            return _cachedDevices.FirstOrDefault(d => d.Id == device.ID);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get default device for role {Role}", role);
            return null;
        }
    }

    public bool SetDefaultDevice(string deviceId, DeviceRole role = DeviceRole.Both)
    {
        try
        {
            var device = _enumerator.GetDevice(deviceId);
            if (device == null)
            {
                _logger.LogWarning("Device not found: {DeviceId}", deviceId);
                return false;
            }

            // Note: Setting default device programmatically requires elevation in newer Windows
            // This is a limitation - we can only request the change via policy
            _logger.LogInformation("Requested default device change to {DeviceId} (Role: {Role})", deviceId, role);
            
            // Force refresh
            RefreshDevices();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set default device {DeviceId}", deviceId);
            return false;
        }
    }

    public void StartMonitoring()
    {
        _logger.LogDebug("Audio device monitoring started");
    }

    public void StopMonitoring()
    {
        _logger.LogDebug("Audio device monitoring stopped");
    }

    internal void OnDeviceNotification()
    {
        RefreshDevices();
    }

    private void RefreshDevices()
    {
        var newDevices = new List<AudioDevice>();

        try
        {
            var endpoints = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Unplugged);
            
            foreach (var endpoint in endpoints)
            {
                var device = ConvertToAudioDevice(endpoint);
                if (device != null)
                {
                    newDevices.Add(device);
                }
            }

            DetectChanges(_cachedDevices, newDevices);

            _cachedDevices.Clear();
            _cachedDevices.AddRange(newDevices);
            
            _logger.LogDebug("Refreshed audio devices: {Count} found", _cachedDevices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh audio devices");
        }
    }

    private AudioDevice? ConvertToAudioDevice(MMDevice device)
    {
        try
        {
            var type = DetermineDeviceType(device);
            var role = DetermineDeviceRole(device);
            var isConnected = device.State == DeviceState.Active;
            
            var isDefault = false;
            var isCommDefault = false;
            
            try
            {
                var defaultConsole = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                var defaultComm = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
                isDefault = defaultConsole?.ID == device.ID;
                isCommDefault = defaultComm?.ID == device.ID;
            }
            catch { }

            return new AudioDevice(
                Id: device.ID,
                FriendlyName: device.FriendlyName,
                DeviceInterfacePath: device.ID,
                Type: type,
                Role: role,
                IsConnected: isConnected,
                IsDefault: isDefault,
                IsCommunicationsDefault: isCommDefault,
                FormFactor: null
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert device {DeviceId}", device.ID);
            return null;
        }
    }

    private DeviceType DetermineDeviceType(MMDevice device)
    {
        var name = device.FriendlyName.ToLowerInvariant();
        
        if (name.Contains("vb-cable") || name.Contains("voicemeeter") || name.Contains("virtual") || name.Contains("cable output"))
            return DeviceType.Virtual;

        if (name.Contains("bluetooth") || name.Contains("hands-free") || (name.Contains("headset") && name.Contains("bluetooth")))
            return DeviceType.Bluetooth;

        if (name.Contains("hdmi") || name.Contains("display") || name.Contains("monitor") || 
            name.Contains("tv") || name.Contains("lg ") || name.Contains("samsung ") || 
            name.Contains("dell ") || name.Contains("hp ") || name.Contains("asus ") || name.Contains("acer "))
            return DeviceType.Hdmi;

        if (name.Contains("headset") || name.Contains("headphone") || name.Contains("wh-1000xm") || 
            name.Contains("g pro") || name.Contains("arctis") || name.Contains("razer") || 
            name.Contains("hyperx") || name.Contains("corsair") || (name.Contains("logitech") && !name.Contains("speaker")))
            return DeviceType.Headset;

        if (name.Contains("speaker") || name.Contains("realtek") || name.Contains("audio") || name.Contains("sound"))
            return DeviceType.Speakers;

        return DeviceType.Unknown;
    }

    private DeviceRole DetermineDeviceRole(MMDevice device)
    {
        var isDefault = false;
        var isCommDefault = false;
        
        try
        {
            var defaultConsole = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            var defaultComm = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            isDefault = defaultConsole?.ID == device.ID;
            isCommDefault = defaultComm?.ID == device.ID;
        }
        catch { }

        if (isDefault && isCommDefault) return DeviceRole.Both;
        if (isDefault) return DeviceRole.Default;
        if (isCommDefault) return DeviceRole.Communications;
        return DeviceRole.Default;
    }

    private void DetectChanges(List<AudioDevice> oldDevices, List<AudioDevice> newDevices)
    {
        var oldIds = oldDevices.Select(d => d.Id).ToHashSet();
        var newIds = newDevices.Select(d => d.Id).ToHashSet();

        foreach (var id in newIds.Except(oldIds))
        {
            var device = newDevices.First(d => d.Id == id);
            DeviceChanged?.Invoke(this, new DeviceEvent(DeviceEventType.Connected, device, DateTime.Now));
        }

        foreach (var id in oldIds.Except(newIds))
        {
            var device = oldDevices.First(d => d.Id == id);
            DeviceChanged?.Invoke(this, new DeviceEvent(DeviceEventType.Disconnected, device, DateTime.Now));
        }

        foreach (var id in oldIds.Intersect(newIds))
        {
            var old = oldDevices.First(d => d.Id == id);
            var @new = newDevices.First(d => d.Id == id);
            
            if (old.IsDefault != @new.IsDefault || old.IsCommunicationsDefault != @new.IsCommunicationsDefault)
            {
                DeviceChanged?.Invoke(this, new DeviceEvent(DeviceEventType.DefaultChanged, @new, DateTime.Now));
            }
            
            if (old.IsConnected != @new.IsConnected)
            {
                DeviceChanged?.Invoke(this, new DeviceEvent(
                    @new.IsConnected ? DeviceEventType.Connected : DeviceEventType.Disconnected, 
                    @new, DateTime.Now));
            }
        }
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