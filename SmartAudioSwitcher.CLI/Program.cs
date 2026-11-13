using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.CommandLine.Invocation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using SmartAudioSwitcher.Platform.Services;

namespace SmartAudioSwitcher.CLI;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var host = new LazyHost();
        return await RunAsync(args, host.GetServices);
    }

    // The factory is evaluated only by commands that need hardware or settings.
    public static async Task<int> RunAsync(string[] args, Func<IServiceProvider> serviceFactory)
    {
        var root = new RootCommand("SmartAudioSwitcher CLI - Control Windows audio devices");
        T Service<T>() where T : notnull => serviceFactory().GetRequiredService<T>();
        int Execute(Func<int> action)
        {
            try { return action(); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        void Add(string name, string description, Func<int> action)
        {
            var command = new Command(name, description);
            command.SetHandler((InvocationContext context) => context.ExitCode = Execute(action));
            root.AddCommand(command);
        }
        void AddArgument(string name, string description, Func<string, int> action)
        {
            var command = new Command(name, description);
            var argument = new Argument<string>("device");
            command.AddArgument(argument);
            command.SetHandler((InvocationContext context) => context.ExitCode = Execute(() => action(context.ParseResult.GetValueForArgument(argument))));
            root.AddCommand(command);
        }
        Add("version", "Show version", () => { Console.WriteLine("SmartAudioSwitcher CLI v0.0.1"); return 0; });
        Add("list-devices", "List output devices", () => { ListDevices(Service<IAudioDeviceService>()); return 0; });
        Add("get-default", "Get the default device", () => { var d = Service<IAudioDeviceService>().GetDefaultDevice(); if (d == null) return 1; Console.WriteLine($"{d.FriendlyName} ({d.Id})"); return 0; });
        AddArgument("set-default", "Set default by ID", id => SetDefaultDevice(Service<IAudioDeviceService>(), id));
        AddArgument("set-device", "Set default by partial name", name => SetDeviceByName(Service<IAudioDeviceService>(), name));
        Add("cycle", "Cycle output devices", () => CycleDevice(Service<IAudioDeviceService>()));
        Add("force-headset", "Switch to a connected headset", () => ForceHeadset(Service<IAudioDeviceService>()));
        Add("list-profiles", "List routing profiles", () => { ListProfiles(Service<IConfigurationService>().LoadSettings()); return 0; });
        Add("eval-profile", "Evaluate the current profile", () => {
            var engine = Service<IProfileEngine>();
            engine.ReloadConfiguration(Service<IConfigurationService>().LoadSettings());
            EvaluateProfile(engine, Service<IAudioDeviceService>(), Service<IProcessMonitorService>());
            return 0;
        });
        return await new CommandLineBuilder(root).UseDefaults().Build().InvokeAsync(args);
    }

    private sealed class LazyHost : IDisposable
    {
        private IHost? _host;
        public IServiceProvider GetServices()
        {
            if (_host != null) return _host.Services;
            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
            builder.Services.AddSingleton<IProfileEngine, ProfileEngine>();
            builder.Services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
            builder.Services.AddSingleton<IAudioSessionService, AudioSessionService>();
            builder.Services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
            _host = builder.Build();
            return _host.Services;
        }
        public void Dispose() => _host?.Dispose();
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

    private static int SetDefaultDevice(IAudioDeviceService devices, string id)
    {
        if (devices.GetDeviceById(id) is not { IsConnected: true } device)
        { Console.Error.WriteLine($"Connected device not found: {id}"); return 1; }
        if (!devices.SetDefaultDevice(id))
        { Console.Error.WriteLine("Windows did not confirm the device change"); return 1; }
        Console.WriteLine($"Default device: {device.FriendlyName}");
        return 0;
    }

    private static int SetDeviceByName(IAudioDeviceService devices, string name)
    {
        var matches = devices.GetOutputDevices().Where(d => d.IsConnected && d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1) { Console.Error.WriteLine("Name must match exactly one connected device; use its ID otherwise"); return 1; }
        return SetDefaultDevice(devices, matches[0].Id);
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

    private static int CycleDevice(IAudioDeviceService devices)
    {
        var connected = devices.GetOutputDevices().Where(d => d.IsConnected).ToList();
        var current = devices.GetDefaultDevice();
        var target = DeviceCycle.Next(connected, current, d => d.Id, current?.Id);
        if (target == null) { Console.Error.WriteLine("No connected output device"); return 1; }
        return SetDefaultDevice(devices, target.Id);
    }

    private static int ForceHeadset(IAudioDeviceService devices)
    {
        var target = devices.GetOutputDevices().FirstOrDefault(d => d.IsConnected && d.Type is DeviceType.Headset or DeviceType.Bluetooth);
        if (target == null) { Console.Error.WriteLine("No connected headset"); return 1; }
        return SetDefaultDevice(devices, target.Id);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }
}