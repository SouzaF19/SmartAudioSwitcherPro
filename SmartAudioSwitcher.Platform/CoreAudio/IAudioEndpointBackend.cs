using NAudio.CoreAudioApi;
using SmartAudioSwitcher.Core.Models;

namespace SmartAudioSwitcher.Platform.CoreAudio;
/// <summary>Endpoint I/O boundary. Snapshot is render endpoints only; policy calls must throw on failed HRESULT.</summary>
public interface IAudioEndpointBackend : IDisposable
{
    event Action? Changed;
    IReadOnlyList<AudioDevice> Snapshot();
    string? GetDefaultId(Role role);
    void SetDefault(string id, Role role);
    void StartMonitoring();
    void StopMonitoring();
}
