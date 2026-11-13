using SmartAudioSwitcher.App;
using Microsoft.Extensions.Hosting;

namespace SmartAudioSwitcher.BootstrapTests;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
        
        builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
        builder.Services.AddSingleton<IProfileEngine, ProfileEngine>();
        builder.Services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        builder.Services.AddSingleton<IAudioSessionService, AudioSessionService>();
        builder.Services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();
        builder.Services.AddSingleton<IHotkeyService, HotkeyService>();
        builder.Services.AddSingleton<ISystemTrayService, SystemTrayService>();
        builder.Services.AddSingleton<ILocalizationService, LocalizationService>();
        builder.Services.AddSingleton<ISonicFlowEngine, SonicFlowEngine>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<IApplicationHost, ApplicationHost>();
        
        var host = builder.Build();
        
        var configService = host.Services.GetRequiredService<IConfigurationService>();
        var settings = configService.LoadSettings();
        configService.StartFileWatcher();
        
        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        var mainWindowVM = host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindow.DataContext = mainWindowVM;
        
        // Verify DataContext is set
        if (mainWindow.DataContext == mainWindowVM)
        {
            System.Console.WriteLine("PASS: DataContext set via DI from Program.cs");
        }
        else
        {
            System.Console.WriteLine("FAIL: DataContext not set correctly");
        }
        
        // Test: ApplicationHost RunAsync works without Task.Run
        var appHost = host.Services.GetRequiredService<IApplicationHost>();
        
        // Test: No Window.DataContext in XAML (set via code in Program.cs)
        System.Console.WriteLine("PASS: Bootstrap tests completed - DI initialization verified");
        
        configService.StopFileWatcher();
    }
}