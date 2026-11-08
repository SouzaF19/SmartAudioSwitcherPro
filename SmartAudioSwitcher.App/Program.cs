using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using SmartAudioSwitcher.Platform.Services;
using SmartAudioSwitcher.App.Services;
using SmartAudioSwitcher.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace SmartAudioSwitcher.App;

public class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        
        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        // Register Core services
        builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
        builder.Services.AddSingleton<IProfileEngine, ProfileEngine>();
        
        // Register Platform services
        builder.Services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        builder.Services.AddSingleton<IAudioSessionService, AudioSessionService>();
        builder.Services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        builder.Services.AddSingleton<IHotkeyService, HotkeyService>();
        builder.Services.AddSingleton<ISystemTrayService, SystemTrayService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // Register SonicFlow Engine
        builder.Services.AddSingleton<ISonicFlowEngine, SonicFlowEngine>();
        
        // Register UI services
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        
        // Register Application Host
        builder.Services.AddSingleton<IApplicationHost, ApplicationHost>();

        var host = builder.Build();

        // Initialize services
        var configService = host.Services.GetRequiredService<IConfigurationService>();
        var settings = configService.LoadSettings();
        configService.StartFileWatcher();

        var localizationService = host.Services.GetRequiredService<ILocalizationService>();
        localizationService.SetLanguage(settings.General.Language);
        
        var profileEngine = host.Services.GetRequiredService<IProfileEngine>();
        profileEngine.ReloadConfiguration(settings);

        // Register hotkeys
        var hotkeyService = host.Services.GetRequiredService<IHotkeyService>();
        RegisterHotkeys(hotkeyService, host.Services);

        // Start monitoring services
        var deviceService = host.Services.GetRequiredService<IAudioDeviceService>();
        var sessionService = host.Services.GetRequiredService<IAudioSessionService>();
        var processMonitor = host.Services.GetRequiredService<IProcessMonitorService>();
        
        deviceService.StartMonitoring();
        sessionService.StartMonitoring();
        processMonitor.StartMonitoring();

        // Subscribe to events
        SubscribeToEvents(host.Services, profileEngine, configService);

        // Run application
        var appHost = host.Services.GetRequiredService<IApplicationHost>();
        await appHost.RunAsync();

        // Cleanup
        configService.StopFileWatcher();
        hotkeyService.UnregisterAll();
    }

    private static void RegisterHotkeys(IHotkeyService hotkeyService, IServiceProvider services)
    {
        var configService = services.GetRequiredService<IConfigurationService>();
        var settings = configService.LoadSettings();

        hotkeyService.RegisterHotkey("CycleOutput", settings.Hotkeys.CycleOutput, () =>
        {
            // Cycle through output devices
            var deviceService = services.GetRequiredService<IAudioDeviceService>();
            var devices = deviceService.GetOutputDevices().Where(d => d.IsConnected).ToList();
            var current = deviceService.GetDefaultDevice();
            var currentIndex = devices.FindIndex(d => d.Id == current?.Id);
            var nextIndex = (currentIndex + 1) % devices.Count;
            deviceService.SetDefaultDevice(devices[nextIndex].Id);
        });

        hotkeyService.RegisterHotkey("ToggleMute", settings.Hotkeys.ToggleMute, () =>
        {
            // Toggle system mute
            var deviceService = services.GetRequiredService<IAudioDeviceService>();
            var defaultDevice = deviceService.GetDefaultDevice();
            if (defaultDevice != null)
            {
                // Would need volume control implementation
            }
        });

        hotkeyService.RegisterHotkey("ForceHeadset", settings.Hotkeys.ForceHeadset, () =>
        {
            // Force switch to headset
            var deviceService = services.GetRequiredService<IAudioDeviceService>();
            var headset = deviceService.GetOutputDevices()
                .FirstOrDefault(d => d.Type == DeviceType.Headset && d.IsConnected);
            if (headset != null)
            {
                deviceService.SetDefaultDevice(headset.Id);
            }
        });

        hotkeyService.RegisterHotkey("OpenDashboard", settings.Hotkeys.OpenDashboard, () =>
        {
            var mainWindow = services.GetRequiredService<MainWindow>();
            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
                mainWindow.WindowState = System.Windows.WindowState.Normal;
                mainWindow.Activate();
            }
            else
            {
                mainWindow.Hide();
            }
        });
    }

    private static void SubscribeToEvents(IServiceProvider services, IProfileEngine profileEngine, IConfigurationService configService)
    {
        var deviceService = services.GetRequiredService<IAudioDeviceService>();
        var processMonitor = services.GetRequiredService<IProcessMonitorService>();
        var sessionService = services.GetRequiredService<IAudioSessionService>();
        var mainWindowVM = services.GetRequiredService<MainWindowViewModel>();

        // Device changes
        deviceService.DeviceChanged += (sender, e) =>
        {
            mainWindowVM.OnDeviceChanged(e);
            EvaluateAndApplyRouting(services, profileEngine, "Device change");
        };

        // Process changes
        processMonitor.ProcessEvent += (sender, e) =>
        {
            mainWindowVM.OnProcessEvent(e);
            EvaluateAndApplyRouting(services, profileEngine, $"Process {e.Type}: {e.ProcessName}");
        };

        // Session changes
        sessionService.SessionChanged += (sender, e) =>
        {
            mainWindowVM.OnSessionEvent(e);
        };

        // Config changes
        configService.SettingsChanged += (sender, settings) =>
        {
            profileEngine.ReloadConfiguration(settings);
            mainWindowVM.OnSettingsChanged(settings);
            EvaluateAndApplyRouting(services, profileEngine, "Settings changed");
        };
    }

    private static void EvaluateAndApplyRouting(IServiceProvider services, IProfileEngine profileEngine, string reason)
    {
        try
        {
            var deviceService = services.GetRequiredService<IAudioDeviceService>();
            var processMonitor = services.GetRequiredService<IProcessMonitorService>();
            var mainWindowVM = services.GetRequiredService<MainWindowViewModel>();

            var devices = deviceService.GetOutputDevices();
            var processes = processMonitor.GetRunningProcesses();

            var profile = profileEngine.EvaluateProfile(devices, processes);
            if (profile != null)
            {
                var decision = profileEngine.DecideRouting(profile, deviceService.GetDefaultDevice(), 
                    processMonitor.GetForegroundProcess(), devices);
                
                mainWindowVM.OnRoutingDecision(decision, reason);

                // Apply routing for default device
                if (!decision.IsFallback || true) // Always apply for now
                {
                    deviceService.SetDefaultDevice(decision.DeviceId);
                }
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "Error evaluating routing: {Reason}", reason);
        }
    }
}