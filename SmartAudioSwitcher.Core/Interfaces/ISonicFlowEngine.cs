using SmartAudioSwitcher.Core.Models;

namespace SmartAudioSwitcher.Core.Interfaces;

/// <summary>
/// Represents a visual routing connection from SonicFlow canvas
/// </summary>
public record VisualRoute(
    string SourceNodeId,
    string SourceTitle,
    NodeType SourceType,
    string? SourceProcessName,
    string? SourceDeviceId,
    string TargetNodeId,
    string TargetTitle,
    NodeType TargetType,
    string? TargetProcessName,
    string? TargetDeviceId
);

/// <summary>
/// Engine for translating SonicFlow visual routes to actual audio routing decisions
/// </summary>
public interface ISonicFlowEngine
{
    /// <summary>
    /// Event fired when a route is applied to the audio system
    /// </summary>
    event EventHandler<RouteAppliedEventArgs>? RouteApplied;

    /// <summary>
    /// Event fired when there's an error applying routes
    /// </summary>
    event EventHandler<RouteErrorEventArgs>? RouteError;

    /// <summary>
    /// Applies all visual routes from the canvas to the audio system
    /// </summary>
    /// <param name="routes">Visual routes from SonicFlow canvas</param>
    /// <param name="availableDevices">Currently available audio devices</param>
    /// <param name="runningProcesses">Currently running processes</param>
    /// <returns>Number of routes successfully applied</returns>
    Task<int> ApplyRoutesAsync(
        IReadOnlyList<VisualRoute> routes,
        IReadOnlyList<AudioDevice> availableDevices,
        IReadOnlyList<ProcessInfo> runningProcesses,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current active routing state from the audio system
    /// </summary>
    Task<IReadOnlyList<ActiveRoute>> GetActiveRoutesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a specific route from the audio system
    /// </summary>
    Task<bool> RemoveRouteAsync(string sourceProcessName, string targetDeviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that all routes can be resolved to actual devices/processes
    /// </summary>
    RouteValidationResult ValidateRoutes(
        IReadOnlyList<VisualRoute> routes,
        IReadOnlyList<AudioDevice> availableDevices,
        IReadOnlyList<ProcessInfo> runningProcesses);
}

/// <summary>
/// Represents an actively applied route in the audio system
/// </summary>
public record ActiveRoute(
    int ProcessId,
    string ProcessName,
    string TargetDeviceId,
    string TargetDeviceName,
    DateTime AppliedAt,
    bool IsActive
);

/// <summary>
/// Event args for route applied event
/// </summary>
public class RouteAppliedEventArgs : EventArgs
{
    public string ProcessName { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Event args for route error event
/// </summary>
public class RouteErrorEventArgs : EventArgs
{
    public string SourceName { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
    public Exception? Exception { get; init; }
}

/// <summary>
/// Result of route validation
/// </summary>
public class RouteValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<ResolvedRoute> ResolvedRoutes { get; set; } = new();
}

/// <summary>
/// A route that has been resolved to actual device/process
/// </summary>
public record ResolvedRoute(
    VisualRoute VisualRoute,
    ProcessInfo? MatchedProcess,
    AudioDevice? MatchedDevice,
    bool IsResolved
);