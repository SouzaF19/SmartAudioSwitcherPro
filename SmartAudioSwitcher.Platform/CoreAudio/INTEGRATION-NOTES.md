# Windows integration boundaries

- Default endpoint mutation uses IPolicyConfig (IID f8679f50-850a-41cf-9c72-430f290290c8, class 870af99c-171d-4f9e-af0d-e63df40c2bc9). This Windows ABI is undocumented. Every HRESULT is checked and each requested role is read back through MMDeviceEnumerator. Default means Console + Multimedia; Communications means only Communications; Both means all three. A partial policy failure returns false; this is not an atomic transaction and no automatic rollback overwrites intervening external changes.
- Notifications use RegisterEndpointNotificationCallback/UnregisterEndpointNotificationCallback, via NAudio. The COM callback queues work rather than blocking or enumerating. Snapshot publication precedes application events. Explicit Start/Stop and Dispose own registration lifetime.
- AudioSessionService enumerates all active render endpoints/sessions, retains system sounds and multiple sessions per PID, and polls at 500 ms. It never writes volume. Its public Core model has no native session-instance identity; polling matches by PID/endpoint/ordinal. Very short sessions or reordering of same-PID/same-endpoint sessions can be missed/misattributed. The singular PID lookup returns the first matching session, not proof of a persisted app preference.
- Per-app endpoint policy remains explicitly unsupported: SetSessionDevice logs the reason and returns false, never reports success. EarTrumpet's Windows policy references expose SetPersistedDefaultAudioEndpoint/GetPersistedDefaultAudioEndpoint, but version-specific WinRT factory ABI, endpoint HSTRING decoration, clearing/restoring preferences and behavior of existing streams cannot safely be validated here without changing user routing. No GUID/vtable for that API is guessed, and no external executable is required.
- SonicFlowEngine restoration, for an injected capable provider, restores a previously observed endpoint only; it is not equivalent to restoring/clearing a persisted Windows application preference. Missing previous state or an externally changed endpoint refuses removal. The production fail-closed session provider cannot create a route, so this limitation never masquerades as a real successful route.

References inspected:
- https://github.com/fredemmott/AudioDeviceLib/blob/master/src/PolicyConfig.h (MIT; notice alongside this file)
- https://github.com/naudio/NAudio/blob/master/NAudio.Wasapi/CoreAudioApi/MMDeviceEnumerator.cs
- https://github.com/naudio/NAudio/blob/master/NAudio.Wasapi/CoreAudioApi/Interfaces/IMMNotificationClient.cs
- https://github.com/naudio/NAudio/blob/master/NAudio.Wasapi/CoreAudioApi/AudioSessionControl.cs
- https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IAudioPolicyConfigFactory.cs (reference only; not copied)
- https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immnotificationclient

Tests/SmartAudioSwitcher.PlatformTests links production Platform sources plus only Core contracts/models to isolate the Platform tests from unrelated Core service build failures. No native COM constructor runs in these tests: endpoint I/O and session snapshots are injected fakes. Default mutation and notifications are verified at the abstraction boundary, not by changing actual user hardware.
