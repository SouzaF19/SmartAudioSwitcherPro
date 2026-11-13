using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.Core.Services;

public class ProfileEngine : IProfileEngine
{
    private AppSettings _settings = new();
    private readonly ILogger<ProfileEngine> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly Func<bool> _isDocked;

    public ProfileEngine(ILogger<ProfileEngine> logger) : this(logger, TimeProvider.System, () => false) { }

    public ProfileEngine(ILogger<ProfileEngine> logger, TimeProvider timeProvider, Func<bool> isDocked)
    {
        _logger = logger;
        _timeProvider = timeProvider;
        _isDocked = isDocked;
    }

    public void ReloadConfiguration(AppSettings settings)
    {
        _settings = settings;
        _logger.LogInformation("Profile engine configuration reloaded. {Count} profiles loaded.", _settings.Profiles.Count);
    }

    public Profile? EvaluateProfile(IReadOnlyList<AudioDevice> devices, IReadOnlyList<ProcessInfo> processes)
    {
        var now = _timeProvider.GetLocalNow().DateTime;
        var isDocked = _isDocked();

        foreach (var profile in _settings.Profiles.Where(p => p.Enabled))
        {
            if (MatchesProfile(profile, devices, processes, now, isDocked))
            {
                _logger.LogDebug("Profile matched: {ProfileName}", profile.Name);
                return profile;
            }
        }

        // Fallback to default profile
        var defaultProfile = _settings.Profiles.FirstOrDefault(p => p.Enabled && p.Name.Equals("Default", StringComparison.OrdinalIgnoreCase));
        if (defaultProfile != null)
        {
            _logger.LogDebug("Using default profile");
            return defaultProfile;
        }

        _logger.LogWarning("No profile matched and no default profile found");
        return null;
    }

    private bool MatchesProfile(Profile profile, IReadOnlyList<AudioDevice> devices, IReadOnlyList<ProcessInfo> processes, DateTime now, bool isDocked)
    {
        if (profile.Triggers.Count == 0)
            return true; // Catch-all profile

        var alternatives = profile.Triggers.Where(t => t.Type is ProfileTriggerType.ProcessMatch or ProfileTriggerType.WindowTitleMatch).ToArray();
        var context = profile.Triggers.Where(t => t.Type is not (ProfileTriggerType.ProcessMatch or ProfileTriggerType.WindowTitleMatch));
        return context.All(t => EvaluateTrigger(t, devices, processes, now, isDocked)) &&
            (alternatives.Length == 0 || alternatives.Any(t => EvaluateTrigger(t, devices, processes, now, isDocked)));
    }

    private bool EvaluateTrigger(ProfileTrigger trigger, IReadOnlyList<AudioDevice> devices, IReadOnlyList<ProcessInfo> processes, DateTime now, bool isDocked)
    {
        return trigger.Type switch
        {
            ProfileTriggerType.ProcessMatch => processes.Any(p => 
                MatchesProcessPattern(p.ProcessName, trigger.Value ?? "") ||
                (p.MainModulePath != null && MatchesProcessPattern(Path.GetFileName(p.MainModulePath), trigger.Value ?? ""))),

            ProfileTriggerType.WindowTitleMatch => processes.Any(p => 
                p.WindowTitle != null && MatchesPattern(p.WindowTitle, trigger.Value ?? "")),

            ProfileTriggerType.TimeBetween => EvaluateTimeTrigger(trigger, now),

            ProfileTriggerType.DockedState => isDocked == (trigger.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true),

            ProfileTriggerType.DeviceConnected => devices.Any(d => d.IsConnected && MatchesPattern(d.FriendlyName, trigger.Value ?? "")),

            ProfileTriggerType.DeviceDisconnected => !devices.Any(d => d.IsConnected && MatchesPattern(d.FriendlyName, trigger.Value ?? "")),

            ProfileTriggerType.Always => true,

            _ => false
        };
    }

    private bool EvaluateTimeTrigger(ProfileTrigger trigger, DateTime now)
    {
        if (string.IsNullOrEmpty(trigger.Value))
            return false;

        // Format: "HH:mm-HH:mm" or "HH:mm-HH:mm,HH:mm-HH:mm" for multiple ranges
        var ranges = trigger.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var range in ranges)
        {
            var parts = range.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;

            if (TimeSpan.TryParse(parts[0].Trim(), out var start) &&
                TimeSpan.TryParse(parts[1].Trim(), out var end))
            {
                var current = now.TimeOfDay;
                
                if (start <= end)
                {
                    // Same day range (e.g., 09:00-18:00)
                    if (current >= start && current <= end)
                        return true;
                }
                else
                {
                    // Overnight range (e.g., 22:00-07:00)
                    if (current >= start || current <= end)
                        return true;
                }
            }
        }

