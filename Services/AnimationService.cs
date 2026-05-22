using Avalonia.Media.Imaging;
using Chomik.Models;

namespace Chomik.Services;

/// <summary>
/// Responsible for loading animation frames from disk.
/// Isolated from the main window so it's independently testable.
/// </summary>
public sealed class AnimationService
{
    private readonly Dictionary<string, List<AnimationFrame>> _animations = [];

    public IReadOnlyDictionary<string, List<AnimationFrame>> Animations => _animations;

    public void Load()
    {
        _animations.Clear();

        string baseDir = GetBaseDir();
        string animsFile = FindAnimsFile(baseDir);
        string filesDir = Path.Combine(baseDir, "files");
        if (!Directory.Exists(filesDir)) filesDir = baseDir;

        if (!File.Exists(animsFile)) return;

        string? currentSection = null;
        List<AnimationFrame>? currentList = null;

        foreach (string rawLine in File.ReadAllLines(animsFile))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//") || line.StartsWith('#'))
                continue;

            if (line.StartsWith("Anim"))
            {
                FlushSection(currentSection, currentList);
                currentSection = line;
                currentList = [];
                continue;
            }

            if (currentSection is null || currentList is null) continue;

            // Skip bare integers (frame-count hints in some versions of anims.txt)
            if (int.TryParse(line, out _)) continue;

            string[] parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!parts[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(parts[1], out int duration)) continue;

            string framePath = Path.Combine(filesDir, parts[0]);
            if (!File.Exists(framePath)) continue;

            try
            {
                using var stream = new FileStream(framePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                currentList.Add(new AnimationFrame(new Bitmap(stream), duration));
            }
            catch { /* skip unreadable frames */ }
        }

        FlushSection(currentSection, currentList);
    }

    public bool Has(string name) => _animations.ContainsKey(name);

    public List<AnimationFrame>? Get(string name) =>
        _animations.TryGetValue(name, out var frames) ? frames : null;

    // -------------------------------------------------------------------------

    private void FlushSection(string? name, List<AnimationFrame>? frames)
    {
        if (name is not null && frames?.Count > 0)
            _animations[name] = frames;
    }

    private static string GetBaseDir()
    {
        string exeDir = Path.GetDirectoryName(
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
        string appDir = AppDomain.CurrentDomain.BaseDirectory;

        foreach (string dir in new[] { exeDir, appDir })
        {
            if (!string.IsNullOrEmpty(dir) &&
                (File.Exists(Path.Combine(dir, "files", "anims.txt")) ||
                 File.Exists(Path.Combine(dir, "anims.txt"))))
                return dir;
        }
        return appDir;
    }

    private static string FindAnimsFile(string baseDir)
    {
        string candidate = Path.Combine(baseDir, "files", "anims.txt");
        if (File.Exists(candidate)) return candidate;
        return Path.Combine(baseDir, "anims.txt");
    }
}
