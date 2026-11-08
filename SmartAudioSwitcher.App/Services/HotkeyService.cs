using SmartAudioSwitcher.Core.Interfaces;
using System.Windows.Input;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SmartAudioSwitcher.App.Services;

public class HotkeyService : IHotkeyService, IDisposable
{
    private readonly ILogger<HotkeyService> _logger;
    private readonly Dictionary<string, (HotkeyEntry Hotkey, Action Callback)> _hotkeys = new();
    private readonly IntPtr _windowHandle;
    private bool _disposed;

    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger;

        // Create a hidden window to receive hotkey messages
        var helper = new WindowHelper();
        _windowHandle = new WindowInteropHelper(helper).Handle;

        // Register for hotkey messages
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
    }

    public void RegisterHotkey(string name, string keyCombination, Action callback)
    {
        if (_hotkeys.ContainsKey(name))
        {
            UnregisterHotkey(name);
        }

        if (!TryParseHotkey(keyCombination, out var hotkey))
        {
            _logger.LogWarning("Invalid hotkey format: {KeyCombination}", keyCombination);
            return;
        }

        var id = _hotkeys.Count + 1;
        if (RegisterHotKey(_windowHandle, id, hotkey.Modifiers, hotkey.Key))
        {
            _hotkeys[name] = (new HotkeyEntry(id, hotkey.Modifiers, hotkey.Key), callback);
            _logger.LogDebug("Registered hotkey: {Name} = {KeyCombination} (ID: {Id})", name, keyCombination, id);
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("Failed to register hotkey {Name}: {Error}", name, error);
        }
    }

    public void UnregisterHotkey(string name)
    {
        if (_hotkeys.TryGetValue(name, out var entry))
        {
            UnregisterHotKey(_windowHandle, entry.Hotkey.Id);
            _hotkeys.Remove(name);
            _logger.LogDebug("Unregistered hotkey: {Name}", name);
        }
    }

    public void UnregisterAll()
    {
        foreach (var (name, entry) in _hotkeys)
        {
            UnregisterHotKey(_windowHandle, entry.Hotkey.Id);
        }
        _hotkeys.Clear();
        _logger.LogDebug("Unregistered all hotkeys");
    }

    public bool IsRegistered(string name)
    {
        return _hotkeys.ContainsKey(name);
    }

    private void OnThreadPreprocessMessage(ref System.Windows.Interop.MSG msg, ref bool handled)
    {
        if (msg.message == WM_HOTKEY)
        {
            var id = msg.wParam.ToInt32();
            var entry = _hotkeys.Values.FirstOrDefault(h => h.Hotkey.Id == id);
            
            if (entry.Callback != null)
            {
                try
                {
                    entry.Callback();
                    handled = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing hotkey callback for ID {Id}", id);
                }
            }
        }
    }

    private bool TryParseHotkey(string keyCombination, out HotkeyEntry hotkey)
    {
        hotkey = default;

        if (string.IsNullOrWhiteSpace(keyCombination))
            return false;

        var parts = keyCombination.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var modifiers = 0u;
        Key? key = null;

        foreach (var part in parts)
        {
            var trimmed = part.Trim().ToLowerInvariant();
            switch (trimmed)
            {
                case "ctrl":
                case "control":
                    modifiers |= MOD_CONTROL;
                    break;
                case "alt":
                    modifiers |= MOD_ALT;
                    break;
                case "shift":
                    modifiers |= MOD_SHIFT;
                    break;
                case "win":
                case "windows":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    if (Enum.TryParse<Key>(trimmed, true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else
                    {
                        return false;
                    }
                    break;
            }
        }

        if (key == null)
            return false;

        hotkey = new HotkeyEntry(0, modifiers, KeyInterop.VirtualKeyFromKey(key.Value));
        return true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
            UnregisterAll();
            _disposed = true;
        }
    }

    private readonly record struct HotkeyEntry(int Id, uint Modifiers, int Key);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public int time;
        public System.Drawing.Point pt;
    }

    private class WindowHelper : Window
    {
        public WindowHelper()
        {
            this.Visibility = Visibility.Hidden;
            this.ShowInTaskbar = false;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Width = 0;
            this.Height = 0;
            this.Left = -10000;
            this.Top = -10000;
            this.Show();
        }
    }
}