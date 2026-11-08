using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.UI.Controls;
using System.Collections.Generic;
using System.Windows.Controls;
using System.IO;
using System.Text.Json;

namespace SmartAudioSwitcher.App;

public partial class MainWindow : Window
{
    private readonly ILogger<MainWindow> _logger;
    private readonly ISystemTrayService _trayService;
    private bool _isClosing = false;
    private int _nodeCounter = 0;
    private string _profileDirectory = string.Empty;

    public MainWindow(ILogger<MainWindow> logger, ISystemTrayService trayService)
    {
        _logger = logger;
        _trayService = trayService;
        InitializeComponent();
        
        this.ShowInTaskbar = true;
        
        // Setup profile directory
        _profileDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SmartAudioSwitcher", "Profiles");
        Directory.CreateDirectory(_profileDirectory);

        // Bind buttons to methods
        var btnAddApp = this.FindName("btnAddApp") as System.Windows.Controls.Button;
        if (btnAddApp != null) btnAddApp.Click += AddAppNode_Click;

        var btnAddDevice = this.FindName("btnAddDevice") as System.Windows.Controls.Button;
        if (btnAddDevice != null) btnAddDevice.Click += AddDeviceNode_Click;

        var btnSaveProfile = this.FindName("btnSaveProfile") as System.Windows.Controls.Button;
        if (btnSaveProfile != null) btnSaveProfile.Click += SaveProfile_Click;

        var btnLoadProfile = this.FindName("btnLoadProfile") as System.Windows.Controls.Button;
        if (btnLoadProfile != null) btnLoadProfile.Click += LoadProfile_Click;

        var btnClearCanvas = this.FindName("btnClearCanvas") as System.Windows.Controls.Button;
        if (btnClearCanvas != null) btnClearCanvas.Click += ClearCanvas_Click;

        // Setup canvas events
        MainCanvas.ConnectionCreated += OnConnectionCreated;
        MainCanvas.ConnectionRemoved += OnConnectionRemoved;
        MainCanvas.MouseMove += OnCanvasMouseMove;
        MainCanvas.MouseLeftButtonUp += OnCanvasMouseLeftButtonUp;
        MainCanvas.MouseLeftButtonDown += OnCanvasMouseLeftButtonDown;
        
        // Load default profile on startup
        LoadDefaultProfile();
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
        if (MainCanvas.Connections.Any(c => c.Source == node || c.Target == node))
        {
            // Node already connected - could show context menu to disconnect
            return;
        }

        if (MainCanvas.Connections.Count == 0 && _connectionSourceNode == null)
        {
            // First click - start connection from this node
            MainCanvas.StartConnection(node);
            _connectionSourceNode = node;
        }
        else if (_connectionSourceNode != null && _connectionSourceNode != node)
        {
            // Second click - complete connection
            MainCanvas.TryCompleteConnection(node);
            _connectionSourceNode = null;
        }
        else if (_connectionSourceNode == node)
        {
            // Clicked same node - cancel
            MainCanvas.CancelConnection();
            _connectionSourceNode = null;
        }
    }

    private RoutingNode? _connectionSourceNode = null;

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (MainCanvas.Connections.Any(c => c.Source.IsConnectionSource) || _connectionSourceNode != null)
        {
            var pos = e.GetPosition(MainCanvas);
            MainCanvas.UpdatePreviewLine(pos);
        }
    }

    private void OnCanvasMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // If we have a connection source but clicked on empty canvas, cancel
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
        // Focus the canvas to receive keyboard events
        MainCanvas.Focus();
    }

    private void OnConnectionCreated(object? sender, Connection connection)
    {
        _logger.LogInformation("Connection created: {Source} -> {Target}", 
            connection.Source.NodeTitle, connection.Target.NodeTitle);
        
        // Visual feedback
        connection.Source.IsConnectionSource = false;
        connection.Target.IsSelectedForConnection = false;
    }

    private void OnConnectionRemoved(object? sender, Connection connection)
    {
        _logger.LogInformation("Connection removed: {Source} -> {Target}", 
            connection.Source.NodeTitle, connection.Target.NodeTitle);
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = CreateProfileFromCanvas();
        if (profile == null) return;

        // Simple save dialog simulation - in real app would use SaveFileDialog
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
        // In a real app, this would show a file picker. For now, load the first available.
        var files = Directory.GetFiles(_profileDirectory, "*.json");
        if (files.Length == 0)
        {
            _trayService.ShowNotification("SonicFlow", "No saved profiles found", NotificationType.Info);
            return;
        }

        var filePath = files[0]; // Load first for now
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
        var profileList = this.FindName("ProfileList") as System.Windows.Controls.ListBox;
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
        else if (e.Key == Key.Delete)
        {
            // Delete selected nodes/connections - could implement selection logic
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