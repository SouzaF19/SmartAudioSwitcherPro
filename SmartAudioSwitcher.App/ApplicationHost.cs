using SmartAudioSwitcher.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.App;

public class ApplicationHost : IApplicationHost, IHostedService
{
    private readonly ILogger<ApplicationHost> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly MainWindow _mainWindow;

    public event EventHandler? ShutdownRequested;

    public ApplicationHost(ILogger<ApplicationHost> logger, IHostApplicationLifetime lifetime, MainWindow mainWindow)
    {
        _logger = logger;
        _lifetime = lifetime;
        _mainWindow = mainWindow;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Application host starting...");

        // WPF App.Run() already provides the message loop on UI thread
        // No need for Task.Run or Dispatcher.Run - just show the window
        _mainWindow.Show();

        // Wait for shutdown signal
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var reg = cts.Token.Register(() =>
        {
            _lifetime.StopApplication();
        });

        // When App.Shutdown is called, the message loop exits
        // Wait for the lifetime to stop the application
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        _logger.LogInformation("Application host stopped");
    }

    public void RequestShutdown()
    {
        _logger.LogInformation("Shutdown requested");
        ShutdownRequested?.Invoke(this, EventArgs.Empty);

        // Close the main window which will exit the message loop
        _mainWindow.Dispatcher.InvokeAsync(() =>
        {
            _mainWindow.Close();
        });

        _lifetime.StopApplication();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        RequestShutdown();
        return Task.CompletedTask;
    }
}
