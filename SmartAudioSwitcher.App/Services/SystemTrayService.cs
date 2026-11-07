using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using Hardcodet.Wpf.TaskbarNotification;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.App.Services;

public class SystemTrayService : ISystemTrayService, IDisposable
{
    private readonly ILogger<SystemTrayService> _logger;
    private readonly TaskbarIcon _taskbarIcon;
    private bool _disposed;

    public event EventHandler<TrayMenuAction>? MenuAction;

    public SystemTrayService(ILogger<SystemTrayService> logger)
    {
        _logger = logger;
        
        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "SmartAudioSwitcher",
            Visibility = Visibility.Visible
        };

        _taskbarIcon.Icon = CreateDefaultIcon();
    }

    public void Initialize()
    {
        _logger.LogDebug("System tray initialized");
    }

    public void ShowNotification(string title, string message, NotificationType type = NotificationType.Info)
    {
        var icon = type switch
        {
            NotificationType.Warning => System.Windows.Forms.ToolTipIcon.Warning,
            NotificationType.Error => System.Windows.Forms.ToolTipIcon.Error,
            NotificationType.Success => System.Windows.Forms.ToolTipIcon.Info,
            _ => System.Windows.Forms.ToolTipIcon.Info
        };

        _taskbarIcon.ShowBalloonTip(title, message, (BalloonIcon)icon);
    }

    public void UpdateIcon(bool isActive, string? tooltip = null)
    {
        if (tooltip != null)
        {
            _taskbarIcon.ToolTipText = tooltip;
        }
    }

    public void SetContextMenu(IReadOnlyList<TrayMenuItem> items)
    {
        var contextMenu = new ContextMenu();
        
        foreach (var item in items)
        {
            contextMenu.Items.Add(CreateMenuItem(item));
        }

        _taskbarIcon.ContextMenu = contextMenu;
    }

    private MenuItem CreateMenuItem(TrayMenuItem item)
    {
        var menuItem = new MenuItem
        {
            Header = item.Header,
            IsEnabled = item.IsEnabled,
            IsChecked = item.IsChecked,
            Tag = item.Id
        };

        if (item.SubItems?.Count > 0)
        {
            foreach (var subItem in item.SubItems)
            {
                menuItem.Items.Add(CreateMenuItem(subItem));
            }
        }
        else
        {
            menuItem.Click += (s, e) => 
            {
                MenuAction?.Invoke(this, new TrayMenuAction(item.Id, null));
            };
        }

        return menuItem;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _taskbarIcon?.Dispose();
            _disposed = true;
        }
    }

    private System.Drawing.Icon CreateDefaultIcon()
    {
        var bitmap = new System.Drawing.Bitmap(16, 16);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.Clear(System.Drawing.Color.Transparent);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.DodgerBlue);
            g.FillEllipse(brush, 2, 2, 12, 12);
            using var whiteBrush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
            g.FillPolygon(whiteBrush, new[]
            {
                new System.Drawing.Point(5, 5),
                new System.Drawing.Point(9, 8),
                new System.Drawing.Point(5, 11)
            });
        }
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
}