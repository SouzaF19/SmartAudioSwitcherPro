using System.Windows;
using MaterialDesignThemes.Wpf;

namespace SmartAudioSwitcher.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Set default MaterialDesign theme
        var app = Application.Current;
        var theme = app.Resources.MergedDictionaries
            .OfType<ResourceDictionary>()
            .FirstOrDefault(d => d.Contains("MaterialDesignTheme.Light"));

        if (theme != null)
        {
            var colorSource = app.Resources.MergedDictionaries
                .OfType<ResourceDictionary>()
                .FirstOrDefault(d => d.Contains("MaterialDesignColor.Blue"));

            if (colorSource != null)
            {
                app.Resources.MergedDictionaries[0] = colorSource;
            }
        }
    }
}