        return false;
    }

    private bool MatchesPattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (string.IsNullOrEmpty(input)) return false;

        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(input, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
    }

    private bool MatchesProcessPattern(string processName, string pattern)
    {
        static string Normalize(string value)
        {
            value = Path.GetFileName(value);
            return value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;
        }
        return MatchesPattern(Normalize(processName), Normalize(pattern));
    }

    public RoutingDecision DecideRouting(Profile profile, AudioDevice? currentDevice, ProcessInfo? process, IReadOnlyList<AudioDevice> availableDevices)
    {
        var connectedDevices = availableDevices.Where(d => d.IsConnected).ToList();
        
        // If specific process has an exception, use that
        var exception = process == null ? default : profile.ProcessExceptions.FirstOrDefault(rule => MatchesProcessPattern(process.ProcessName, rule.Key));
        if (process != null && exception.Key != null)
        {
            var exceptionDeviceType = exception.Value;
            var device = FindDeviceByType(connectedDevices, exceptionDeviceType);
            if (device != null)
            {
                return new RoutingDecision(
                    device.Id,
                    device.FriendlyName,
                    $"Process exception for {process.ProcessName} → {exceptionDeviceType}",
                    profile.Name,
                    false
                );
            }
        }

        // Try default device type for profile
        var defaultDevice = FindDeviceByType(connectedDevices, profile.DefaultDeviceType);
        if (defaultDevice != null)
        {
            return new RoutingDecision(
                defaultDevice.Id,
                defaultDevice.FriendlyName,
                $"Profile '{profile.Name}' default device type: {profile.DefaultDeviceType}",
                profile.Name,
                false
            );
        }

        // Try fallback chain
        foreach (var fallbackType in profile.FallbackChain)
        {
            var fallbackDevice = FindDeviceByType(connectedDevices, fallbackType);
            if (fallbackDevice != null)
            {
                return new RoutingDecision(
                    fallbackDevice.Id,
                    fallbackDevice.FriendlyName,
                    $"Fallback to {fallbackType} (profile: {profile.Name})",
                    profile.Name,
                    true
                );
            }
        }

        // Last resort: system default
        var systemDefault = connectedDevices.FirstOrDefault(d => d.IsDefault);
        if (systemDefault != null)
        {
            return new RoutingDecision(
                systemDefault.Id,
                systemDefault.FriendlyName,
                "System default device (last resort)",
                profile.Name,
                true
            );
        }

        // Absolute last resort: first connected device
        var anyDevice = connectedDevices.FirstOrDefault();
        if (anyDevice != null)
        {
            return new RoutingDecision(
                anyDevice.Id,
                anyDevice.FriendlyName,
                "First available device (desperate fallback)",
                profile.Name,
                true
            );
        }

        throw new InvalidOperationException("No audio devices available");
    }

    private AudioDevice? FindDeviceByType(IReadOnlyList<AudioDevice> devices, string typeName)
    {
        var normalizedType = typeName.ToLowerInvariant();
        if (normalizedType == "system_default") return devices.FirstOrDefault(d => d.IsDefault);
        
        // First try exact type match
        var exactMatch = devices.FirstOrDefault(d => 
            d.Type.ToString().Equals(normalizedType, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null) return exactMatch;

        // Then try friendly name patterns from settings
        var patterns = GetPatternsForType(normalizedType);
        foreach (var pattern in patterns)
        {
            var match = devices.FirstOrDefault(d => 
                MatchesPattern(d.FriendlyName, pattern));
            if (match != null) return match;
        }

        // Then try partial name match
        return devices.FirstOrDefault(d => 
            d.FriendlyName.Contains(typeName, StringComparison.OrdinalIgnoreCase));
    }

    private List<string> GetPatternsForType(string typeName)
    {
        return typeName switch
        {
            "headset" => _settings.Devices.HeadsetPatterns,
            "speakers" => _settings.Devices.SpeakersPatterns,
            "hdmi" => _settings.Devices.HdmiPatterns,
            "bluetooth" => _settings.Devices.BluetoothPatterns,
            "virtual" => _settings.Devices.VirtualPatterns,
            "system_default" => new List<string>(),
            _ => new List<string>()
        };
    }
}