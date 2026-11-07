using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SmartAudioSwitcher.Platform.CoreAudio;

[SupportedOSPlatform("windows")]
internal static class MMDeviceApi
{
    // This file provides constants and type definitions for reference
    // Actual implementation uses NAudio.CoreAudioApi which wraps these COM interfaces
    
    public static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    
    public enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2,
    }

    public enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    public enum EDeviceState
    {
        DEVICE_STATE_ACTIVE = 0x00000001,
        DEVICE_STATE_DISABLED = 0x00000002,
        DEVICE_STATE_NOTPRESENT = 0x00000004,
        DEVICE_STATE_UNPLUGGED = 0x00000008,
        DEVICE_STATEMASK_ALL = 0x0000000F
    }

    public enum AudioSessionState
    {
        AudioSessionStateInactive = 0,
        AudioSessionStateActive = 1,
        AudioSessionStateExpired = 2
    }

    public enum ConnectorType
    {
        ConnectorTypeUnknown = 0,
        ConnectorTypePhysicalInternal = 1,
        ConnectorTypePhysicalExternal = 2,
        ConnectorTypeSoftwareIo = 3,
        ConnectorTypeSoftwareFixed = 4,
        ConnectorTypeNetwork = 5
    }
}