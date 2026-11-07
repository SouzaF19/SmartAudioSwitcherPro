using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SmartAudioSwitcher.Core.Services;

public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly string _configPath;
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;
    private AppSettings? _cachedSettings;
    private FileSystemWatcher? _watcher;

    public event EventHandler<AppSettings>? SettingsChanged;

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        _configPath = GetDefaultConfigPath();
        
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                _logger.LogInformation("Config file not found at {Path}, creating defaults", _configPath);
                var defaults = GetDefaults();
                SaveSettings(defaults);
                return defaults;
            }

            var yaml = File.ReadAllText(_configPath);
            var settings = _deserializer.Deserialize<AppSettings>(yaml);
            
            // Merge with defaults to ensure all properties exist
            settings = MergeWithDefaults(settings);
            
            _cachedSettings = settings;
            _logger.LogInformation("Settings loaded from {Path}", _configPath);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings, using defaults");
            return GetDefaults();
        }
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var yaml = _serializer.Serialize(settings);
            File.WriteAllText(_configPath, yaml);
            
            _cachedSettings = settings;
            _logger.LogInformation("Settings saved to {Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            throw;
        }
    }

    public string GetConfigPath() => _configPath;

    public AppSettings GetDefaults() => new AppSettings
    {
        General = new GeneralSettings(),
        Devices = new DeviceMatchingSettings(),
        Profiles = GetDefaultProfiles(),
        Hotkeys = new HotkeySettings(),
        Api = new ApiSettings()
    };

    private List<Profile> GetDefaultProfiles()
    {
        return new List<Profile>
        {
            new Profile(
                Name: "Gaming",
                Triggers: new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.ProcessMatch, "*game*", null),
                    new(ProfileTriggerType.ProcessMatch, "steam.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "epicgameslauncher.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "battle.net.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "origin.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "goggalaxy.exe", null),
                    new(ProfileTriggerType.WindowTitleMatch, "* - Steam", null)
                },
                DefaultDeviceType: "speakers",
                ProcessExceptions: new Dictionary<string, string>
                {
                    ["Discord.exe"] = "headset",
                    ["TeamSpeak*"] = "headset",
                    ["chrome.exe"] = "headset",
                    ["msedge.exe"] = "headset",
                    ["firefox.exe"] = "headset",
                    ["OBS*.exe"] = "headset",
                    ["Streamlabs*"] = "headset"
                },
                FallbackChain: new List<string> { "headset", "hdmi", "system_default" },
                VolumeLimit: null,
                Enabled: true
            ),
            new Profile(
                Name: "Work Hours",
                Triggers: new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.TimeBetween, "09:00-18:00", null),
                    new(ProfileTriggerType.DockedState, "true", null)
                },
                DefaultDeviceType: "hdmi",
                ProcessExceptions: new Dictionary<string, string>
                {
                    ["Teams.exe"] = "headset",
                    ["Zoom.exe"] = "headset",
                    ["Webex*"] = "headset",
                    ["Skype*"] = "headset",
                    ["Slack.exe"] = "headset"
                },
                FallbackChain: new List<string> { "speakers", "headset", "system_default" },
                VolumeLimit: null,
                Enabled: true
            ),
            new Profile(
                Name: "Night Mode",
                Triggers: new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.TimeBetween, "22:00-07:00", null)
                },
                DefaultDeviceType: "headset",
                ProcessExceptions: new Dictionary<string, string>(),
                FallbackChain: new List<string> { "speakers", "system_default" },
                VolumeLimit: 0.4,
                Enabled: true
            ),
            new Profile(
                Name: "Presentation",
                Triggers: new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.ProcessMatch, "POWERPNT.EXE", null),
                    new(ProfileTriggerType.WindowTitleMatch, "* - PowerPoint", null),
                    new(ProfileTriggerType.ProcessMatch, "WINWORD.EXE", null)
                },
                DefaultDeviceType: "hdmi",
                ProcessExceptions: new Dictionary<string, string>(),
                FallbackChain: new List<string> { "speakers", "system_default" },
                VolumeLimit: null,
                Enabled: true
            ),
            new Profile(
                Name: "Default",
                Triggers: new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.Always, null, null)
                },
                DefaultDeviceType: "speakers",
                ProcessExceptions: new Dictionary<string, string>(),
                FallbackChain: new List<string> { "headset", "hdmi", "system_default" },
                VolumeLimit: null,
                Enabled: true
            )
        };
    }

    private AppSettings MergeWithDefaults(AppSettings loaded)
    {
        var defaults = GetDefaults();
        
        // Ensure all collections are initialized
        loaded.General ??= defaults.General;
        loaded.Devices ??= defaults.Devices;
        loaded.Profiles ??= defaults.Profiles;
        loaded.Hotkeys ??= defaults.Hotkeys;
        loaded.Api ??= defaults.Api;

        // Merge device patterns (keep user patterns, add missing defaults)
        MergePatterns(loaded.Devices.HeadsetPatterns, defaults.Devices.HeadsetPatterns);
        MergePatterns(loaded.Devices.SpeakersPatterns, defaults.Devices.SpeakersPatterns);
        MergePatterns(loaded.Devices.HdmiPatterns, defaults.Devices.HdmiPatterns);
        MergePatterns(loaded.Devices.BluetoothPatterns, defaults.Devices.BluetoothPatterns);
        MergePatterns(loaded.Devices.VirtualPatterns, defaults.Devices.VirtualPatterns);

        return loaded;
    }

    private void MergePatterns(List<string> userPatterns, List<string> defaultPatterns)
    {
        foreach (var pattern in defaultPatterns)
        {
            if (!userPatterns.Any(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
            {
                userPatterns.Add(pattern);
            }
        }
    }

    private string GetDefaultConfigPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configDir = Path.Combine(appData, "SmartAudioSwitcher");
        return Path.Combine(configDir, "config.yaml");
    }

    public void StartFileWatcher()
    {
        if (_watcher != null) return;

        var directory = Path.GetDirectoryName(_configPath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnConfigChanged;
        _logger.LogDebug("Started config file watcher on {Path}", _configPath);
    }

    public void StopFileWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce rapid changes
        Task.Delay(500).ContinueWith(_ =>
        {
            try
            {
                var newSettings = LoadSettings();
                SettingsChanged?.Invoke(this, newSettings);
                _logger.LogInformation("Settings reloaded due to file change");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload settings after file change");
            }
        });
    }
}