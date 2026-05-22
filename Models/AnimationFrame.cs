using Avalonia.Media.Imaging;

namespace Chomik.Models;

/// <summary>
/// A single frame in a sprite animation: the bitmap and how long to display it.
/// </summary>
public sealed class AnimationFrame
{
    public Bitmap Image { get; }
    public int DurationMs { get; }

    public AnimationFrame(Bitmap image, int durationMs)
    {
        Image = image;
        DurationMs = durationMs > 0 ? durationMs : 100;
    }
}
