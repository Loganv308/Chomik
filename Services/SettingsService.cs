using System.Text.Json;
using Chomik.Models;

namespace Chomik.Services;

/// <summary>
/// Loads and saves AppSettings as JSON. Replaces the original fragile
/// hand-rolled key=value text parser (which also had a duplicate-parse bug).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _settingsPath;

    public SettingsService()
    {
        _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return TryMigrateFromLegacy();

            string json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch { /* non-fatal */ }
    }

    /// <summary>
    /// One-time migration from the old settings.txt format so existing
    /// users don't lose their configuration.
    /// </summary>
    private AppSettings TryMigrateFromLegacy()
    {
        string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
        var settings = new AppSettings();

        if (!File.Exists(legacyPath)) return settings;

        try
        {
            foreach (string line in File.ReadAllLines(legacyPath))
            {
                string[] parts = line.Split('=', 2);
                if (parts.Length != 2) continue;

                string key = parts[0].Trim();
                string value = parts[1].Trim();

                switch (key)
                {
                    case "is_music_listening_enabled":
                        if (bool.TryParse(value, out bool m)) settings.MusicListeningEnabled = m;
                        break;
                    case "real_eat_files":
                        if (bool.TryParse(value, out bool r)) settings.ShredFiles = r;
                        break;
                    case "permanent_delete":
                        if (bool.TryParse(value, out bool p)) settings.PermanentDelete = p;
                        break;
                    case "music_whitelist":
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            settings.MusicWhitelist.Clear();
                            settings.MusicWhitelist.AddRange(value.Split(';'));
                        }
                        break;
                }
            }

            // Save in new format, leave old file in place as backup
            Save(settings);
        }
        catch { /* migration failure is non-fatal */ }

        return settings;
    }
}
