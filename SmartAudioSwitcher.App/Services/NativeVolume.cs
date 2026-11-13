using NAudio.CoreAudioApi;
namespace SmartAudioSwitcher.App.Services;

internal static class NativeVolume
{
    public static bool ToggleMute()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            var expected = !endpoint.AudioEndpointVolume.Mute;
            endpoint.AudioEndpointVolume.Mute = expected;
            return endpoint.AudioEndpointVolume.Mute == expected;
        }
        catch { return false; }
    }
}
