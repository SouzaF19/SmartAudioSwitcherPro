using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
namespace SmartAudioSwitcher.App.Services;

public sealed class AudioActions(IAudioDeviceService devices, Func<bool> toggleMute)
{
    public bool Cycle()
    {
        var outputs = devices.GetOutputDevices().Where(d => d.IsConnected).ToList();
        if (outputs.Count == 0) return false;
        var current = devices.GetDefaultDevice();
        var index = outputs.FindIndex(d => d.Id == current?.Id);
        return devices.SetDefaultDevice(outputs[(index + 1) % outputs.Count].Id);
    }
    public bool ForceHeadset()
    {
        var headset = devices.GetOutputDevices().FirstOrDefault(d => d.IsConnected && d.Type == DeviceType.Headset);
        return headset != null && devices.SetDefaultDevice(headset.Id);
    }
    public bool ToggleMute() => toggleMute();
}
