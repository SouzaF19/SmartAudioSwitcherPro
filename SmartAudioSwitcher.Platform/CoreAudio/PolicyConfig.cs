using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace SmartAudioSwitcher.Platform.CoreAudio;
// ABI reference: fredemmott/AudioDeviceLib src/PolicyConfig.h (IPolicyConfig, Windows 7+).
// GUIDs and all 12 method slots retained in order. Only SetDefaultEndpoint is called.
// Upstream license/attribution in THIRD-PARTY-NOTICES.md. This is an undocumented Windows interface.
internal static class PolicyConfig
{
    public static void SetDefaultEndpoint(string id, Role role)
    {
        object? client = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"), throwOnError: true)!;
            client = Activator.CreateInstance(type)!;
            Marshal.ThrowExceptionForHR(((IPolicyConfig)client).SetDefaultEndpoint(id, role));
        }
        finally
        {
            if (client != null && Marshal.IsComObject(client))
                Marshal.ReleaseComObject(client);
        }
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig]
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig]
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int @default, out IntPtr format);
        [PreserveSig]
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig]
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig]
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int @default, out long period, out long minimum);
        [PreserveSig]
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
        [PreserveSig]
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig]
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig]
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig]
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, Role role);
        [PreserveSig]
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
}
