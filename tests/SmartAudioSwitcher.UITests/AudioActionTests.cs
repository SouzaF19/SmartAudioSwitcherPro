using SmartAudioSwitcher.App.Services;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Xunit;
public class AudioActionTests
{
 [Fact] public void Cycle_without_devices_is_safe() => Assert.False(new AudioActions(new FakeDevices(),()=>false).Cycle());
 [Fact] public void Cycle_propagates_native_failure() {var f=new FakeDevices();f.Devices.Add(Device("a"));Assert.False(new AudioActions(f,()=>false).Cycle());Assert.Equal("a",f.Requested);}
 [Fact] public void Mute_propagates_failure() => Assert.False(new AudioActions(new FakeDevices(),()=>false).ToggleMute());
 static AudioDevice Device(string id)=>new(id,id,"",DeviceType.Speakers,DeviceRole.Default,true,false,false,null);
 sealed class FakeDevices:IAudioDeviceService
 {
  public List<AudioDevice> Devices=new();public string? Requested;
  public IReadOnlyList<AudioDevice> GetAllDevices()=>Devices;
  public IReadOnlyList<AudioDevice> GetOutputDevices()=>Devices;
  public AudioDevice? GetDeviceById(string id)=>Devices.FirstOrDefault(d=>d.Id==id);
  public AudioDevice? GetDefaultDevice(DeviceRole role=DeviceRole.Default)=>null;
  public bool SetDefaultDevice(string id,DeviceRole role=DeviceRole.Both){Requested=id;return false;}
  public event EventHandler<DeviceEvent>? DeviceChanged {add{} remove{}}
  public void StartMonitoring(){} public void StopMonitoring(){}
 }
}
