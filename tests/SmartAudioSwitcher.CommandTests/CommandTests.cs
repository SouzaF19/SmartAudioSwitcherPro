using SmartAudioSwitcher.Core.Services;
using SmartAudioSwitcher.Core.Models;
using Xunit;
public class CommandTests
{
 [Fact] public async Task Version_does_not_initialize_hardware() => Assert.Equal(0, await SmartAudioSwitcher.CLI.Program.RunAsync(new[]{"version"}, () => throw new Exception("Hardware must stay lazy")));
 [Fact] public async Task Help_does_not_initialize_hardware() => Assert.Equal(0, await SmartAudioSwitcher.CLI.Program.RunAsync(new[]{"--help"}, () => throw new Exception("Hardware must stay lazy")));
 [Fact] public void Empty_cycle_returns_no_target() => Assert.Null(DeviceCycle.Next(Array.Empty<string>(), null, x=>x, null));
 [Fact] public void Api_is_disabled_by_default() => Assert.False(ApiHostOptions.Resolve(new ApiSettings()).Enabled);
 [Theory] [InlineData(0)] [InlineData(65536)] public void Api_rejects_invalid_port(int port) => Assert.NotNull(ApiHostOptions.Resolve(new ApiSettings{Enabled=true,Port=port}).Error);
 [Fact] public void Api_rejects_remote_without_authentication() => Assert.NotNull(ApiHostOptions.Resolve(new ApiSettings{Enabled=true,AllowRemote=true}).Error);
}
