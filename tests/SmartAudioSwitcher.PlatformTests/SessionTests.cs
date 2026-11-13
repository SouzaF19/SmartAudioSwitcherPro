using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Platform.Services;
using SmartAudioSwitcher.Core.Interfaces;
using Xunit;

namespace SmartAudioSwitcher.PlatformTests;
public sealed class SessionTests
{
    [Fact]
    public void Polling_identifies_endpoint_changes_without_losing_same_pid_sessions()
    {
        var first = new AudioSession(42, "player", "Player", null, false, 1, false, "before");
        var now = new[]
        {
            first
        };
        using var service = new AudioSessionService(NullLogger<AudioSessionService>.Instance, () => Volatile.Read(ref now), TimeSpan.FromMilliseconds(20));
        using var changed = new ManualResetEventSlim();
        service.SessionChanged += (_, e) =>
        {
            if (e.Type == AudioSessionEventType.DeviceChanged)
                changed.Set();
        };
        service.StartMonitoring();
        Volatile.Write(ref now, new[] { first with { CurrentDeviceId = "after" } });
        Assert.True(changed.Wait(TimeSpan.FromSeconds(3)), "Expected endpoint-change event");
        service.StopMonitoring();
        Volatile.Write(ref now, new[] { first, first with { CurrentDeviceId = "after" } });
        Assert.Equal(2, service.GetActiveSessions().Count);
    }

    [Fact]
    public void Snapshot_enumerates_and_lookup_is_by_process_id()
    {
        var item = new AudioSession(42, "player", "Player", null, false, 0.75f, false, "endpoint");
        using var service = new AudioSessionService(NullLogger<AudioSessionService>.Instance, () => new[] { item });
        Assert.Equal(new[] { item }, service.GetActiveSessions());
        Assert.Equal(item, service.GetSessionByProcessId(42));
        Assert.Null(service.GetSessionByProcessId(999));
    }

    [Fact]
    public void Routing_is_fail_closed()
    {
        using var service = new AudioSessionService(NullLogger<AudioSessionService>.Instance, () => Array.Empty<AudioSession>());
        Assert.False(service.SetSessionDevice(42, "endpoint"));
    }

    [Fact]
    public void Polling_emits_created_and_disposed_once()
    {
        var now = Array.Empty<AudioSession>();
        using var service = new AudioSessionService(NullLogger<AudioSessionService>.Instance, () => now, TimeSpan.FromMilliseconds(40));
        var seen = new System.Collections.Concurrent.ConcurrentQueue<AudioSessionEventType>();
        service.SessionChanged += (_, e) => seen.Enqueue(e.Type);
        service.StartMonitoring();
        now = new[]
        {
            new AudioSession(42, "player", "Player", null, false, 1, false, "endpoint")
        };
        Thread.Sleep(180);
        now = Array.Empty<AudioSession>();
        Thread.Sleep(180);
        service.StopMonitoring();
        Assert.Equal(new[] { AudioSessionEventType.Created, AudioSessionEventType.Disposed }, seen.ToArray());
    }
}
