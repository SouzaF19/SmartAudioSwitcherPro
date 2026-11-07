using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using SmartAudioSwitcher.Core.Interfaces;

namespace SmartAudioSwitcher.App;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly ISystemTrayService _trayService;
    private bool _isClosing = false;

    public MainWindow(ILogger<MainWindow> logger, ISystemTrayService trayService)
    {
        _logger = logger;
        _trayService = trayService;
        InitializeComponent();
        
        // Don't show in taskbar when minimized to tray
        this.ShowInTaskbar = true;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("MainWindow loaded");
        
        // Initialize tray context menu
        InitializeTrayMenu();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosing)
        {
            e.Cancel = true;
            HideWindow();
        }
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideWindow();
        }
    }

    private void HideWindow()
    {
        Hide();
        _trayService.ShowNotification("SmartAudioSwitcher", "Running in background. Click tray icon to restore.", 
            SmartAudioSwitcher.Core.Models.NotificationType.Info);
    }

    public new void Show()
    {
        base.Show();
        WindowState = WindowState.Normal;
        Activate();
        _logger.LogDebug("MainWindow shown");
    }

    private void InitializeTrayMenu()
    {
        var items = new List<TrayMenuItem>
        {
            new("show", "Show Dashboard", null, true, false, null),
            new("sep1", "", null, false, false, null),
            new("cycle", "Cycle Output Device", null, true, false, null),
            new("mute", "Toggle Mute", null, true, false, null),
            new("headset", "Force Headset", null, true, false, null),
            new("sep2", "", null, false, false, null),
            new("settings", "Settings", null, true, false, null),
            new("sep3", "", null, false, false, null),
            new("exit", "Exit", null, true, false, null)
        };

        _trayService.SetContextMenu(items);
        _trayService.MenuAction += OnTrayMenuAction;
    }

    private void OnTrayMenuAction(object? sender, TrayMenuAction e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.ItemId)
            {
                case "show":
                    Show();
                    break;
                case "cycle":
                    CycleOutputDevice();
                    break;
                case "mute":
                    ToggleMute();
                    break;
                case "headset":
                    ForceHeadset();
                    break;
                case "settings":
                    Show();
                    // Navigate to settings tab
                    break;
                case "exit":
                    _isClosing = true;
                    Close();
                    Application.Current.Shutdown();
                    break;
            }
        });
    }

    private void CycleOutputDevice()
    {
        // Would call device service to cycle
        _logger.LogInformation("Cycle output device requested from tray");
    }

    private void ToggleMute()
    {
        _logger.LogInformation("Toggle mute requested from tray");
    }

    private void ForceHeadset()
    {
        _logger.LogInformation("Force headset requested from tray");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideWindow();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}