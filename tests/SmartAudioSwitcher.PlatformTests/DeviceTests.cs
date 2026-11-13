using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Platform.CoreAudio;
using SmartAudioSwitcher.Platform.Services;
using NAudio.CoreAudioApi;

namespace SmartAudioSwitcher.PlatformTests;
public class DeviceTests
{
    public static AudioDevice Device(string id = "speaker") => new(id, id, id, DeviceType.Speakers, DeviceRole.Default, true, false, false, null);
    [Fact]
    public void Default_sets_console_and_multimedia_and_checks_readback()
    {
        using var backend = new FakeEndpoints();
        using var service = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, backend);
        Assert.True(service.SetDefaultDevice("speaker", DeviceRole.Default));
        Assert.Equal(new[] { Role.Console, Role.Multimedia }, backend.Writes);
    }

    [Fact]
    public void Both_sets_all_three_roles()
    {
        using var b = new FakeEndpoints();
        using var s = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, b);
        Assert.True(s.SetDefaultDevice("speaker", DeviceRole.Both));
        Assert.Equal(3, b.Writes.Count);
    }

    [Fact]
    public void Readback_mismatch_is_failure()
    {
        using var b = new FakeEndpoints
        {
            Accept = false
        };
        using var s = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, b);
        Assert.False(s.SetDefaultDevice("speaker"));
    }

    [Fact]
    public void Missing_device_is_not_written()
    {
        using var b = new FakeEndpoints();
        using var s = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, b);
        Assert.False(s.SetDefaultDevice("missing"));
        Assert.Empty(b.Writes);
    }

    [Fact]
    public void Notification_updates_snapshot_before_event_and_monitoring_is_idempotent()
    {
        using var b = new FakeEndpoints();
        using var s = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, b);
        s.StartMonitoring();
        s.StartMonitoring();
        bool observed = false;
        s.DeviceChanged += (_, e) => observed = s.GetDeviceById(e.Device.Id) != null;
        b.Devices.Add(Device("new"));
        b.Notify();
        Assert.True(observed);
        Assert.Equal(1, b.Starts);
        var snapshot = s.GetAllDevices();
        b.Devices.Clear();
        b.Notify();
        Assert.Equal(2, snapshot.Count);
        s.StopMonitoring();
        s.StopMonitoring();
        Assert.Equal(1, b.Stops);
    }

    [Fact]
    public void Newly_seen_inactive_endpoint_is_not_reported_connected()
    {
        using var backend = new FakeEndpoints();
        backend.Devices.Clear();
        using var service = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, backend);
        DeviceEvent? received = null;
        service.DeviceChanged += (_, e) => received = e;
        service.StartMonitoring();
        backend.Devices.Add(Device() with { IsConnected = false });
        backend.Notify();
        Assert.NotNull(received);
        Assert.Equal(DeviceEventType.Disconnected, received.Type);
    }

    [Fact]
    public void Dispose_releases_backend_even_if_unregistration_fails()
    {
        var backend = new FakeEndpoints { FailStop = true };
        var service = new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, backend);
        service.StartMonitoring();
        try { service.Dispose(); } catch (InvalidOperationException) { }
        Assert.True(backend.Disposed);
        Assert.Throws<ObjectDisposedException>(() => service.GetAllDevices());
    }

    public sealed class FakeEndpoints : IAudioEndpointBackend
    {
        public List<AudioDevice> Devices = new()
        {
            Device()
        };
        public bool Accept = true;
        public List<Role> Writes = new();
        public int Starts, Stops;
        public bool FailStop, Disposed;
        readonly Dictionary<Role, string> defaults = new();
        public event Action? Changed;
        public IReadOnlyList<AudioDevice> Snapshot() => Devices.ToArray();
        public string? GetDefaultId(Role role) => defaults.GetValueOrDefault(role);
        public void SetDefault(string id, Role role)
        {
            Writes.Add(role);
            if (Accept)
                defaults[role] = id;
        }

        public void StartMonitoring() => Starts++;
        public void StopMonitoring() { Stops++; if (FailStop) throw new InvalidOperationException("Unregister failed"); }
        public void Notify() => Changed?.Invoke();
        public void Dispose() { Disposed = true; }
    }
}
