using Avalonia.Media.Imaging;

namespace Chomik.Helpers;

/// <summary>
/// Per-pixel alpha hit testing. Caches the alpha channel data so repeated
/// hit tests on the same bitmap (each animation frame) are fast.
/// </summary>
public sealed class PixelHitTestHelper
{
    // WeakReference values so bitmaps can be GC'd when animations change
    private readonly Dictionary<Bitmap, byte[]> _alphaCache = [];

    public bool IsOpaque(Bitmap? bitmap, int x, int y)
    {
        if (bitmap is null) return false;

        int w = bitmap.PixelSize.Width;
        int h = bitmap.PixelSize.Height;

        if (x < 0 || y < 0 || x >= w || y >= h) return false;

        byte[] alpha = GetAlphaData(bitmap, w, h);
        return alpha[y * w + x] > 10;
    }

    public byte[] GetAlphaData(Bitmap bitmap)
    {
        int w = bitmap.PixelSize.Width;
        int h = bitmap.PixelSize.Height;
        return GetAlphaData(bitmap, w, h);
    }

    public void Evict(Bitmap bitmap) => _alphaCache.Remove(bitmap);

    public void Clear() => _alphaCache.Clear();

    // -------------------------------------------------------------------------

    private byte[] GetAlphaData(Bitmap bitmap, int w, int h)
    {
        if (_alphaCache.TryGetValue(bitmap, out byte[]? cached))
            return cached;

        int pixelCount = w * h;
        byte[] alpha = new byte[pixelCount];

        unsafe
        {
            // Lock pixels: Avalonia CopyPixels gives us BGRA8888
            int stride = w * 4;
            byte[] pixels = new byte[pixelCount * 4];

            unsafe
            {
                fixed (byte* ptr = pixels)
                {
                    bitmap.CopyPixels(
                        new Avalonia.PixelRect(0, 0, w, h),
                        (nint)ptr,
                        pixels.Length,
                        stride);
                }
            }

            for (int i = 0; i < pixelCount; i++)
                alpha[i] = pixels[i * 4 + 3]; // alpha is byte 3 in BGRA
        }

        _alphaCache[bitmap] = alpha;
        return alpha;
    }
}
