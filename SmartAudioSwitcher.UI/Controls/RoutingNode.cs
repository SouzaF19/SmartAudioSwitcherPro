using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Input;
using System.Collections.Generic;
using SmartAudioSwitcher.Core.Models;

namespace SmartAudioSwitcher.UI.Controls;

public class RoutingNode : UserControl
{
    public static readonly DependencyProperty NodeTitleProperty = 
        DependencyProperty.Register("NodeTitle", typeof(string), typeof(RoutingNode), new PropertyMetadata("Node"));

    public static readonly DependencyProperty NodeTypeProperty = 
        DependencyProperty.Register("NodeType", typeof(NodeType), typeof(RoutingNode), new PropertyMetadata(NodeType.App, OnNodeTypeChanged));

    public static readonly DependencyProperty IsSelectedForConnectionProperty = 
        DependencyProperty.Register("IsSelectedForConnection", typeof(bool), typeof(RoutingNode), new PropertyMetadata(false, OnSelectionChanged));

    public static readonly DependencyProperty IsConnectionSourceProperty = 
        DependencyProperty.Register("IsConnectionSource", typeof(bool), typeof(RoutingNode), new PropertyMetadata(false, OnSelectionChanged));

    public string NodeTitle
    {
        get => (string)GetValue(NodeTitleProperty);
        set => SetValue(NodeTitleProperty, value);
    }

    public NodeType NodeType
    {
        get => (NodeType)GetValue(NodeTypeProperty);
        set => SetValue(NodeTypeProperty, value);
    }

    public bool IsSelectedForConnection
    {
        get => (bool)GetValue(IsSelectedForConnectionProperty);
        set => SetValue(IsSelectedForConnectionProperty, value);
    }

    public bool IsConnectionSource
    {
        get => (bool)GetValue(IsConnectionSourceProperty);
        set => SetValue(IsConnectionSourceProperty, value);
    }

    // Data properties for persistence
    public string? ProcessName { get; set; }
    public string? DeviceId { get; set; }

    public event EventHandler<RoutingNode>? ConnectionRequested;

    private Border _border = null!;
    private TextBlock _textBlock = null!;

    public RoutingNode(string title, NodeType nodeType = NodeType.App)
    {
        NodeTitle = title;
        NodeType = nodeType;
        InitializeVisuals();
        InitializeInteraction();
    }

    private void InitializeVisuals()
    {
        _textBlock = new TextBlock
        {
            Text = NodeTitle,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold
        };

        _border = new Border
        {
            Background = GetBackgroundForType(NodeType),
            BorderBrush = GetBorderBrushForType(NodeType),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Width = 150,
            Height = 60,
            Child = new Grid
            {
                Children = { _textBlock }
            }
        };

        Content = _border;
        UpdateVisualState();
    }

    private void InitializeInteraction()
    {
        this.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 1)
            {
                this.CaptureMouse();
                ConnectionRequested?.Invoke(this, this);
                e.Handled = true;
            }
        };

        this.MouseMove += (s, e) =>
        {
            if (this.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            {
                var pos = e.GetPosition(this.Parent as UIElement);
                Canvas.SetLeft(this, pos.X - 75);
                Canvas.SetTop(this, pos.Y - 30);
                
                if (this.Parent is RoutingCanvas canvas)
                {
                    canvas.UpdateConnections();
                }
            }
        };

        this.MouseLeftButtonUp += (s, e) =>
        {
            this.ReleaseMouseCapture();
        };
    }

    private static void OnNodeTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RoutingNode node)
        {
            node._border.Background = GetBackgroundForType(node.NodeType);
            node._border.BorderBrush = GetBorderBrushForType(node.NodeType);
        }
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RoutingNode node)
        {
            node.UpdateVisualState();
        }
    }

    private void UpdateVisualState()
    {
        if (_border == null) return;

        var baseBrush = GetBackgroundForType(NodeType);
        var baseBorderBrush = GetBorderBrushForType(NodeType);

        if (IsConnectionSource)
        {
            _border.Background = new SolidColorBrush(Color.FromRgb(0, 180, 120));
            _border.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 255, 180));
            _border.BorderThickness = new Thickness(3);
            _border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 180, 120),
                BlurRadius = 15,
                ShadowDepth = 0,
                Opacity = 0.6
            };
        }
        else if (IsSelectedForConnection)
        {
            _border.Background = new SolidColorBrush(Color.FromRgb(80, 120, 180));
            _border.BorderBrush = new SolidColorBrush(Color.FromRgb(120, 180, 255));
            _border.BorderThickness = new Thickness(3);
            _border.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 120, 215),
                BlurRadius = 10,
                ShadowDepth = 0,
                Opacity = 0.5
            };
        }
        else
        {
            _border.Background = baseBrush;
            _border.BorderBrush = baseBorderBrush;
            _border.BorderThickness = new Thickness(2);
            _border.Effect = null;
        }
    }

    private static SolidColorBrush GetBackgroundForType(NodeType type)
    {
        return type switch
        {
            NodeType.Device => new SolidColorBrush(Color.FromRgb(30, 60, 90)),
            NodeType.App => new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            _ => new SolidColorBrush(Color.FromRgb(45, 45, 48))
        };
    }

    private static SolidColorBrush GetBorderBrushForType(NodeType type)
    {
        return type switch
        {
            NodeType.Device => new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            NodeType.App => new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            _ => new SolidColorBrush(Color.FromRgb(63, 63, 70))
        };
    }

    public Point GetCenter()
    {
        return new Point(Canvas.GetLeft(this) + 75, Canvas.GetTop(this) + 30);
    }
}