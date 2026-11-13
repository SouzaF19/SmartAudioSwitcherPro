using System.Runtime.ExceptionServices;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.UI.Controls;
using Xunit;

public class RoutingNodeTests
{
    [Fact]
    public void DeviceNodeCanBeConstructedOnStaThread()
    {
        Exception? error = null;
        var thread = new Thread(() => { try { var node = new RoutingNode("Device", NodeType.Device); Assert.Equal(NodeType.Device, node.NodeType); Assert.NotNull(node.Content); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    [Fact]
    public void CompletingConnectionRemovesPreviewLine()
    {
        Exception? error = null;
        var thread = new Thread(() => { try {
            var canvas = new RoutingCanvas();
            var a = new RoutingNode("App", NodeType.App);
            var b = new RoutingNode("Device", NodeType.Device);
            System.Windows.Controls.Canvas.SetLeft(a, 0); System.Windows.Controls.Canvas.SetTop(a, 0);
            System.Windows.Controls.Canvas.SetLeft(b, 200); System.Windows.Controls.Canvas.SetTop(b, 0);
            canvas.Children.Add(a); canvas.Children.Add(b);
            canvas.StartConnection(a); canvas.TryCompleteConnection(b);
            Assert.Single(canvas.Connections);
            Assert.Single(canvas.Children.OfType<System.Windows.Shapes.Path>());
            Assert.False(a.IsConnectionSource);
        } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
