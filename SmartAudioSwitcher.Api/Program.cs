using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartAudioSwitcher.Core.Interfaces;
using SmartAudioSwitcher.Core.Models;
using SmartAudioSwitcher.Core.Services;
using SmartAudioSwitcher.Platform.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Add services
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IProfileEngine, ProfileEngine>();
builder.Services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
builder.Services.AddSingleton<IAudioSessionService, AudioSessionService>();
builder.Services.AddSingleton<IProcessMonitorService, ProcessMonitorService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Initialize services
var configService = app.Services.GetRequiredService<IConfigurationService>();
var settings = configService.LoadSettings();

var profileEngine = app.Services.GetRequiredService<IProfileEngine>();
profileEngine.ReloadConfiguration(settings);

var deviceService = app.Services.GetRequiredService<IAudioDeviceService>();
deviceService.StartMonitoring();

var processMonitor = app.Services.GetRequiredService<IProcessMonitorService>();
processMonitor.StartMonitoring();

var sessionService = app.Services.GetRequiredService<IAudioSessionService>();
sessionService.StartMonitoring();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// API Routes
var api = app.MapGroup("/api/v1").WithTags("SmartAudioSwitcher");

// Device endpoints
api.MapGet("/devices", () => 
{
    var devices = deviceService.GetOutputDevices();
    return Results.Ok(devices.Select(d => new
    {
        d.Id,
        d.FriendlyName,
        d.Type,
        d.Role,
        d.IsConnected,
        d.IsDefault,
        d.IsCommunicationsDefault
    }));
}).WithName("GetDevices").WithOpenApi();

api.MapGet("/devices/default", () => 
{
    var device = deviceService.GetDefaultDevice();
    if (device == null) return Results.NotFound("No default device found");
    
    return Results.Ok(new
    {
        device.Id,
        device.FriendlyName,
        device.Type,
        device.Role,
        device.IsConnected,
        device.IsDefault,
        device.IsCommunicationsDefault
    });
}).WithName("GetDefaultDevice").WithOpenApi();

api.MapGet("/devices/{deviceId}", (string deviceId) => 
{
    var device = deviceService.GetDeviceById(deviceId);
    if (device == null) return Results.NotFound($"Device not found: {deviceId}");
    
    return Results.Ok(new
    {
        device.Id,
        device.FriendlyName,
        device.Type,
        device.Role,
        device.IsConnected,
        device.IsDefault,
        device.IsCommunicationsDefault
    });
}).WithName("GetDevice").WithOpenApi();

api.MapPost("/devices/{deviceId}/set-default", (string deviceId, bool? communications = null) => 
{
    var device = deviceService.GetDeviceById(deviceId);
    if (device == null) return Results.NotFound($"Device not found: {deviceId}");

    var role = communications == true ? DeviceRole.Communications : 
               communications == false ? DeviceRole.Default : DeviceRole.Both;

    var success = deviceService.SetDefaultDevice(deviceId, role);
    
    if (success)
    {
        return Results.Ok(new { message = $"Default device set to {device.FriendlyName}", deviceId });
    }
    
    return Results.BadRequest("Failed to set default device");
}).WithName("SetDefaultDevice").WithOpenApi();

api.MapPost("/devices/cycle", () => 
{
    var devices = deviceService.GetOutputDevices().Where(d => d.IsConnected).ToList();
    var current = deviceService.GetDefaultDevice();
    var currentIndex = devices.FindIndex(d => d.Id == current?.Id);
    var nextIndex = (currentIndex + 1) % devices.Count;
    var next = devices[nextIndex];
    
    var success = deviceService.SetDefaultDevice(next.Id);
    
    if (success)
    {
        return Results.Ok(new { message = $"Cycled to {next.FriendlyName}", deviceId = next.Id });
    }
    
    return Results.BadRequest("Failed to cycle device");
}).WithName("CycleDevice").WithOpenApi();

api.MapPost("/devices/force-headset", () => 
{
    var headset = deviceService.GetOutputDevices()
        .FirstOrDefault(d => d.Type == DeviceType.Headset && d.IsConnected);

    if (headset == null) return Results.NotFound("No connected headset found");

    var success = deviceService.SetDefaultDevice(headset.Id);
    
    if (success)
    {
        return Results.Ok(new { message = $"Switched to headset: {headset.FriendlyName}", deviceId = headset.Id });
    }
    
    return Results.BadRequest("Failed to switch to headset");
}).WithName("ForceHeadset").WithOpenApi();

// Profile endpoints
api.MapGet("/profiles", () => 
{
    var profiles = settings.Profiles.Where(p => p.Enabled).Select(p => new
    {
        p.Name,
        p.DefaultDeviceType,
        TriggerCount = p.Triggers.Count,
        ExceptionCount = p.ProcessExceptions.Count,
        p.FallbackChain,
        p.VolumeLimit,
        p.Enabled
    });
    
    return Results.Ok(profiles);
}).WithName("GetProfiles").WithOpenApi();

api.MapGet("/profiles/active", () => 
{
    var devices = deviceService.GetOutputDevices();
    var processes = processMonitor.GetRunningProcesses();
    
    var profile = profileEngine.EvaluateProfile(devices, processes);
    if (profile == null) return Results.NotFound("No profile matched");

    var decision = profileEngine.DecideRouting(profile, deviceService.GetDefaultDevice(), 
        processMonitor.GetForegroundProcess(), devices);

    return Results.Ok(new
    {
        profile.Name,
        decision.DeviceId,
        decision.DeviceName,
        decision.Reason,
        decision.ProfileName,
        decision.IsFallback
    });
}).WithName("GetActiveProfile").WithOpenApi();

// Routing endpoints
api.MapGet("/routing", () => 
{
    var devices = deviceService.GetOutputDevices();
    var processes = processMonitor.GetRunningProcesses();
    var sessions = sessionService.GetActiveSessions();
    
    var profile = profileEngine.EvaluateProfile(devices, processes);
    if (profile == null) return Results.NotFound("No profile matched");

    var decision = profileEngine.DecideRouting(profile, deviceService.GetDefaultDevice(), 
        processMonitor.GetForegroundProcess(), devices);

    return Results.Ok(new
    {
        profile = profile.Name,
        defaultDevice = new { decision.DeviceId, decision.DeviceName },
        reasoning = decision.Reason,
        isFallback = decision.IsFallback,
        activeSessions = sessions.Select(s => new
        {
            s.ProcessId,
            s.ProcessName,
            s.DisplayName,
            s.Volume,
            s.IsMuted,
            s.CurrentDeviceId
        })
    });
}).WithName("GetRouting").WithOpenApi();

// Status endpoint
api.MapGet("/status", () => 
{
    var defaultDevice = deviceService.GetDefaultDevice();
    var devices = deviceService.GetOutputDevices();
    var processes = processMonitor.GetRunningProcesses();
    
    var profile = profileEngine.EvaluateProfile(devices, processes);
    
    return Results.Ok(new
    {
        running = true,
        version = "1.0.0",
        defaultDevice = defaultDevice != null ? new { defaultDevice.Id, defaultDevice.FriendlyName } : null,
        connectedDevices = devices.Count(d => d.IsConnected),
        totalDevices = devices.Count,
        runningProcesses = processes.Count,
        activeProfile = profile?.Name,
        timestamp = DateTime.UtcNow
    });
}).WithName("GetStatus").WithOpenApi();

// Config endpoint
api.MapGet("/config", () => 
{
    return Results.Ok(settings);
}).WithName("GetConfig").WithOpenApi();

// Health check
api.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithName("HealthCheck").WithOpenApi();

app.Run();