using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Input;

namespace SmartAudioSwitcher.UI.Controls;

public class RoutingNode : UserControl
{
    public static readonly DependencyProperty NodeTitleProperty = 
        DependencyProperty.Register("NodeTitle", typeof(string), typeof(RoutingNode), new PropertyMetadata("Node"));

    public string NodeTitle
    {
        get => (string)GetValue(NodeTitleProperty);
        set => SetValue(NodeTitleProperty, value);
    }

    public RoutingNode()
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Width = 150,
            Height = 60,
            Child = new Grid
            {
                Children = {
                    new TextBlock
                    {
                        Text = NodeTitle,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        FontWeight = FontWeights.SemiBold
                    }
                }
            }
        };
        Content = border;

        // Basic drag and drop logic
        this.MouseDown += (s, e) => {
            if (e.LeftButton == MouseButtonState.Pressed) {
                this.CaptureMouse();
            }
        };

        this.MouseMove += (s, e) => {
            if (this.IsMouseCaptured) {
                var pos = e.GetPosition(this.Parent as UIElement);
                Canvas.SetLeft(this, pos.X - 75);
                Canvas.SetTop(this, pos.Y - 30);
            }
        };

        this.MouseUp += (s, e) => {
            this.ReleaseMouseCapture();
        };
    }
}
