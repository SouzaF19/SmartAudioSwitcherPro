using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.App.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IConfigurationService _configService;
    
    private ObservableCollection<DeviceViewModel> _devices = new();
    private ObservableCollection<RoutingViewModel> _activeRouting = new();
    private ObservableCollection<ProfileViewModel> _profiles = new();
    private ProfileViewModel? _activeProfile;
    private string _statusText = "Starting...";
    private bool _isRunning = true;

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IConfigurationService configService)
    {
        _logger = logger;
        _configService = configService;
        
        LoadData();
    }

    public ObservableCollection<DeviceViewModel> Devices
    {
        get => _devices;
        set => SetField(ref _devices, value);
    }

    public ObservableCollection<RoutingViewModel> ActiveRouting
    {
        get => _activeRouting;
        set => SetField(ref _activeRouting, value);
    }

    public ObservableCollection<ProfileViewModel> Profiles
    {
        get => _profiles;
        set => SetField(ref _profiles, value);
    }

    public ProfileViewModel? ActiveProfile
    {
        get => _activeProfile;
        set => SetField(ref _activeProfile, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set => SetField(ref _isRunning, value);
    }

    public ICommand RefreshCommand => new RelayCommand(LoadData);
    public ICommand OpenSettingsCommand => new RelayCommand(OpenSettings);
    public ICommand MinimizeToTrayCommand => new RelayCommand(MinimizeToTray);

    public void OnDeviceChanged(DeviceEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = Devices.FirstOrDefault(d => d.Id == e.Device.Id);
            if (existing != null)
            {
                existing.UpdateFromDevice(e.Device);
            }
            else if (e.Type == DeviceEventType.Connected)
            {
                Devices.Add(new DeviceViewModel(e.Device));
            }

            UpdateStatus($"Device {e.Type}: {e.Device.FriendlyName}");
        });
    }

    public void OnProcessEvent(ProcessEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateStatus($"Process {e.Type}: {e.ProcessName} (PID: {e.ProcessId})");
        });
    }

    public void OnSessionEvent(AudioSessionEvent e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Update session info if needed
        });
    }

    public void OnRoutingDecision(RoutingDecision decision, string reason)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = ActiveRouting.FirstOrDefault(r => r.ProcessName == "Default");
            if (existing != null)
            {
                existing.DeviceName = decision.DeviceName;
                existing.DeviceId = decision.DeviceId;
                existing.Reason = decision.Reason;
                existing.ProfileName = decision.ProfileName;
                existing.IsFallback = decision.IsFallback;
                existing.AppliedAt = DateTime.Now;
            }
            else
            {
                ActiveRouting.Insert(0, new RoutingViewModel
                {
                    ProcessName = "Default",
                    DeviceName = decision.DeviceName,
                    DeviceId = decision.DeviceId,
                    Reason = decision.Reason,
                    ProfileName = decision.ProfileName,
                    IsFallback = decision.IsFallback,
                    AppliedAt = DateTime.Now
                });
            }

            ActiveProfile = Profiles.FirstOrDefault(p => p.Name == decision.ProfileName);
            if (ActiveProfile != null)
            {
                foreach (var p in Profiles)
                {
                    p.IsActive = p == ActiveProfile;
                }
            }

            UpdateStatus($"Routing: {decision.DeviceName} ({reason})");
        });
    }

    public void OnSettingsChanged(AppSettings settings)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Profiles.Clear();
            foreach (var profile in settings.Profiles)
            {
                Profiles.Add(new ProfileViewModel(profile));
            }
            LoadDevices();
        });
    }

    private void LoadData()
    {
        LoadDevices();
        LoadProfiles();
        StatusText = "Ready";
    }

    private void LoadDevices()
    {
        // Devices are populated via events from the service
    }

    private void LoadProfiles()
    {
        var settings = _configService.LoadSettings();
        Profiles.Clear();
        foreach (var profile in settings.Profiles)
        {
            Profiles.Add(new ProfileViewModel(profile));
        }
    }

    private void OpenSettings()
    {
        // Open settings window
        _logger.LogInformation("Open settings requested");
    }

    private void MinimizeToTray()
    {
        // Handled by window
    }

    private void UpdateStatus(string message)
    {
        StatusText = $"{DateTime.Now:HH:mm:ss} - {message}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

public class DeviceViewModel : INotifyPropertyChanged
{
    public string Id { get; }
    public string FriendlyName { get; private set; }
    public DeviceType Type { get; private set; }
    public DeviceRole Role { get; private set; }
    public bool IsConnected { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsCommunicationsDefault { get; private set; }
    public string TypeIcon => GetTypeIcon(Type);
    public string StatusText => GetStatusText();

    public DeviceViewModel(AudioDevice device)
    {
        Id = device.Id;
        UpdateFromDevice(device);
    }

    public void UpdateFromDevice(AudioDevice device)
    {
        FriendlyName = device.FriendlyName;
        Type = device.Type;
        Role = device.Role;
        IsConnected = device.IsConnected;
        IsDefault = device.IsDefault;
        IsCommunicationsDefault = device.IsCommunicationsDefault;
        OnPropertyChanged(nameof(FriendlyName));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(Role));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDefault));
        OnPropertyChanged(nameof(IsCommunicationsDefault));
        OnPropertyChanged(nameof(TypeIcon));
        OnPropertyChanged(nameof(StatusText));
    }

    private string GetTypeIcon(DeviceType type)
    {
        return type switch
        {
            DeviceType.Headset => "🎧",
            DeviceType.Speakers => "🔊",
            DeviceType.Hdmi => "📺",
            DeviceType.Bluetooth => "📱",
            DeviceType.Virtual => "🔀",
            DeviceType.Monitor => "🖥️",
            _ => "❓"
        };
    }

    private string GetStatusText()
    {
        var parts = new List<string>();
        if (IsConnected) parts.Add("Connected");
        if (IsDefault) parts.Add("Default");
        if (IsCommunicationsDefault) parts.Add("Communications");
        return string.Join(" • ", parts);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RoutingViewModel : INotifyPropertyChanged
{
    public string ProcessName { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string Reason { get; set; } = "";
    public string ProfileName { get; set; } = "";
    public bool IsFallback { get; set; }
    public DateTime AppliedAt { get; set; }
    public string TimeAgo => GetTimeAgo();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private string GetTimeAgo()
    {
        var diff = DateTime.Now - AppliedAt;
        if (diff.TotalSeconds < 60) return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
        return $"{(int)diff.TotalDays}d ago";
    }
}

public class ProfileViewModel : INotifyPropertyChanged
{
    public string Name { get; }
    public string DefaultDeviceType { get; }
    public int TriggerCount { get; }
    public int ExceptionCount { get; }
    public bool Enabled { get; }
    public bool IsActive { get; set; }

    public ProfileViewModel(Profile profile)
    {
        Name = profile.Name;
        DefaultDeviceType = profile.DefaultDeviceType;
        TriggerCount = profile.Triggers.Count;
        ExceptionCount = profile.ProcessExceptions.Count;
        Enabled = profile.Enabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public event EventHandler? CanExecuteChanged;
}