using System;
using System.Threading;
using SmartAudioSwitcher.UI.Controls;
using Xunit;
public class CanvasClearTests
{
 [Fact] public void Clear_connections_also_cancels_pending_preview()
 {
  Exception? error=null;
  var thread=new Thread(()=>{try{
   var canvas=new RoutingCanvas(); var node=new RoutingNode("App"); canvas.Children.Add(node);
   canvas.StartConnection(node); canvas.ClearConnections();
   Assert.Single(canvas.Children); Assert.False(node.IsConnectionSource);
  }catch(Exception e){error=e;}});
  thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
  if(error!=null)throw error;
 }
}
