using SmartAudioSwitcher.Core.Configuration;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
namespace SmartAudioSwitcher.Core.Services;
public sealed class ConfigurationService:IConfigurationService,IDisposable
{
 readonly ILogger<ConfigurationService> _logger;
 readonly string _configPath;
 readonly object _gate=new();
 readonly ISerializer _serializer=new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull).Build();
 AppSettings? _cachedSettings;string? _lastText;FileSystemWatcher? _watcher;Timer? _debounce;bool _disposed;
 public event EventHandler<AppSettings>? SettingsChanged;
 public ConfigurationService(ILogger<ConfigurationService> logger,string? configPath=null)
 {
  _logger=logger;
  _configPath=Path.GetFullPath(configPath??GetDefaultConfigPath());
 }
 public string GetConfigPath()=>_configPath;
 public AppSettings GetDefaults()=>CreateDefaults();
 public string GetDefaultConfigPath()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SmartAudioSwitcher","config.yaml");
 public AppSettings LoadSettings()
 {
  lock(_gate)
  {
   ObjectDisposedException.ThrowIf(_disposed,this);
   if(!File.Exists(_configPath))
   {
    _cachedSettings??=CreateDefaults();
    SaveSettings(_cachedSettings);return _cachedSettings;
   }
   try
   {
    var text=File.ReadAllText(_configPath);
    var settings=ConfigurationYaml.Deserialize(text,out var hasProfiles);
    if(!hasProfiles)settings.Profiles=GetDefaultProfiles();
    _cachedSettings=settings;_lastText=text;return settings;
   }
   catch(Exception ex) when(ex is not OutOfMemoryException)
   {
    _logger.LogError(ex,"Invalid configuration at {Path}; retaining the last valid settings without changing the file",_configPath);
    return _cachedSettings??=CreateDefaults();
   }
  }
 }
 AppSettings CreateDefaults()=>new(){Profiles=GetDefaultProfiles()};
 public void SaveSettings(AppSettings settings)
 {
  lock(_gate)
  {
   ObjectDisposedException.ThrowIf(_disposed,this);ConfigurationYaml.Validate(settings);
   var text=_serializer.Serialize(settings);var directory=Path.GetDirectoryName(_configPath)!;Directory.CreateDirectory(directory);
   var temporary=Path.Combine(directory,"."+Path.GetFileName(_configPath)+"."+Guid.NewGuid().ToString("N")+".tmp");
   try
   {
    using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
    {using(var writer=new StreamWriter(file,System.Text.Encoding.UTF8,1024,true)){writer.Write(text);writer.Flush();}file.Flush(true);}
    File.Move(temporary,_configPath,true);_cachedSettings=settings;_lastText=text;
   }
   finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }
 public void StartFileWatcher()
 {
  lock(_gate)
  {
   ObjectDisposedException.ThrowIf(_disposed,this);if(_watcher!=null)return;
   Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
   _debounce=new Timer(_=>Reload(),null,Timeout.Infinite,Timeout.Infinite);
   _watcher=new FileSystemWatcher(Path.GetDirectoryName(_configPath)!,Path.GetFileName(_configPath)){NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size};
   _watcher.Changed+=Schedule;_watcher.Created+=Schedule;_watcher.Renamed+=Schedule;_watcher.EnableRaisingEvents=true;
  }
 }
 void Schedule(object sender,FileSystemEventArgs args)
 {lock(_gate){if(!_disposed&&_watcher!=null)_debounce?.Change(150,Timeout.Infinite);}}
 void Reload()
 {
  AppSettings? updated=null;
  lock(_gate)
  {
   if(_disposed||_watcher==null||!File.Exists(_configPath))return;
   var previous=_lastText;var settings=LoadSettings();if(previous!=_lastText)updated=settings;
  }
  if(updated!=null)
   foreach(EventHandler<AppSettings> handler in SettingsChanged?.GetInvocationList()??Array.Empty<Delegate>())
    try{handler(this,updated);}catch(Exception ex){_logger.LogError(ex,"Configuration subscriber failed");}
 }
 public void StopFileWatcher()
 {
  lock(_gate){_watcher?.Dispose();_watcher=null;_debounce?.Dispose();_debounce=null;}
 }
 public void Dispose(){lock(_gate){if(_disposed)return;StopFileWatcher();_disposed=true;}}
    private List<Profile> GetDefaultProfiles()
    {
        return new List<Profile>
        {
            new Profile
            {
                Name = "Gaming",
                Triggers = new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.ProcessMatch, "*game*", null),
                    new(ProfileTriggerType.ProcessMatch, "steam.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "epicgameslauncher.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "battle.net.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "origin.exe", null),
                    new(ProfileTriggerType.ProcessMatch, "goggalaxy.exe", null),
                    new(ProfileTriggerType.WindowTitleMatch, "* - Steam", null)
                },
                DefaultDeviceType = "speakers",
                ProcessExceptions = new Dictionary<string, string>
                {
                    ["Discord.exe"] = "headset",
                    ["TeamSpeak*"] = "headset",
                    ["chrome.exe"] = "headset",
                    ["msedge.exe"] = "headset",
                    ["firefox.exe"] = "headset",
                    ["OBS*.exe"] = "headset",
                    ["Streamlabs*"] = "headset"
                },
                FallbackChain = new List<string> { "headset", "hdmi", "system_default" },
                VolumeLimit = null,
                Enabled = true
            },
            new Profile
            {
                Name = "Work Hours",
                Triggers = new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.TimeBetween, "09:00-18:00", null),
                    new(ProfileTriggerType.DockedState, "true", null)
                },
                DefaultDeviceType = "hdmi",
                ProcessExceptions = new Dictionary<string, string>
                {
                    ["Teams.exe"] = "headset",
                    ["Zoom.exe"] = "headset",
                    ["Webex*"] = "headset",
                    ["Skype*"] = "headset",
                    ["Slack.exe"] = "headset"
                },
                FallbackChain = new List<string> { "speakers", "headset", "system_default" },
                VolumeLimit = null,
                Enabled = true
            },
            new Profile
            {
                Name = "Night Mode",
                Triggers = new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.TimeBetween, "22:00-07:00", null)
                },
                DefaultDeviceType = "headset",
                ProcessExceptions = new Dictionary<string, string>(),
                FallbackChain = new List<string> { "speakers", "system_default" },
                VolumeLimit = 0.4,
                Enabled = true
            },
            new Profile
            {
                Name = "Presentation",
                Triggers = new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.ProcessMatch, "POWERPNT.EXE", null),
                    new(ProfileTriggerType.WindowTitleMatch, "* - PowerPoint", null),
                    new(ProfileTriggerType.ProcessMatch, "WINWORD.EXE", null)
                },
                DefaultDeviceType = "hdmi",
                ProcessExceptions = new Dictionary<string, string>(),
                FallbackChain = new List<string> { "speakers", "system_default" },
                VolumeLimit = null,
                Enabled = true
            },
            new Profile
            {
                Name = "Default",
                Triggers = new List<ProfileTrigger>
                {
                    new(ProfileTriggerType.Always, null, null)
                },
                DefaultDeviceType = "speakers",
                ProcessExceptions = new Dictionary<string, string>(),
                FallbackChain = new List<string> { "headset", "hdmi", "system_default" },
                VolumeLimit = null,
                Enabled = true
            }
        };
    }
}
