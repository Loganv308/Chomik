namespace Chomik.Models;

/// <summary>
/// All user-configurable settings. Loaded/saved via SettingsService.
/// </summary>
public class AppSettings
{
    public bool MusicListeningEnabled { get; set; } = true;
    public bool ShredFiles { get; set; } = false;
    public bool PermanentDelete { get; set; } = false;
    public double IdleDelaySeconds { get; set; } = 3.0;
    public int AfkTimeoutMinutes { get; set; } = 3;
    public List<string> MusicWhitelist { get; init; } = new();
}
