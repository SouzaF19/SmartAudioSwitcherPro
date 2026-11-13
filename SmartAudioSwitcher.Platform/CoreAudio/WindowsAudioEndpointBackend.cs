using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using SmartAudioSwitcher.Core.Models;
using System.Runtime.InteropServices;

namespace SmartAudioSwitcher.Platform.CoreAudio;
/// <summary>MMDevice notifications and render snapshots. Policy activation is lazy, never during enumeration.</summary>
public sealed class WindowsAudioEndpointBackend : IAudioEndpointBackend, IMMNotificationClient
{
    readonly MMDeviceEnumerator _enumerator = new();
    readonly object _gate = new();
    bool _monitoring, _disposed;
    public event Action? Changed;
    public IReadOnlyList<AudioDevice> Snapshot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var console = GetDefaultId(Role.Console);
            var comm = GetDefaultId(Role.Communications);
            var result = new List<AudioDevice>();
            foreach (var endpoint in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All))
                using (endpoint)
                {
                    var id = endpoint.ID;
                    var name = endpoint.FriendlyName;
                    var d = id == console;
                    var c = id == comm;
                    result.Add(new(id, name, id, Classify(name), d && c ? DeviceRole.Both : c ? DeviceRole.Communications : DeviceRole.Default, endpoint.State == DeviceState.Active, d, c, null));
                }

            return result;
        }
    }

    static DeviceType Classify(string name)
    {
        name = name.ToLowerInvariant();
        if (name.Contains("virtual") || name.Contains("cable") || name.Contains("voicemeeter"))
            return DeviceType.Virtual;
        if (name.Contains("bluetooth") || name.Contains("hands-free"))
            return DeviceType.Bluetooth;
        if (name.Contains("hdmi") || name.Contains("display") || name.Contains("monitor"))
            return DeviceType.Hdmi;
        if (name.Contains("headset") || name.Contains("headphone"))
            return DeviceType.Headset;
        if (name.Contains("speaker") || name.Contains("realtek"))
            return DeviceType.Speakers;
        return DeviceType.Unknown;
    }

    public string? GetDefaultId(Role role)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
                return device.ID;
            }
            catch (COMException ex)when (ex.HResult == unchecked((int)0x80070490))
            {
                return null;
            }
        }
    }

    public void SetDefault(string id, Role role)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            PolicyConfig.SetDefaultEndpoint(id, role);
        }
    }

    public void StartMonitoring()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_monitoring)
                return;
            Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(this));
            _monitoring = true;
        }
    }

    public void StopMonitoring()
    {
        lock (_gate)
        {
            if (!_monitoring)
                return;
            Marshal.ThrowExceptionForHR(_enumerator.UnregisterEndpointNotificationCallback(this));
            _monitoring = false;
        }
    }

    // No enumeration, waits, or subscriber invocation from inside the COM callback.
    void Notify() => ThreadPool.QueueUserWorkItem(_ =>
    {
        Action? callback;
        lock (_gate)
        {
            if (_disposed || !_monitoring)
                return;
            callback = Changed;
        }

        try
        {
            callback?.Invoke();
        }
        catch
        {
        }
    });
    public void OnDeviceStateChanged(string id, DeviceState state) => Notify();
    public void OnDeviceAdded(string id) => Notify();
    public void OnDeviceRemoved(string id) => Notify();
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
    {
        if (flow == DataFlow.Render)
            Notify();
    }

    public void OnPropertyValueChanged(string id, PropertyKey key) => Notify();
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                StopMonitoring();
            }
            finally
            {
                _disposed = true;
                _enumerator.Dispose();
            }
        }
    }
}
