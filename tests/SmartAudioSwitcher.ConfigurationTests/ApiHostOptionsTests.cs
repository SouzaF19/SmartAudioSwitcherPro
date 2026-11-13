using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using Xunit;

public class ApiHostOptionsTests
{
 [Fact] public void Remote_is_rejected() { var r=ApiHostOptions.Resolve(new ApiSettings { Enabled=true, AllowRemote=true }); Assert.False(r.Enabled); Assert.Contains("authentication",r.Error); }
 [Fact] public void Enabled_binds_loopback() { var r=ApiHostOptions.Resolve(new ApiSettings { Enabled=true, Port=1234 }); Assert.Equal("http://127.0.0.1:1234",r.Url); }
 [Fact] public void Invalid_port_rejected() { Assert.NotNull(ApiHostOptions.Resolve(new ApiSettings { Port=0 }).Error); }
 [Fact] public void Disabled_stays_disabled() { Assert.False(ApiHostOptions.Resolve(new ApiSettings { Enabled=false }).Enabled); }
}
