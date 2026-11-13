using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.UI.Controls;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using SmartAudioSwitcher.App.Services;

namespace SmartAudioSwitcher.App;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly ISystemTrayService _trayService;
    private readonly IAudioDeviceService _deviceService;
    private readonly IAudioSessionService _sessionService;
    private readonly IProcessMonitorService _processMonitor;
    private readonly ISonicFlowEngine _sonicFlowEngine;
    private bool _isClosing = false;
    private int _nodeCounter = 0;
    private string _profileDirectory = string.Empty;
    private RoutingNode? _connectionSourceNode = null;

    public MainWindow(
        ILogger<MainWindow> logger,
        ISystemTrayService trayService,
        IAudioDeviceService deviceService,
        IAudioSessionService sessionService,
        IProcessMonitorService processMonitor,
        ISonicFlowEngine sonicFlowEngine)
    {
        _logger = logger;
        _trayService = trayService;
        _deviceService = deviceService;
        _sessionService = sessionService;
        _processMonitor = processMonitor;
        _sonicFlowEngine = sonicFlowEngine;

        InitializeComponent();

        // DataContext set from Program.cs via DI - VM resolved by host lifetime
        // NO instantiation here - VM is injected through constructor and DataContext set in Program.cs
        this.ShowInTaskbar = true;

        // Setup profile directory
        _profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartAudioSwitcher", "Profiles");
        Directory.CreateDirectory(_profileDirectory);

        // Bind buttons to methods
        var btnAddApp = this.FindName("btnAddApp") as Button;
        if (btnAddApp != null) btnAddApp.Click += AddAppNode_Click;

        var btnAddDevice = this.FindName("btnAddDevice") as Button;
        if (btnAddDevice != null) btnAddDevice.Click += AddDeviceNode_Click;

        var btnSaveProfile = this.FindName("btnSaveProfile") as Button;
        if (btnSaveProfile != null) btnSaveProfile.Click += SaveProfile_Click;

        var btnLoadProfile = this.FindName("btnLoadProfile") as Button;
        if (btnLoadProfile != null) btnLoadProfile.Click += LoadProfile_Click;

        var btnClearCanvas = this.FindName("btnClearCanvas") as Button;
        if (btnClearCanvas != null) btnClearCanvas.Click += ClearCanvas_Click;

        var btnApplyRoutes = this.FindName("btnApplyRoutes") as Button;
        if (btnApplyRoutes != null) btnApplyRoutes.Click += ApplyRoutes_Click;

        var btnRefreshNodes = this.FindName("btnRefreshNodes") as Button;
        if (btnRefreshNodes != null) btnRefreshNodes.Click += RefreshNodes_Click;

        // Setup canvas events
        MainCanvas.ConnectionCreated += OnConnectionCreated;
        MainCanvas.ConnectionRemoved += OnConnectionRemoved;
        MainCanvas.MouseMove += OnCanvasMouseMove;
        MainCanvas.MouseLeftButtonUp += OnCanvasMouseLeftButtonUp;
        MainCanvas.MouseLeftButtonDown += OnCanvasMouseLeftButtonDown;

        // Subscribe to SonicFlow engine events
        _sonicFlowEngine.RouteApplied += OnRouteApplied;
        _sonicFlowEngine.RouteError += OnRouteError;

        // Populate initial nodes
        PopulateInitialNodes();
        RefreshProfileList();
    }

    private void PopulateInitialNodes()
    {
        // Add audio devices as device nodes
        var devices = _deviceService.GetOutputDevices().Where(d => d.IsConnected).ToList();
        foreach (var device in devices)
        {
            var node = new RoutingNode(device.FriendlyName, NodeType.Device)
            {
                DeviceId = device.Id
            };
            Canvas.SetLeft(node, 600);
            Canvas.SetTop(node, 50 + (_nodeCounter * 80));
            node.ConnectionRequested += OnNodeConnectionRequested;
            MainCanvas.Children.Add(node);
            _nodeCounter++;
        }

        // Add running audio processes as app nodes
        var processes = _processMonitor.GetRunningProcesses()
            .Where(p => p.HasMainWindow && !string.IsNullOrEmpty(p.ProcessName))
            .OrderBy(p => p.ProcessName)
            .Take(10) // Limit to top 10
            .ToList();

        foreach (var process in processes)
        {
            var node = new RoutingNode(process.ProcessName, NodeType.App)
            {
                ProcessName = process.ProcessName
            };
            Canvas.SetLeft(node, 50);
            Canvas.SetTop(node, 50 + (_nodeCounter * 80));
            node.ConnectionRequested += OnNodeConnectionRequested;
            MainCanvas.Children.Add(node);
            _nodeCounter++;
        }
    }

    private void RefreshNodes_Click(object sender, RoutedEventArgs e)
    {
        // Clear existing nodes but keep connections
        var nodes = MainCanvas.Children.OfType<RoutingNode>().ToList();
        foreach (var node in nodes)
        {
            node.ConnectionRequested -= OnNodeConnectionRequested;
            MainCanvas.Children.Remove(node);
        }
        _nodeCounter = 0;
        PopulateInitialNodes();

        _trayService.ShowNotification("SonicFlow", "Nodes refreshed with current devices and processes", NotificationType.Info);
    }

    private async void ApplyRoutes_Click(object sender, RoutedEventArgs e)
    {
        var routes = GetVisualRoutesFromCanvas();
        if (!routes.Any())
        {
            _trayService.ShowNotification("SonicFlow", "No routes to apply", NotificationType.Warning);
            return;
        }

        try
        {
            var devices = _deviceService.GetOutputDevices();
            var processes = _processMonitor.GetRunningProcesses();

            var appliedCount = await _sonicFlowEngine.ApplyRoutesAsync(routes, devices, processes);

            _trayService.ShowNotification("SonicFlow",
                $"Applied {appliedCount} of {routes.Count} routes",
                appliedCount > 0 ? NotificationType.Success : NotificationType.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying routes");
            _trayService.ShowNotification("SonicFlow", $"Error applying routes: {ex.Message}", NotificationType.Error);
        }
    }

    private List<VisualRoute> GetVisualRoutesFromCanvas()
    {
        var routes = new List<VisualRoute>();
        var nodes = MainCanvas.Children.OfType<RoutingNode>().ToDictionary(n => n.GetHashCode().ToString());

        foreach (var conn in MainCanvas.Connections)
        {
            var sourceId = conn.Source.GetHashCode().ToString();
            var targetId = conn.Target.GetHashCode().ToString();

            if (nodes.TryGetValue(sourceId, out var source) &&
                nodes.TryGetValue(targetId, out var target))
            {
                // Ensure source is app, target is device
                RoutingNode appNode, deviceNode;
                if (source.NodeType == NodeType.App && target.NodeType == NodeType.Device)
                {
                    appNode = source;
                    deviceNode = target;
                }
                else if (source.NodeType == NodeType.Device && target.NodeType == NodeType.App)
                {
                    appNode = target;
                    deviceNode = source;
                }
                else
                {
                    _logger.LogWarning("Invalid connection: {SourceType} -> {TargetType}", source.NodeType, target.NodeType);
                    continue;
                }

                routes.Add(new VisualRoute(
                    SourceNodeId: sourceId,
                    SourceTitle: appNode.NodeTitle,
                    SourceType: appNode.NodeType,
                    SourceProcessName: appNode.ProcessName,
                    SourceDeviceId: appNode.DeviceId,
                    TargetNodeId: targetId,
                    TargetTitle: deviceNode.NodeTitle,
                    TargetType: deviceNode.NodeType,
                    TargetProcessName: deviceNode.ProcessName,
                    TargetDeviceId: deviceNode.DeviceId));
            }
        }

        return routes;
    }

    private void AddAppNode_Click(object sender, RoutedEventArgs e)
    {
        _nodeCounter++;
        var node = new RoutingNode($"App {_nodeCounter}", NodeType.App);
        Canvas.SetLeft(node, 50 + (_nodeCounter * 20));
        Canvas.SetTop(node, 50 + (_nodeCounter * 20));
        node.ConnectionRequested += OnNodeConnectionRequested;
        MainCanvas.Children.Add(node);
    }

    private void AddDeviceNode_Click(object sender, RoutedEventArgs e)
    {
        _nodeCounter++;
        var node = new RoutingNode($"Device {_nodeCounter}", NodeType.Device);
        Canvas.SetLeft(node, 600);
        Canvas.SetTop(node, 50 + (_nodeCounter * 20));
        node.ConnectionRequested += OnNodeConnectionRequested;
        MainCanvas.Children.Add(node);
    }

    private void OnNodeConnectionRequested(object? sender, RoutingNode node)
    {
        // Don't allow connecting if already has connections
        if (MainCanvas.Connections.Any(c => c.Source == node || c.Target == node))
        {
            return;
        }

        if (_connectionSourceNode == null)
        {
            // First click - start connection from this node
            MainCanvas.StartConnection(node);
            _connectionSourceNode = node;
        }
        else if (_connectionSourceNode != node)
        {
            // Second click - complete connection
            MainCanvas.TryCompleteConnection(node);
            _connectionSourceNode = null;
        }
        else
        {
            // Clicked same node - cancel
            MainCanvas.CancelConnection();
            _connectionSourceNode = null;
        }
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_connectionSourceNode != null)
        {
            var pos = e.GetPosition(MainCanvas);
            MainCanvas.UpdatePreviewLine(pos);
        }
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_connectionSourceNode != null)
        {
            var hitTest = VisualTreeHelper.HitTest(MainCanvas, e.GetPosition(MainCanvas));
            if (hitTest?.VisualHit is not RoutingNode)
            {
                MainCanvas.CancelConnection();
                _connectionSourceNode = null;
            }
        }
    }

    private void OnCanvasMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        MainCanvas.Focus();
    }

    private void OnConnectionCreated(object? sender, Connection connection)
    {
        _logger.LogInformation("Connection created: {Source} -> {Target}", connection.Source.NodeTitle, connection.Target.NodeTitle);

        connection.Source.IsConnectionSource = false;
        connection.Target.IsSelectedForConnection = false;
    }

    private void OnConnectionRemoved(object? sender, Connection connection)
    {
        _logger.LogInformation("Connection removed: {Source} -> {Target}", connection.Source.NodeTitle, connection.Target.NodeTitle);
    }

    private void OnRouteApplied(object? sender, RouteAppliedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.Success)
            {
                _logger.LogInformation("Route applied: {Process} -> {Device}", e.ProcessName, e.DeviceName);
            }
        });
    }

    private void OnRouteError(object? sender, RouteErrorEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _logger.LogError("Route error: {Source} -> {Target}: {Error}", e.SourceName, e.TargetName, e.ErrorMessage);
        });
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = CreateProfileFromCanvas();
        if (profile == null) return;

        var profileName = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter profile name:", "Save SonicFlow Profile", "MyProfile");

        if (string.IsNullOrWhiteSpace(profileName)) return;

        var fileName = $"{profileName}.json";
        var filePath = Path.Combine(_profileDirectory, fileName);

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(profile, options);
            File.WriteAllText(filePath, json);

            _trayService.ShowNotification("SonicFlow", $"Profile '{profileName}' saved successfully",
                NotificationType.Success);
            _logger.LogInformation("Profile saved: {Path}", filePath);

            RefreshProfileList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save profile");
            _trayService.ShowNotification("SonicFlow", "Failed to save profile", NotificationType.Error);
        }
    }

    private void LoadProfile_Click(object sender, RoutedEventArgs e)
    {
        var files = Directory.GetFiles(_profileDirectory, "*.json");
        if (files.Length == 0)
        {
            _trayService.ShowNotification("SonicFlow", "No saved profiles found", NotificationType.Info);
            return;
        }

        var filePath = files[0];
        LoadProfileFromFile(filePath);
    }

    private void LoadDefaultProfile()
    {
        var defaultPath = Path.Combine(_profileDirectory, "default.json");
        if (File.Exists(defaultPath))
        {
            LoadProfileFromFile(defaultPath);
        }
    }

    private void LoadProfileFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var profile = JsonSerializer.Deserialize<SonicFlowProfile>(json);

            if (profile != null)
            {
                ClearCanvasInternal();
                RestoreProfileToCanvas(profile);

                _trayService.ShowNotification("SonicFlow", $"Profile '{profile.Name}' loaded", NotificationType.Success);
                _logger.LogInformation("Profile loaded: {Path}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profile from {Path}", filePath);
            _trayService.ShowNotification("SonicFlow", "Failed to load profile", NotificationType.Error);
        }
    }

    private void ClearCanvas_Click(object sender, RoutedEventArgs e)
    {
        ClearCanvasInternal();
    }

    private void ClearCanvasInternal()
    {
        MainCanvas.ClearConnections();

        var nodes = MainCanvas.Children.OfType<RoutingNode>().ToList();
        foreach (var node in nodes)
        {
            node.ConnectionRequested -= OnNodeConnectionRequested;
            MainCanvas.Children.Remove(node);
        }

        _nodeCounter = 0;
    }

    private SonicFlowProfile? CreateProfileFromCanvas()
    {
        var nodes = MainCanvas.Children.OfType<RoutingNode>().ToList();
        if (!nodes.Any()) return null;

        var profile = new SonicFlowProfile
        {
            Name = "Untitled",
            CreatedAt = DateTime.Now,
            ModifiedAt = DateTime.Now,
            Nodes = new List<NodeData>(),
            Connections = new List<ConnectionData>()
        };

        foreach (var node in nodes)
        {
            profile.Nodes.Add(new NodeData
            {
                Id = node.GetHashCode().ToString(),
                Title = node.NodeTitle,
                Type = node.NodeType.ToString(),
                Left = Canvas.GetLeft(node),
                Top = Canvas.GetTop(node),
                ProcessName = node.ProcessName,
                DeviceId = node.DeviceId
            });
        }

        foreach (var conn in MainCanvas.Connections)
        {
            var sourceNodeData = profile.Nodes.FirstOrDefault(n => n.Id == conn.Source.GetHashCode().ToString());
            var targetNodeData = profile.Nodes.FirstOrDefault(n => n.Id == conn.Target.GetHashCode().ToString());

            if (sourceNodeData != null && targetNodeData != null)
            {
                profile.Connections.Add(new ConnectionData
                {
                    SourceId = sourceNodeData.Id,
                    TargetId = targetNodeData.Id
                });
            }
        }

        return profile;
    }

    private void RestoreProfileToCanvas(SonicFlowProfile profile)
    {
        var nodeMap = new Dictionary<string, RoutingNode>();

        foreach (var nodeData in profile.Nodes)
        {
            var nodeType = Enum.TryParse<NodeType>(nodeData.Type, out var parsed) ? parsed : NodeType.App;
            var node = new RoutingNode(nodeData.Title, nodeType)
            {
                ProcessName = nodeData.ProcessName,
                DeviceId = nodeData.DeviceId
            };

            Canvas.SetLeft(node, nodeData.Left);
            Canvas.SetTop(node, nodeData.Top);
            node.ConnectionRequested += OnNodeConnectionRequested;
            MainCanvas.Children.Add(node);

            nodeMap[nodeData.Id] = node;
        }

        foreach (var connData in profile.Connections)
        {
            if (nodeMap.TryGetValue(connData.SourceId, out var source) &&
                nodeMap.TryGetValue(connData.TargetId, out var target))
            {
                MainCanvas.ConnectNodes(source, target);
            }
        }

        _nodeCounter = profile.Nodes.Count;
    }

    private void RefreshProfileList()
    {
        var profileList = this.FindName("ProfileList") as ListBox;
        if (profileList != null)
        {
            var files = Directory.GetFiles(_profileDirectory, "*.json");
            profileList.ItemsSource = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToList();
        }
    }

    private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var listBox = sender as ListBox;
        if (listBox?.SelectedItem is string profileName)
        {
            var filePath = Path.Combine(_profileDirectory, $"{profileName}.json");
            if (File.Exists(filePath))
            {
                LoadProfileFromFile(filePath);
            }
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("MainWindow loaded");
        InitializeTrayMenu();
        RefreshProfileList();
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
            NotificationType.Info);
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
            if (_connectionSourceNode != null)
            {
                MainCanvas.CancelConnection();
                _connectionSourceNode = null;
            }
            else
            {
                HideWindow();
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}

// Data models for profile persistence
public class SonicFlowProfile
{
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public List<NodeData> Nodes { get; set; } = new();
    public List<ConnectionData> Connections { get; set; } = new();
}

public class NodeData
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Type { get; set; } = "App";
    public double Left { get; set; }
    public double Top { get; set; }
    public string? ProcessName { get; set; }
    public string? DeviceId { get; set; }
}

public class ConnectionData
{
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
}
