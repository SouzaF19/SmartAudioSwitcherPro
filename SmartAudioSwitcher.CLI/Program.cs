using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using SmartAudioSwitcher.Platform.Services;

namespace SmartAudioSwitcher.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
        builder.Services.AddSingleton<IProfileEngine, ProfileEngine>();
        builder.Services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        builder.Services.AddSingleton<IAudioSessionService, AudioSessionService>();
        builder.Services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();

        var host = builder.Build();

        var configService = host.Services.GetRequiredService<IConfigurationService>();
        var settings = configService.LoadSettings();
        
        var profileEngine = host.Services.GetRequiredService<IProfileEngine>();
        profileEngine.ReloadConfiguration(settings);

        var deviceService = host.Services.GetRequiredService<IAudioDeviceService>();
        deviceService.StartMonitoring();

        var processMonitor = host.Services.GetRequiredService<IProcessMonitorService>();

        var rootCommand = new RootCommand("SmartAudioSwitcher CLI - Control audio devices from command line");

        // List devices command
        var listDevices = new Command("list-devices", "List all audio devices");
        listDevices.SetHandler(() => ListDevices(deviceService));
        rootCommand.AddCommand(listDevices);

        // Get current default device
        var getDefault = new Command("get-default", "Get current default output device");
        getDefault.SetHandler(() => GetDefaultDevice(deviceService));
        rootCommand.AddCommand(getDefault);

        // Set default device by ID
        var setDefault = new Command("set-default", "Set default output device by ID");
        var deviceIdArg = new Argument<string>("device-id", "Device ID to set as default");
        setDefault.AddArgument(deviceIdArg);
        setDefault.SetHandler((string id) => SetDefaultDevice(deviceService, id), deviceIdArg);
        rootCommand.AddCommand(setDefault);

        // Set default by name (partial match)
        var setDefaultByName = new Command("set-device", "Set default device by name (partial match)");
        var nameArg = new Argument<string>("name", "Device name (partial match supported)");
        setDefaultByName.AddArgument(nameArg);
        setDefaultByName.SetHandler((string name) => SetDeviceByName(deviceService, name), nameArg);
        rootCommand.AddCommand(setDefaultByName);

        // List profiles
        var listProfiles = new Command("list-profiles", "List all routing profiles");
        listProfiles.SetHandler(() => ListProfiles(settings));
        rootCommand.AddCommand(listProfiles);

        // Evaluate current profile
        var evalProfile = new Command("eval-profile", "Evaluate which profile would be active now");
        evalProfile.SetHandler(() => EvaluateProfile(profileEngine, deviceService, processMonitor));
        rootCommand.AddCommand(evalProfile);

        // Cycle devices
        var cycle = new Command("cycle", "Cycle to next output device");
        cycle.SetHandler(() => CycleDevice(deviceService));
        rootCommand.AddCommand(cycle);

        // Force headset
        var forceHeadset = new Command("force-headset", "Force switch to headset");
        forceHeadset.SetHandler(() => ForceHeadset(deviceService));
        rootCommand.AddCommand(forceHeadset);

        // Version
        var version = new Command("version", "Show version");
        version.SetHandler(() => Console.WriteLine("SmartAudioSwitcher CLI v1.0.0"));
        rootCommand.AddCommand(version);

        var commandLineBuilder = new CommandLineBuilder(rootCommand);
        var parser = commandLineBuilder.Build();
        
        return await parser.InvokeAsync(args);
    }

    private static void ListDevices(IAudioDeviceService deviceService)
    {
        var devices = deviceService.GetOutputDevices();
        var defaultDevice = deviceService.GetDefaultDevice();

        Console.WriteLine();
        Console.WriteLine("Audio Output Devices:");
        Console.WriteLine(new string('=', 80));
        Console.WriteLine($"{"ID",-40} {"Name",-30} {"Type",-12} {"Status"}");
        Console.WriteLine(new string('-', 80));

        foreach (var device in devices)
        {
            var marker = device.Id == defaultDevice?.Id ? " * " : "   ";
            var status = device.IsConnected ? "Connected" : "Disconnected";
            if (device.IsDefault) status += " [DEFAULT]";
            if (device.IsCommunicationsDefault) status += " [COMM]";
            
            Console.WriteLine($"{marker}{device.Id,-38} {Truncate(device.FriendlyName, 28),-30} {device.Type,-12} {status}");
        }

        Console.WriteLine(new string('=', 80));
        Console.WriteLine("* = Current default device");
        Console.WriteLine();
    }

    private static void GetDefaultDevice(IAudioDeviceService deviceService)
    {
        var device = deviceService.GetDefaultDevice();
        if (device != null)
        {
            Console.WriteLine($"Default Device: {device.FriendlyName} ({device.Id})");
        }
        else
        {
            Console.WriteLine("No default device found");
        }
    }

    private static void SetDefaultDevice(IAudioDeviceService deviceService, string deviceId)
    {
        var device = deviceService.GetDeviceById(deviceId);
        if (device == null)
        {
            Console.WriteLine($"Error: Device not found: {deviceId}");
            return;
        }

        if (deviceService.SetDefaultDevice(deviceId))
        {
            Console.WriteLine($"Default device set to: {device.FriendlyName}");
        }
        else
        {
            Console.WriteLine("Failed to set default device");
        }
    }

    private static void SetDeviceByName(IAudioDeviceService deviceService, string name)
    {
        var devices = deviceService.GetOutputDevices();
        var match = devices.FirstOrDefault(d => 
            d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase) && d.IsConnected);

        if (match == null)
        {
            Console.WriteLine($"Error: No connected device found matching '{name}'");
            return;
        }

        if (deviceService.SetDefaultDevice(match.Id))
        {
            Console.WriteLine($"Default device set to: {match.FriendlyName}");
        }
        else
        {
            Console.WriteLine("Failed to set default device");
        }
    }

    private static void ListProfiles(AppSettings settings)
    {
        Console.WriteLine();
        Console.WriteLine("Routing Profiles:");
        Console.WriteLine(new string('=', 60));

        foreach (var profile in settings.Profiles.Where(p => p.Enabled))
        {
            Console.WriteLine($"Profile: {profile.Name}");
            Console.WriteLine($"  Default Device Type: {profile.DefaultDeviceType}");
            Console.WriteLine($"  Triggers: {profile.Triggers.Count}");
            Console.WriteLine($"  Exceptions: {profile.ProcessExceptions.Count}");
            Console.WriteLine($"  Fallback Chain: {string.Join(" > ", profile.FallbackChain)}");
            if (profile.VolumeLimit.HasValue)
            {
                Console.WriteLine($"  Volume Limit: {profile.VolumeLimit.Value * 100:F0}%");
            }
            Console.WriteLine();
        }
    }

    private static void EvaluateProfile(IProfileEngine profileEngine, IAudioDeviceService deviceService, IProcessMonitorService processMonitor)
    {
        var devices = deviceService.GetOutputDevices();
        var processes = processMonitor.GetRunningProcesses();

        var profile = profileEngine.EvaluateProfile(devices, processes);
        if (profile != null)
        {
            var decision = profileEngine.DecideRouting(profile, deviceService.GetDefaultDevice(), 
                processMonitor.GetForegroundProcess(), devices);

            Console.WriteLine();
            Console.WriteLine($"Active Profile: {profile.Name}");
            Console.WriteLine($"Routing Decision: {decision.DeviceName} ({decision.DeviceId})");
            Console.WriteLine($"Reason: {decision.Reason}");
            Console.WriteLine($"Fallback: {decision.IsFallback}");
        }
        else
        {
            Console.WriteLine("No profile matched");
        }
    }

    private static void CycleDevice(IAudioDeviceService deviceService)
    {
        var devices = deviceService.GetOutputDevices().Where(d => d.IsConnected).ToList();
        var current = deviceService.GetDefaultDevice();
        var currentIndex = devices.FindIndex(d => d.Id == current?.Id);
        var nextIndex = (currentIndex + 1) % devices.Count;
        
        var next = devices[nextIndex];
        if (deviceService.SetDefaultDevice(next.Id))
        {
            Console.WriteLine($"Switched to: {next.FriendlyName}");
        }
        else
        {
            Console.WriteLine("Failed to cycle device");
        }
    }

    private static void ForceHeadset(IAudioDeviceService deviceService)
    {
        var headset = deviceService.GetOutputDevices()
            .FirstOrDefault(d => d.Type == DeviceType.Headset && d.IsConnected);

        if (headset == null)
        {
            Console.WriteLine("No connected headset found");
            return;
        }

        if (deviceService.SetDefaultDevice(headset.Id))
        {
            Console.WriteLine($"Switched to headset: {headset.FriendlyName}");
        }
        else
        {
            Console.WriteLine("Failed to switch to headset");
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }
}