using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Services;
using Xunit;
public sealed class ConfigRegressionTests:IDisposable
{
 readonly string directory=Path.Combine(Path.GetTempPath(),"sas-tests-"+Guid.NewGuid());
 string PathName=>Path.Combine(directory,"config.yaml");
 ConfigurationService Create(string yaml){Directory.CreateDirectory(directory);File.WriteAllText(PathName,yaml);return new(NullLogger<ConfigurationService>.Instance,PathName);}
 [Fact] public void Documented_keys_are_not_silently_ignored()
 {
  var service=Create("general:\n  start_minimized: false\n  check_updates: false\nprofiles:\n- name: Custom\n  when: []\n  routing:\n    default: headset\n".Replace("\n","\n"));
  var settings=service.LoadSettings();Assert.False(settings.General.StartMinimized);Assert.False(settings.General.CheckUpdates);Assert.Equal("headset",settings.Profiles[0].DefaultDeviceType);
 }
 [Fact] public void Unknown_keys_retain_last_valid_and_leave_invalid_file_untouched()
 {
  var service=Create("general:\n  language: pt-BR\n".Replace("\n","\n"));Assert.Equal("pt-BR",service.LoadSettings().General.Language);
  const string invalid="general:\n  typo_key: 3\n";File.WriteAllText(PathName,invalid.Replace("\n","\n"));
  Assert.Equal("pt-BR",service.LoadSettings().General.Language);Assert.Equal(invalid.Replace("\n","\n"),File.ReadAllText(PathName));
 }
 [Fact] public void Legacy_camel_case_roundtrips()
 {
  var service=Create("general:\n  startMinimized: false\n".Replace("\n","\n"));var settings=service.LoadSettings();Assert.False(settings.General.StartMinimized);
  settings.General.Language="pt-BR";service.SaveSettings(settings);Assert.Equal("pt-BR",service.LoadSettings().General.Language);
 }
 public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
