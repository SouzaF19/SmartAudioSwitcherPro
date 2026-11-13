using SmartAudioSwitcher.Core.Models;

namespace SmartAudioSwitcher.Core.Services;

public static class ApiHostOptions
{
    public static (bool Enabled, string? Url, string? Error) Resolve(ApiSettings? settings)
    {
        if (settings is null) return (false, null, "API settings are missing");
        if (settings.Port is < 1 or > 65535) return (false, null, "API port must be between 1 and 65535");
        if (settings.AllowRemote) return (false, null, "Remote API access is disabled until authentication is implemented");
        return settings.Enabled ? (true, $"http://127.0.0.1:{settings.Port}", null) : (false, null, null);
    }
}

public static class DeviceCycle
{
    public static T? Next<T>(IReadOnlyList<T> connected, T? current, Func<T, string> id, string? currentId) where T : class
    {
        if (connected.Count == 0) return null;
        var index = -1;
        for (var i = 0; i < connected.Count; i++) if (id(connected[i]) == currentId) { index = i; break; }
        return connected[(index + 1) % connected.Count];
    }
}
