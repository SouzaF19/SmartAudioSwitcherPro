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
    public static async Task<int> Main(string[] args)
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

        // Register UI services - window and VM resolved via DI
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MainWindowViewModel>();

        // Register Application Host
        builder.Services.AddSingleton<IApplicationHost, ApplicationHost>();

        var host = builder.Build();

        // Resolve services and initialize
        var configService = host.Services.GetRequiredService<IConfigurationService>();
        var settings = configService.LoadSettings();
        configService.StartFileWatcher();

        var localizationService = host.Services.GetRequiredService<ILocalizationService>();
        localizationService.SetLanguage(settings.General.Language);

        var profileEngine = host.Services.GetRequiredService<IProfileEngine>();
        profileEngine.ReloadConfiguration(settings);

        // Build the main window via DI - NO Task.Run for Show
        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        var mainWindowVM = host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.DataContext = mainWindowVM;

        // Subscribe to events via DI
        SubscribeToEvents(host.Services, profileEngine, configService);

        // Run application via App.OnStartup as owner - synchronous with host lifetime
        // The ApplicationHost manages the UI thread dispatcher and shutdown cleanup
        var appHost = host.Services.GetRequiredService<IApplicationHost>();
        await appHost.RunAsync();

        // Cleanup
        configService.StopFileWatcher();
        var hotkeyService = host.Services.GetRequiredService<IHotkeyService>();
        hotkeyService.UnregisterAll();

        return 0;
    }

    private static void SubscribeToEvents(IServiceProvider services, IProfileEngine profileEngine, IConfigurationService configService)
    {
        var deviceService = services.GetRequiredService<IAudioDeviceService>();
        var processMonitor = services.GetRequiredService<IProcessMonitorService>();
        var sessionService = services.GetRequiredService<IAudioSessionService>();

        // Device changes
        deviceService.DeviceChanged += (sender, e) =>
        {
            var mainWindowVM = services.GetRequiredService<MainWindowViewModel>();
            mainWindowVM.OnDeviceChanged(e);
            EvaluateAndApplyRouting(services, profileEngine, "Device change");
        };

        // Process changes
        processMonitor.ProcessEvent += (sender, e) =>
        {
            var mainWindowVM = services.GetRequiredService<MainWindowViewModel>();
            mainWindowVM.OnProcessEvent(e);
            EvaluateAndApplyRouting(services, profileEngine, $"Process {e.Type}: {e.ProcessName}");
        };

        // Session changes
        sessionService.SessionChanged += (sender, e) =>
        {
            var mainWindowVM = services.GetRequiredService<MainWindowViewModel>();
            mainWindowVM.OnSessionEvent(e);
        };

        // Config changes
        configService.SettingsChanged += (sender, settings) =>
        {
            profileEngine.ReloadConfiguration(settings);
            var mainWindowVM = services.GetRequiredService<MainWindowViewModel>();
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
                if (!decision.IsFallback || true)
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
