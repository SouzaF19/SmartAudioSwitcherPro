using SmartAudioSwitcher.Core.Models;

namespace SmartAudioSwitcher.Core.Interfaces;

public interface IAudioDeviceService
{
    IReadOnlyList<AudioDevice> GetAllDevices();
    IReadOnlyList<AudioDevice> GetOutputDevices();
    AudioDevice? GetDeviceById(string deviceId);
    AudioDevice? GetDefaultDevice(DeviceRole role = DeviceRole.Default);
    bool SetDefaultDevice(string deviceId, DeviceRole role = DeviceRole.Both);
    event EventHandler<DeviceEvent> DeviceChanged;
    void StartMonitoring();
    void StopMonitoring();
}

public interface IAudioSessionService
{
    IReadOnlyList<AudioSession> GetActiveSessions();
    AudioSession? GetSessionByProcessId(int processId);
    bool SetSessionDevice(int processId, string deviceId);
    event EventHandler<AudioSessionEvent> SessionChanged;
    void StartMonitoring();
    void StopMonitoring();
}

public record AudioSession(
    int ProcessId,
    string ProcessName,
    string DisplayName,
    string? IconPath,
    bool IsSystemSounds,
    float Volume,
    bool IsMuted,
    string CurrentDeviceId
);

public record AudioSessionEvent(
    AudioSessionEventType Type,
    AudioSession Session
);

public enum AudioSessionEventType
{
    Created,
    Disposed,
    VolumeChanged,
    MuteChanged,
    DeviceChanged,
    DisplayNameChanged
}

public interface IProcessMonitorService
{
    IReadOnlyList<ProcessInfo> GetRunningProcesses();
    ProcessInfo? GetProcessById(int processId);
    event EventHandler<ProcessEvent> ProcessEvent;
    void StartMonitoring();
    void StopMonitoring();
    bool IsProcessRunning(string processName);
    ProcessInfo? GetForegroundProcess();
}

public record ProcessInfo(
    int ProcessId,
    string ProcessName,
    string? WindowTitle,
    string? MainModulePath,
    DateTime StartTime,
    bool HasMainWindow
);

public interface IProfileEngine
{
    Profile? EvaluateProfile(IReadOnlyList<AudioDevice> devices, IReadOnlyList<ProcessInfo> processes);
    RoutingDecision DecideRouting(Profile profile, AudioDevice? currentDevice, ProcessInfo? process, IReadOnlyList<AudioDevice> availableDevices);
    void ReloadConfiguration(AppSettings settings);
}

public interface IConfigurationService
{
    AppSettings LoadSettings();
    void SaveSettings(AppSettings settings);
    event EventHandler<AppSettings> SettingsChanged;
    string GetConfigPath();
    AppSettings GetDefaults();
}

public interface IHotkeyService
{
    void RegisterHotkey(string name, string keyCombination, Action callback);
    void UnregisterHotkey(string name);
    void UnregisterAll();
    bool IsRegistered(string name);
}

public interface ISystemTrayService
{
    void Initialize();
    void ShowNotification(string title, string message, NotificationType type = NotificationType.Info);
    void UpdateIcon(bool isActive, string? tooltip = null);
    void SetContextMenu(IReadOnlyList<TrayMenuItem> items);
    event EventHandler<TrayMenuAction> MenuAction;
    void Dispose();
}

public enum NotificationType
{
    Info,
    Warning,
    Error,
    Success
}

public record TrayMenuItem(
    string Id,
    string Header,
    string? Icon,
    bool IsEnabled,
    bool IsChecked,
    List<TrayMenuItem>? SubItems
);

public record TrayMenuAction(
    string ItemId,
    object? Parameter
);

public interface IApplicationHost
{
    Task RunAsync(CancellationToken cancellationToken = default);
    void RequestShutdown();
    event EventHandler ShutdownRequested;
}