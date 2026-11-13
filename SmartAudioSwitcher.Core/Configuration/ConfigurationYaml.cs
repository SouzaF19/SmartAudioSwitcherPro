using System.Globalization;
using SmartAudioSwitcher.Core.Models;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
namespace SmartAudioSwitcher.Core.Configuration;
internal static class ConfigurationYaml
{
 sealed class TriggerObjectFactory:YamlDotNet.Serialization.ObjectFactories.DefaultObjectFactory
 {public override object Create(Type type)=>type==typeof(ProfileTrigger)?new ProfileTrigger(ProfileTriggerType.Always,null,null):base.Create(type);}
 static readonly IDeserializer Reader=new DeserializerBuilder().WithObjectFactory(new TriggerObjectFactory()).WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
 static string Normalize(string key)=>key.Replace("_","").ToLowerInvariant();
 static YamlMappingNode Map(YamlNode node)=>node as YamlMappingNode ?? throw new FormatException("Expected a YAML mapping");
 static void Properties(YamlMappingNode map,Type type,params (string Key,string Value)[] aliases)
 {
  var names=type.GetProperties().ToDictionary(p=>Normalize(p.Name),p=>char.ToLowerInvariant(p.Name[0])+p.Name[1..]);
  foreach(var alias in aliases) names[Normalize(alias.Key)]=alias.Value;
  var entries=map.Children.ToArray();map.Children.Clear();
  foreach(var pair in entries)
  {
   var key=(pair.Key as YamlScalarNode)?.Value ?? throw new FormatException("Expected property name");
   if(!names.TryGetValue(Normalize(key),out var name))throw new FormatException($"Unknown {type.Name} property: {key}");
   if(map.Children.ContainsKey(new YamlScalarNode(name))) throw new FormatException($"Duplicate property: {name}");
   map.Add(name,pair.Value);
  }
 }
 static YamlNode? Get(YamlMappingNode map,string key)=>map.Children.TryGetValue(new YamlScalarNode(key),out var value)?value:null;
 static IEnumerable<YamlNode> Items(YamlNode node)=> (node as YamlSequenceNode)?.Children ?? throw new FormatException("Expected a YAML sequence");
 public static AppSettings Deserialize(string yaml,out bool hasProfiles)
 {
  var stream=new YamlStream();stream.Load(new StringReader(yaml));
  if(stream.Documents.Count!=1)throw new FormatException("Expected one YAML document");
  var root=Map(stream.Documents[0].RootNode);Properties(root,typeof(AppSettings));
  if(Get(root,"general") is {} general)Properties(Map(general),typeof(GeneralSettings));
  if(Get(root,"devices") is {} devices)Properties(Map(devices),typeof(DeviceMatchingSettings),("headset","headsetPatterns"),("speakers","speakersPatterns"),("hdmi","hdmiPatterns"),("virtual","virtualPatterns"));
  if(Get(root,"api") is {} api)Properties(Map(api),typeof(ApiSettings));
  if(Get(root,"hotkeys") is {} hotkeys)Properties(Map(hotkeys),typeof(HotkeySettings));
  hasProfiles=Get(root,"profiles")!=null;
  if(Get(root,"profiles") is {} profiles)
  foreach(var node in Items(profiles))
  {
   var profile=Map(node);
   if(Get(profile,"when") is {} when)
   {
    profile.Children.Remove(new YamlScalarNode("when"));
    if(Get(profile,"triggers")!=null)throw new FormatException("Use when OR triggers, not both");
    var triggers=new YamlSequenceNode();
    foreach(var condition in Items(when))
    foreach(var pair in Map(condition).Children)
    {
     var raw=((YamlScalarNode)pair.Key).Value ?? "";
     var type=Enum.GetNames<ProfileTriggerType>().FirstOrDefault(n=>Normalize(n)==Normalize(raw)) ?? throw new FormatException($"Unknown trigger: {raw}");
     triggers.Add(new YamlMappingNode{{"type",new YamlScalarNode(type)},{"value",pair.Value}});
    }
    profile.Add("triggers",triggers);
   }
   if(Get(profile,"routing") is {} routing)
   {
    profile.Children.Remove(new YamlScalarNode("routing"));var routes=Map(routing);
    Properties(routes,typeof(Profile),("default","defaultDeviceType"),("exceptions","processExceptions"));
    foreach(var pair in routes.Children)
    {
     var key=((YamlScalarNode)pair.Key).Value;
     if(key is not ("defaultDeviceType" or "processExceptions" or "fallbackChain" or "volumeLimit"))throw new FormatException($"Invalid routing key: {key}");
     if(profile.Children.ContainsKey(pair.Key))throw new FormatException($"Duplicate profile property: {key}");
     profile.Add(pair.Key,pair.Value);
    }
   }
   Properties(profile,typeof(Profile),("default","defaultDeviceType"),("exceptions","processExceptions"));
   if(Get(profile,"triggers") is {} triggerList)
    foreach(var trigger in Items(triggerList))Properties(Map(trigger),typeof(ProfileTrigger));
   if(Get(profile,"processExceptions") is YamlSequenceNode exceptions)
   {
    var mapped=new YamlMappingNode();
    foreach(var exception in exceptions.Children)
    {
     var item=Map(exception);
     if(item.Children.Count!=2 || Get(item,"process") is not YamlScalarNode process || Get(item,"device") is not YamlScalarNode device)throw new FormatException("Exception requires process and device");
     mapped.Add(process,device);
    }
    profile.Children[new YamlScalarNode("processExceptions")]=mapped;
   }
  }
  using var writer=new StringWriter(CultureInfo.InvariantCulture);stream.Save(writer,false);
  var settings=Reader.Deserialize<AppSettings>(writer.ToString()) ?? throw new FormatException("Empty configuration");Validate(settings);return settings;
 }
 public static void Validate(AppSettings s)
 {
  if(s.General==null||s.Devices==null||s.Profiles==null||s.Hotkeys==null||s.Api==null)throw new FormatException("Configuration sections cannot be null");
  if(s.Api.Port is <1 or >65535)throw new FormatException("API port must be 1..65535");
  if(s.Devices.HeadsetPatterns==null||s.Devices.SpeakersPatterns==null||s.Devices.HdmiPatterns==null||s.Devices.VirtualPatterns==null)throw new FormatException("Device patterns cannot be null");
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var p in s.Profiles)
  {
   if(p==null||string.IsNullOrWhiteSpace(p.Name)||!names.Add(p.Name))throw new FormatException("Profile names must be nonempty and unique");
   if(p.Triggers==null||p.ProcessExceptions==null||p.FallbackChain==null||string.IsNullOrWhiteSpace(p.DefaultDeviceType))throw new FormatException("Invalid profile routing");
   if(p.VolumeLimit is {} volume && (!double.IsFinite(volume)||volume<0||volume>1))throw new FormatException("Volume limit must be 0..1");
   foreach(var t in p.Triggers)
   {
    if(t==null||!Enum.IsDefined(t.Type))throw new FormatException("Invalid trigger");
    if(t.Type!=ProfileTriggerType.Always&&string.IsNullOrWhiteSpace(t.Value))throw new FormatException("Trigger value is required");
    if(t.Type==ProfileTriggerType.DockedState&&!bool.TryParse(t.Value,out _))throw new FormatException("Docked state must be true/false");
    if(t.Type==ProfileTriggerType.TimeBetween)
     foreach(var range in t.Value!.Split(','))
     {var parts=range.Split('-');if(parts.Length!=2||parts.Any(part=>!TimeSpan.TryParse(part,CultureInfo.InvariantCulture,out var time)||time<TimeSpan.Zero||time>=TimeSpan.FromDays(1)))throw new FormatException("Invalid time range");}
   }
  }
 }
}
