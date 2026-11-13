using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Platform.CoreAudio;
using SmartAudioSwitcher.Platform.Services;
using NAudio.CoreAudioApi;
using Xunit;
namespace SmartAudioSwitcher.PlatformTests;
public class EventLockTests
{
 [Fact] public void Default_change_subscriber_can_read_snapshot_from_another_thread()
 {
  using var backend=new Endpoints();using var service=new AudioDeviceService(NullLogger<AudioDeviceService>.Instance,backend);
  bool read=false;
  service.DeviceChanged+=(_,e)=> {if(e.Type==DeviceEventType.DefaultChanged) read=Task.Run(()=>service.GetAllDevices()).Wait(TimeSpan.FromSeconds(1));};
  Assert.True(service.SetDefaultDevice("speaker"));Assert.True(read,"Subscriber ran under the service lock");
 }
 [Fact] public void Session_subscriber_can_read_snapshot_from_another_thread()
 {
  AudioSession[] current=Array.Empty<AudioSession>();
  using var service=new AudioSessionService(NullLogger<AudioSessionService>.Instance,()=>Volatile.Read(ref current),TimeSpan.FromMilliseconds(20));
  using var delivered=new ManualResetEventSlim();bool read=false;
  service.SessionChanged+=(_,e)=>{read=Task.Run(()=>service.GetActiveSessions()).Wait(TimeSpan.FromSeconds(1));delivered.Set();};
  service.StartMonitoring();Volatile.Write(ref current,new[]{new AudioSession(1,"a","a",null,false,1,false,"speaker")});
  Assert.True(delivered.Wait(TimeSpan.FromSeconds(4)));service.StopMonitoring();Assert.True(read,"Session subscriber ran under the service lock");
 }
 sealed class Endpoints:IAudioEndpointBackend
 {
  bool selected;public event Action? Changed {add{} remove{}}
  public IReadOnlyList<AudioDevice> Snapshot()=>new[]{new AudioDevice("speaker","Speaker","",DeviceType.Speakers,DeviceRole.Default,true,selected,selected,null)};
  public string? GetDefaultId(Role role)=>selected?"speaker":null;
  public void SetDefault(string id,Role role)=>selected=true;
  public void StartMonitoring(){} public void StopMonitoring(){}public void Dispose(){}
 }
}
