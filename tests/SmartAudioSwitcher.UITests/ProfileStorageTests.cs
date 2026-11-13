using System;
using SmartAudioSwitcher.App.Services;
using Xunit;
public class ProfileStorageTests
{
 [Theory] [InlineData("../escape")] [InlineData("C:/escape")] [InlineData("bad/name")] [InlineData("CON")] [InlineData("LPT1")] [InlineData("")]
 public void Rejects_unsafe_names(string name) => Assert.Throws<ArgumentException>(()=>ProfileStorage.GetPath("C:/profiles",name));
 [Fact] public void Safe_name_stays_inside_directory() => Assert.Equal(System.IO.Path.Combine("C:/profiles","Gaming.json"),ProfileStorage.GetPath("C:/profiles","Gaming"));
}
