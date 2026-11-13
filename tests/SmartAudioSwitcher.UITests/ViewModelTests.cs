using SmartAudioSwitcher.App.ViewModels;
using SmartAudioSwitcher.Core.Models;
using Xunit;
public class ViewModelTests
{
 [Fact] public void Active_profile_notifies_binding()
 {
  var model=new ProfileViewModel(new Profile{Name="Gaming"});string? changed=null;
  model.PropertyChanged+=(_,e)=>changed=e.PropertyName;model.IsActive=true;
  Assert.Equal("IsActive",changed);
 }
}
