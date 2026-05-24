using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Chomik.Services;

/// <summary>
/// Polls the OS for active media playback.
/// On Windows uses the Windows.Media.Control SMTC API (same as the original).
/// On Linux/macOS falls back to process name heuristics.
/// </summary>
public sealed class MusicMonitorService
{
    private readonly List<string> _whitelist;

    public MusicMonitorService(List<string> whitelist)
    {
        _whitelist = whitelist;
    }

    public void UpdateWhitelist(List<string> whitelist)
    {
        _whitelist.Clear();
        _whitelist.AddRange(whitelist);
    }

    public async Task<bool> IsMusicPlayingAsync()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return await CheckWindowsSmtcAsync();

            return CheckProcessHeuristic();
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------

    private async Task<bool> CheckWindowsSmtcAsync()
    {
#if WINDOWS
        try
        {
            var sessions = await Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager
                .RequestAsync();

            var current = sessions.GetCurrentSession();
            if (current is null) return false;

            var info = await current.TryGetMediaPropertiesAsync();
            string title = info?.Title ?? "";
            string artist = info?.Artist ?? "";

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
                return false;

            var playback = current.GetPlaybackInfo();
            bool playing = playback.PlaybackStatus ==
                Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (!playing) return false;
            if (_whitelist.Count == 0) return true;

            if (_whitelist.Count == 0) return true;

            string appId = current.SourceAppUserModelId ?? "";
            return _whitelist.Any(w =>
                appId.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                title.Contains(w, StringComparison.OrdinalIgnoreCase) ||
                artist.Contains(w, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    private bool CheckProcessHeuristic()
    {
        // Heuristic for Linux/macOS: look for common music player processes
        string[] musicProcesses = [
            "spotify", "rhythmbox", "clementine", "vlc",
            "mpv", "amarok", "audacious", "lollypop"
        ];

        var running = Process.GetProcesses()
            .Select(p => { try { return p.ProcessName.ToLowerInvariant(); } catch { return ""; } });

        if (_whitelist.Count > 0)
            return running.Any(name => _whitelist.Any(w =>
                name.Contains(w, StringComparison.OrdinalIgnoreCase)));

        return running.Any(name => musicProcesses.Any(m =>
            name.Contains(m, StringComparison.OrdinalIgnoreCase)));
    }
}
