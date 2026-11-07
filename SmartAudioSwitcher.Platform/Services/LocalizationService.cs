using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SmartAudioSwitcher.Core.Interfaces;
using System.Linq;
using System;

namespace SmartAudioSwitcher.Platform.Services;

public class LocalizationService : ILocalizationService
{
    private readonly string _localesPath;
    private Dictionary<string, string> _currentTranslations = new();
    private string _currentLanguage = "en-US";
    private const string DefaultLanguage = "en-US";

    public event EventHandler LanguageChanged;

    public string CurrentLanguage => _currentLanguage;

    public LocalizationService()
    {
        _localesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Locales");
        // Initial language will be set via SetLanguage called from Program.cs/AppSettings
    }

    public string GetString(string key)
    {
        if (_currentTranslations.TryGetValue(key, out var value))
        {
            return value;
        }
        return key; // Fallback to key if not found
    }

    public void SetLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            languageCode = DefaultLanguage;
        }

        string filePath = Path.Combine(_localesPath, $"{languageCode}.json");
        
        if (!File.Exists(filePath))
        {
            // Fallback to default language
            filePath = Path.Combine(_localesPath, $"{DefaultLanguage}.json");
        }

        try
        {
            string json = File.ReadAllText(filePath);
            _currentTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            _currentLanguage = languageCode;
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Critical fallback: load default or empty
            LoadDefaultFallback();
        }
    }

    private void LoadDefaultFallback()
    {
        string defaultPath = Path.Combine(_localesPath, $"{DefaultLanguage}.json");
        if (File.Exists(defaultPath))
        {
            try
            {
                string json = File.ReadAllText(defaultPath);
                _currentTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            catch { _currentTranslations = new(); }
        }
        _currentLanguage = DefaultLanguage;
    }
}
