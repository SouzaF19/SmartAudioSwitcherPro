using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;

namespace SmartAudioSwitcher.UI.Controls;

public class RoutingCanvas : Canvas
{
    private readonly List<Connection> _connections = new();
    private RoutingNode? _connectionSource = null;
    private Path? _previewLine = null;

    public event EventHandler<Connection>? ConnectionCreated;
    public event EventHandler<Connection>? ConnectionRemoved;

    public IReadOnlyList<Connection> Connections => _connections.AsReadOnly();

    public void ConnectNodes(RoutingNode source, RoutingNode target)
    {
        // Prevent self-connections and duplicate connections
        if (source == target) return;
        if (_connections.Any(c => c.Source == source && c.Target == target)) return;
        if (_connections.Any(c => c.Source == target && c.Target == source)) return;

        var line = CreateConnectionLine(source, target);
        var connection = new Connection(source, target, line);
        
        _connections.Add(connection);
        Children.Add(line);
        
        // Update node visual states
        source.IsConnectionSource = false;
        source.IsSelectedForConnection = false;
        target.IsSelectedForConnection = false;
        _connectionSource = null;

        ConnectionCreated?.Invoke(this, connection);
    }

    public void RemoveConnection(Connection connection)
    {
        if (_connections.Remove(connection))
        {
            Children.Remove(connection.Line);
            ConnectionRemoved?.Invoke(this, connection);
        }
    }

    public void ClearConnections()
    {
        foreach (var conn in _connections.ToList())
        {
            Children.Remove(conn.Line);
        }
        _connections.Clear();
    }

    public void UpdateConnections()
    {
        foreach (var connection in _connections)
        {
            connection.Line.Data = CreateBezierCurve(connection.Source, connection.Target);
        }
    }

    public void StartConnection(RoutingNode source)
    {
        // Cancel any existing connection attempt
        CancelConnection();

        _connectionSource = source;
        source.IsConnectionSource = true;
        
        // Create preview line from source to mouse
        _previewLine = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 180, 120)),
            StrokeThickness = 3,
            StrokeDashArray = new DoubleCollection { 8, 4 }
        };
        Children.Add(_previewLine);
    }

    public void UpdatePreviewLine(Point mousePosition)
    {
        if (_connectionSource != null && _previewLine != null)
        {
            var startPos = _connectionSource.GetCenter();
            var geometry = new PathGeometry();
            var figure = new PathFigure { StartPoint = startPos, IsClosed = false };
            
            var cp1 = new Point(startPos.X + 50, startPos.Y);
            var cp2 = new Point(mousePosition.X - 50, mousePosition.Y);
            
            figure.Segments.Add(new BezierSegment(cp1, cp2, mousePosition, true));
            geometry.Figures.Add(figure);
            _previewLine.Data = geometry;
        }
    }

    public void CancelConnection()
    {
        if (_connectionSource != null)
        {
            _connectionSource.IsConnectionSource = false;
            _connectionSource = null;
        }

        if (_previewLine != null)
        {
            Children.Remove(_previewLine);
            _previewLine = null;
        }
    }

    public void TryCompleteConnection(RoutingNode target)
    {
        if (_connectionSource != null && _connectionSource != target)
        {
            ConnectNodes(_connectionSource, target);
        }
        else
        {
            CancelConnection();
        }
    }

    private Path CreateConnectionLine(RoutingNode source, RoutingNode target)
    {
        var line = new Path
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
            StrokeThickness = 3,
            Data = CreateBezierCurve(source, target)
        };

        // Add click-to-remove functionality
        line.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2) // Double-click to remove
            {
                var connection = _connections.FirstOrDefault(c => c.Line == line);
                if (connection != null)
                {
                    RemoveConnection(connection);
                }
                e.Handled = true;
            }
        };

        // Highlight on hover
        line.MouseEnter += (s, e) =>
        {
            line.Stroke = new SolidColorBrush(Color.FromRgb(100, 200, 255));
            line.StrokeThickness = 4;
        };

        line.MouseLeave += (s, e) =>
        {
            line.Stroke = new SolidColorBrush(Color.FromRgb(0, 120, 215));
            line.StrokeThickness = 3;
        };

        return line;
    }

    private PathGeometry CreateBezierCurve(RoutingNode start, RoutingNode end)
    {
        var geometry = new PathGeometry();
        var figure = new PathFigure { StartPoint = start.GetCenter(), IsClosed = false };

        var startPos = start.GetCenter();
        var endPos = end.GetCenter();

        var cp1 = new Point(startPos.X + 50, startPos.Y);
        var cp2 = new Point(endPos.X - 50, endPos.Y);

        figure.Segments.Add(new BezierSegment(cp1, cp2, endPos, true));
        geometry.Figures.Add(figure);
        return geometry;
    }
}

public class Connection
{
    public RoutingNode Source { get; }
    public RoutingNode Target { get; }
    public Path Line { get; }

    public Connection(RoutingNode source, RoutingNode target, Path line)
    {
        Source = source;
        Target = target;
        Line = line;
    }
}