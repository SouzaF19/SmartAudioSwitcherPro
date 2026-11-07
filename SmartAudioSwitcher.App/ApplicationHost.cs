using SmartAudioSwitcher.Core.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.App;

public class ApplicationHost : IApplicationHost, IHostedService
{
    private readonly ILogger<ApplicationHost> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly MainWindow _mainWindow;
    private TaskCompletionSource<object?>? _runTcs;

    public event EventHandler? ShutdownRequested;

    public ApplicationHost(ILogger<ApplicationHost> logger, IHostApplicationLifetime lifetime, MainWindow mainWindow)
    {
        _logger = logger;
        _lifetime = lifetime;
        _mainWindow = mainWindow;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _runTcs = new TaskCompletionSource<object?>();
        
        _logger.LogInformation("Application host starting...");
        
        // Start the WPF message loop on the UI thread
        _ = Task.Run(() =>
        {
            try
            {
                _mainWindow.Show();
                System.Windows.Threading.Dispatcher.Run();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI thread error");
                _runTcs?.TrySetException(ex);
            }
        });

        // Wait for shutdown signal
        using var reg = cancellationToken.Register(() => 
        {
            _runTcs?.TrySetCanceled();
            _lifetime.StopApplication();
        });

        await _runTcs.Task;
        
        _logger.LogInformation("Application host stopped");
    }

    public void RequestShutdown()
    {
        _logger.LogInformation("Shutdown requested");
        ShutdownRequested?.Invoke(this, EventArgs.Empty);
        
        _mainWindow.Dispatcher.InvokeAsync(() =>
        {
            _mainWindow.Close();
            System.Windows.Threading.Dispatcher.ExitAllFrames();
        });
        
        _runTcs?.TrySetResult(null);
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