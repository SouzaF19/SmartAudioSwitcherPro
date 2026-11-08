using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;

namespace SmartAudioSwitcher.UI.Controls;

public class RoutingCanvas : Canvas
{
    public void ConnectNodes(RoutingNode source, RoutingNode target)
    {
        var line = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            StrokeThickness = 3,
            Data = CreateBezierCurve(source, target)
        };
        Children.Add(line);
    }

    private PathGeometry CreateBezierCurve(RoutingNode start, RoutingNode end)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = GetCenter(start), IsClosed = false };

        var startPos = GetCenter(start);
        var endPos = GetCenter(end);

        var cp1 = new Point(startPos.X + 50, startPos.Y);
        var cp2 = new Point(endPos.X - 50, endPos.Y);

        figure.Segments.Add(new BezierSegment(cp1, cp2, endPos, true));
        geometry.Figures.Add(figure);
        return geometry;
    }

    private Point GetCenter(RoutingNode node)
    {
        return new Point(Canvas.GetLeft(node) + 75, Canvas.GetTop(node) + 30);
    }
}
