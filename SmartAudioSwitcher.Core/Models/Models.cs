namespace SmartAudioSwitcher.Core.Models;

public enum DeviceType
{
    Unknown,
    Headset,
    Speakers,
    Hdmi,
    Bluetooth,
    Virtual,
    Monitor
}

public enum DeviceRole
{
    Default,
    Communications,
    Both
}

public enum ProfileTriggerType
{
    ProcessMatch,
    WindowTitleMatch,
    TimeBetween,
    DockedState,
    DeviceConnected,
    DeviceDisconnected,
    Always
}

public record AudioDevice(
    string Id,
    string FriendlyName,
    string DeviceInterfacePath,
    DeviceType Type,
    DeviceRole Role,
    bool IsConnected,
    bool IsDefault,
    bool IsCommunicationsDefault,
    int? FormFactor
);

public record ProfileTrigger(
    ProfileTriggerType Type,
    string? Value,
    Dictionary<string, string>? Parameters
);

public record DeviceRoutingRule(
    string ProcessPattern,
    string DeviceTypePreference
);

public class Profile
{
    public string Name { get; set; } = "";
    public List<ProfileTrigger> Triggers { get; set; } = new();
    public string DefaultDeviceType { get; set; } = "Speakers";
    public Dictionary<string, string> ProcessExceptions { get; set; } = new();
    public List<string> FallbackChain { get; set; } = new();
    public double? VolumeLimit { get; set; }
    public bool Enabled { get; set; } = true;

    public Profile() { }

    public Profile(string name, List<ProfileTrigger> triggers, string defaultDeviceType, Dictionary<string, string> processExceptions, List<string> fallbackChain, double? volumeLimit, bool enabled)
    {
        Name = name;
        Triggers = triggers;
        DefaultDeviceType = defaultDeviceType;
        ProcessExceptions = processExceptions;
        FallbackChain = fallbackChain;
        VolumeLimit = volumeLimit;
        Enabled = enabled;
    }
}

public record AppRoutingEntry(
    int ProcessId,
    string ProcessName,
    string WindowTitle,
    string TargetDeviceId,
    string TargetDeviceName,
    string ProfileName,
    DateTime AppliedAt
);

public record DeviceEvent(
    DeviceEventType Type,
    AudioDevice Device,
    DateTime Timestamp
);

public enum DeviceEventType
{
    Connected,
    Disconnected,
    DefaultChanged,
    RoleChanged,
    PropertyChanged
}

public record ProcessEvent(
    ProcessEventType Type,
    int ProcessId,
    string ProcessName,
    string? WindowTitle,
    DateTime Timestamp
);

public enum ProcessEventType
{
    Started,
    Exited,
    WindowFocusChanged,
    WindowTitleChanged
}

public record RoutingDecision(
    string DeviceId,
    string DeviceName,
    string Reason,
    string ProfileName,
    bool IsFallback
);

public class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public DeviceMatchingSettings Devices { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public ApiSettings Api { get; set; } = new();
}

public class GeneralSettings
{
    public bool StartMinimized { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
    public string Language { get; set; } = "en-US";
}

public class DeviceMatchingSettings
{
    public List<string> HeadsetPatterns { get; set; } = new() { "Headset", "Headphones", "WH-1000XM", "G Pro", "Arctis", "Razer", "HyperX", "Corsair", "Logitech" };
    public List<string> SpeakersPatterns { get; set; } = new() { "Speakers", "Realtek", "Audio", "Sound" };
    public List<string> HdmiPatterns { get; set; } = new() { "HDMI", "Monitor", "Display", "TV", "LG ", "Samsung ", "Dell ", "HP ", "ASUS ", "Acer ", "BenQ " };
    public List<string> BluetoothPatterns { get; set; } = new() { "Bluetooth", "Hands-Free", "Headset" };
    public List<string> VirtualPatterns { get; set; } = new() { "VB-Cable", "Voicemeeter", "Virtual", "CABLE" };
}

public class HotkeySettings
{
    public string CycleOutput { get; set; } = "Ctrl+Alt+O";
    public string ToggleMute { get; set; } = "Ctrl+Alt+M";
    public string ForceHeadset { get; set; } = "Ctrl+Alt+H";
    public string OpenDashboard { get; set; } = "Ctrl+Shift+A";
}

public class ApiSettings
{
    public bool Enabled { get; set; } = false;
    public int Port { get; set; } = 52841;
    public bool AllowRemote { get; set; } = false;
}