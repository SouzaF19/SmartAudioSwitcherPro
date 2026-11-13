using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using Xunit;

public class ProfileEngineTests
{
    static ProfileTrigger T(ProfileTriggerType type, string? value = null) => new(type, value, null);
    static ProcessInfo Proc(string name) => new(1, name, null, null, DateTime.Now, true);

    [Theory]
    [InlineData("GAME.EXE", "game.exe")]
    [InlineData("game", "game.exe")]
    [InlineData("game.exe", "game")]
    public void Process_match_is_case_insensitive_and_normalizes_exe(string process, string pattern)
    {
        var engine = new ProfileEngine(NullLogger<ProfileEngine>.Instance);
        var p = new Profile { Name = "Game", Triggers = new() { T(ProfileTriggerType.ProcessMatch, pattern) } };
        engine.ReloadConfiguration(new() { Profiles = new() { p } });
        Assert.Same(p, engine.EvaluateProfile(Array.Empty<AudioDevice>(), new[] { Proc(process) }));
    }

    [Fact]
    public void Wildcard_matches_middle_case_insensitively()
    {
        var engine = new ProfileEngine(NullLogger<ProfileEngine>.Instance);
        var p = new Profile { Name = "x", Triggers = new() { T(ProfileTriggerType.ProcessMatch, "*GAM*") } };
        engine.ReloadConfiguration(new() { Profiles = new() { p } });
        Assert.Same(p, engine.EvaluateProfile(Array.Empty<AudioDevice>(), new[] { Proc("game.exe") }));
    }

    [Fact]
    public void Disabled_default_is_not_fallback()
    {
        var engine = new ProfileEngine(NullLogger<ProfileEngine>.Instance);
        engine.ReloadConfiguration(new() { Profiles = new() { new Profile { Name = "Default", Enabled = false } } });
        Assert.Null(engine.EvaluateProfile(Array.Empty<AudioDevice>(), Array.Empty<ProcessInfo>()));
    }

    [Fact]
    public void Dock_condition_uses_provider_not_hardcoded_true()
    {
        var engine = new ProfileEngine(NullLogger<ProfileEngine>.Instance, TimeProvider.System, () => false);
        var p = new Profile { Name = "Dock", Triggers = new() { T(ProfileTriggerType.DockedState, "true") } };
        engine.ReloadConfiguration(new() { Profiles = new() { p } });
        Assert.NotSame(p, engine.EvaluateProfile(Array.Empty<AudioDevice>(), Array.Empty<ProcessInfo>()));
    }

    [Fact]
    public void Time_and_dock_triggers_are_both_required()
    {
        var clock = new FrozenTimeProvider(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero));
        var engine = new ProfileEngine(NullLogger<ProfileEngine>.Instance, clock, () => false);
        var p = new Profile { Name = "Work", Triggers = new() { T(ProfileTriggerType.TimeBetween, "09:00-17:00"), T(ProfileTriggerType.DockedState, "true") } };
        engine.ReloadConfiguration(new() { Profiles = new() { p } });
        Assert.NotSame(p, engine.EvaluateProfile(Array.Empty<AudioDevice>(), Array.Empty<ProcessInfo>()));
    }

    sealed class FrozenTimeProvider(DateTimeOffset now):TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
