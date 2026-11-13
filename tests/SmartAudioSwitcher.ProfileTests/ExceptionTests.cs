using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using Xunit;
public class ExceptionTests
{
 [Theory] [InlineData("Discord","discord.EXE")] [InlineData("TeamSpeak3","TeamSpeak*")] [InlineData("battle.net","battle.net.exe")]
 public void Process_exceptions_normalize_exe_and_wildcards(string process,string pattern)
 {
  var engine=new ProfileEngine(NullLogger<ProfileEngine>.Instance);
  var profile=new Profile{Name="Gaming",DefaultDeviceType="speakers",ProcessExceptions=new(){[pattern]="headset"}};
  AudioDevice D(string id,DeviceType type)=>new(id,id,"",type,DeviceRole.Default,true,false,false,null);
  var decision=engine.DecideRouting(profile,null,new ProcessInfo(1,process,null,null,DateTime.Now,false),new[]{D("speaker",DeviceType.Speakers),D("headset",DeviceType.Headset)});
  Assert.Equal("headset",decision.DeviceId);
 }
}
