using Microsoft.Extensions.Logging.Abstractions;
using SmartAudioSwitcher.Core.Services;
using Xunit;
public class ConfigurationServiceTests {
 [Fact] public void Accepts_documented_snake_case_schema() { var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".yaml"); try { File.WriteAllText(path, @"devices:
  headset_patterns: [TestHeadset]
profiles:
- name: Work
  when:
  - process_match: app.exe
  routing:
    default: headset
    exceptions: {browser.exe: speakers}
    fallback_chain: [speakers]
    volume_limit: 0.5
"); var s=new ConfigurationService(NullLogger<ConfigurationService>.Instance,path).LoadSettings(); Assert.Equal("TestHeadset",s.Devices.HeadsetPatterns[0]); Assert.Equal("Work",s.Profiles[0].Name); Assert.Equal("headset",s.Profiles[0].DefaultDeviceType); } finally {File.Delete(path);} }
}
