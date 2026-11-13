using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Platform.Services;

namespace SmartAudioSwitcher.PlatformTests;
public class RouteTests
{
    static VisualRoute Route(string pattern = "player.exe") => new("app", "Player", NodeType.App, pattern, null, "out", "speaker", NodeType.Device, null, "speaker");
    static ProcessInfo Process(int id, string name = "player") => new(id, name, null, null, DateTime.UnixEpoch, false);
    static SonicFlowEngine Engine(FakeSessions sessions) => new(NullLogger<SonicFlowEngine>.Instance, new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, new DeviceTests.FakeEndpoints()), sessions, null !, null !);
    [Fact]
    public async Task Exe_wildcard_routes_all_matching_processes()
    {
        var sessions = new FakeSessions();
        using var e = Engine(sessions);
        Assert.Equal(2, await e.ApplyRoutesAsync(new[] { Route("play*.exe") }, new[] { DeviceTests.Device() }, new[] { Process(1), Process(2), Process(3, "other") }));
        Assert.Equal(new[] { 1, 2 }, sessions.Writes.Select(w => w.Pid));
    }

    [Fact]
    public async Task Remove_by_name_restores_previous_endpoint()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        using var e = Engine(s);
        await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        Assert.True(await e.RemoveRouteAsync("PLAYER.EXE", "speaker"));
        Assert.Equal("before", s.Current[7]);
    }

    [Fact]
    public async Task Reapply_preserves_original_endpoint()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        using var e = Engine(s);
        for (int i = 0; i < 2; i++)
            await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        Assert.True(await e.RemoveRouteAsync("player", "speaker"));
        Assert.Equal("before", s.Current[7]);
    }

    [Fact]
    public async Task Missing_previous_endpoint_cannot_report_restored()
    {
        var s = new FakeSessions();
        using var e = Engine(s);
        await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        Assert.False(await e.RemoveRouteAsync("player", "speaker"));
    }

    [Fact]
    public async Task External_change_is_not_overwritten_by_remove()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        using var e = Engine(s);
        await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        s.Current[7] = "external";
        Assert.False(await e.RemoveRouteAsync("player", "speaker"));
        Assert.Equal("external", s.Current[7]);
    }

    [Fact]
    public void Disconnected_target_is_invalid()
    {
        var s = new FakeSessions();
        using var e = Engine(s);
        var result = e.ValidateRoutes(new[] { Route("player") }, new[] { DeviceTests.Device()with { IsConnected = false } }, new[] { Process(1) });
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Failed_restore_retains_route_for_retry()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        using var e = Engine(s);
        await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        s.Accept = false;
        Assert.False(await e.RemoveRouteAsync("player", "speaker"));
        s.Accept = true;
        Assert.True(await e.RemoveRouteAsync("player", "speaker"));
    }

    [Fact]
    public async Task Reused_pid_cannot_restore_another_process()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        var monitor = new FakeProcessMonitor();
        using var e = new SonicFlowEngine(NullLogger<SonicFlowEngine>.Instance, new AudioDeviceService(NullLogger<AudioDeviceService>.Instance, new DeviceTests.FakeEndpoints()), s, monitor, null !);
        await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        monitor.Current = Process(7)with
        {
            StartTime = DateTime.UnixEpoch.AddDays(1)
        };
        Assert.False(await e.RemoveRouteAsync("player", "speaker"));
        Assert.Equal("speaker", s.Current[7]);
    }

    [Fact]
    public async Task Restore_requires_readback_not_just_provider_success()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        using var e = Engine(s);
        await e.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        s.Reflect = false;
        Assert.False(await e.RemoveRouteAsync("player", "speaker"));
        s.Reflect = true;
        Assert.True(await e.RemoveRouteAsync("player", "speaker"));
    }

    [Fact]
    public async Task Multiple_original_endpoints_cannot_be_collapsed_into_a_fake_restore()
    {
        var s = new FakeSessions();
        s.Current[7] = "before";
        s.Additional.Add(new AudioSession(7,"player","Other stream",null,false,1,false,"other-original"));
        using var engine = Engine(s);
        await engine.ApplyRoutesAsync(new[] { Route("player") }, new[] { DeviceTests.Device() }, new[] { Process(7) });
        Assert.False(await engine.RemoveRouteAsync("player", "speaker"));
    }

    [Fact]
    public void Empty_process_pattern_falls_back_to_source_title()
    {
        using var engine = Engine(new FakeSessions());
        var route = Route("") with { SourceTitle = "player" };
        Assert.True(engine.ValidateRoutes(new[] { route },new[] { DeviceTests.Device() },new[] { Process(7) }).IsValid);
    }

    public class FakeProcessMonitor : IProcessMonitorService
    {
        public ProcessInfo? Current = Process(7);
        public event EventHandler<ProcessEvent>? ProcessEvent
        {
            add
            {
            }

            remove
            {
            }
        }

        public IReadOnlyList<ProcessInfo> GetRunningProcesses() => Current == null ? Array.Empty<ProcessInfo>() : new[]
        {
            Current
        };
        public ProcessInfo? GetProcessById(int id) => Current?.ProcessId == id ? Current : null;
        public bool IsProcessRunning(string name) => Current?.ProcessName == name;
        public ProcessInfo? GetForegroundProcess() => Current;
        public void StartMonitoring()
        {
        }

        public void StopMonitoring()
        {
        }
    }

    public class FakeSessions : IAudioSessionService
    {
        public Dictionary<int, string> Current = new();
        public List<AudioSession> Additional = new();
        public List<(int Pid, string Device)> Writes = new();
        public bool Accept = true, Reflect = true;
        public event EventHandler<AudioSessionEvent>? SessionChanged
        {
            add
            {
            }

            remove
            {
            }
        }

        public IReadOnlyList<AudioSession> GetActiveSessions() => Current.Select(x => new AudioSession(x.Key, "player", "Player", null, false, 1, false, x.Value)).Concat(Additional).ToArray();
        public AudioSession? GetSessionByProcessId(int id) => GetActiveSessions().FirstOrDefault(x => x.ProcessId == id);
        public bool SetSessionDevice(int id, string device)
        {
            Writes.Add((id, device));
            if (Accept && Reflect)
                Current[id] = device;
            return Accept;
        }

        public void StartMonitoring()
        {
        }

        public void StopMonitoring()
        {
        }
    }
}
