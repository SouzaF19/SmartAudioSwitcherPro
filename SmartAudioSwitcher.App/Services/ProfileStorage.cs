using System.IO;
namespace SmartAudioSwitcher.App.Services;

public static class ProfileStorage
{
    public static string GetPath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.EndsWith('.') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') ||
            name is "." or "..")
            throw new ArgumentException("Choose a valid profile name without path separators", nameof(name));
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
            throw new ArgumentException("Reserved Windows filename", nameof(name));
        return Path.Combine(directory, name + ".json");
    }
}
