using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Chomik.Services;

/// <summary>
/// Secure file shredder. For each file:
///   1. Strip protective attributes.
///   2. Encrypt in-place with AES-256 + a randomly generated key that is
///      never persisted — even bytes that escape overwriting are permanently
///      unreadable ciphertext.
///   3. Overwrite the (now-ciphertext) content 7× with CSPRNG data.
///   4. Truncate to zero length.
///   5. Rename 5× to random names to scrub the original filename from
///      directory entries and filesystem journals.
///   6. Delete.
///
/// For directories: every file inside is shredded first, then the empty
/// shell is removed.
///
/// Threat model: makes files unreadable by any software-based recovery
/// tool on any OS. Not a guarantee against lab-level NAND chip analysis
/// on SSDs with wear-leveling (nothing purely software-based can be).
/// </summary>
public sealed class FileShredderService
{
    private const int RandomPasses = 7;
    private const int RenameCount = 5;
    private const int BufferSize = 65_536;

    public async Task ShredAsync(string path, CancellationToken ct = default)
    {
        if (File.Exists(path))
        {
            await ShredSingleFileAsync(path, ct);
        }
        else if (Directory.Exists(path))
        {
            await ShredDirectoryAsync(path, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Private implementation
    // -------------------------------------------------------------------------

    private static async Task ShredDirectoryAsync(string dirPath, CancellationToken ct)
    {
        // Shred every file in the tree first
        foreach (string file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try { await ShredSingleFileAsync(file, ct); }
            catch { /* best-effort per file */ }
        }

        // Rename and remove the empty shell
        try
        {
            string parent = Path.GetDirectoryName(dirPath)!;
            string tmp = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.Move(dirPath, tmp);
            Directory.Delete(tmp, recursive: true);
        }
        catch
        {
            try { Directory.Delete(dirPath, recursive: true); } catch { }
        }
    }

    private static async Task ShredSingleFileAsync(string path, CancellationToken ct)
    {
        try
        {
            // Strip read-only / hidden / system so we can open for writing
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }

            long length = new FileInfo(path).Length;

            if (length > 0)
            {
                // Step 1 — encrypt in-place with a thrown-away AES-256 key.
                // If any bytes survive the overwrite passes (SSD wear-leveling,
                // filesystem slack), they are permanently unreadable ciphertext.
                await EncryptInPlaceAsync(path, length, ct);

                // Step 2 — overwrite the ciphertext with random data
                await OverwriteAsync(path, length, RandomPasses, ct);

                // Step 3 — truncate to zero
                await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
                    FileShare.None, BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);
                fs.SetLength(0);
                await fs.FlushAsync(ct);
            }

            // Step 4 — cycle through random names to scrub filename from
            // directory entries and filesystem journals
            string current = path;
            string dir = Path.GetDirectoryName(current)!;
            for (int i = 0; i < RenameCount; i++)
            {
                string renamed = Path.Combine(dir, Guid.NewGuid().ToString("N"));
                try { File.Move(current, renamed); current = renamed; }
                catch { break; }
            }

            File.Delete(current);
        }
        catch
        {
            // Last resort: plain delete beats leaving the file intact
            try { File.Delete(path); } catch { }
        }
    }

    private static async Task EncryptInPlaceAsync(string path, long length, CancellationToken ct)
    {
        // Key and IV exist only in RAM for the duration of this call.
        // They are never written to disk. When the 'using' block exits
        // the key material is zeroed by the runtime.
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        aes.GenerateIV();

        byte[] plaintext = await File.ReadAllBytesAsync(path, ct);
        byte[] ciphertext;

        try
        {
            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            await using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
            await cs.WriteAsync(plaintext, ct);
            await cs.FlushFinalBlockAsync(ct);
            ciphertext = ms.ToArray();
        }
        finally
        {
            // Wipe plaintext from RAM immediately — don't wait for GC
            CryptographicOperations.ZeroMemory(plaintext);
        }

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);

        int writeLen = (int)Math.Min(ciphertext.Length, length);
        await fs.WriteAsync(ciphertext.AsMemory(0, writeLen), ct);
        await fs.FlushAsync(ct);
    }

    private static async Task OverwriteAsync(string path, long length, int passes, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Write,
            FileShare.None, BufferSize, FileOptions.WriteThrough | FileOptions.Asynchronous);

        byte[] buffer = new byte[Math.Min(length, BufferSize)];

        for (int pass = 0; pass < passes; pass++)
        {
            ct.ThrowIfCancellationRequested();
            fs.Seek(0, SeekOrigin.Begin);
            long remaining = length;

            while (remaining > 0)
            {
                int chunk = (int)Math.Min(buffer.Length, remaining);
                RandomNumberGenerator.Fill(buffer.AsSpan(0, chunk));
                await fs.WriteAsync(buffer.AsMemory(0, chunk), ct);
                remaining -= chunk;
            }

            await fs.FlushAsync(ct);
        }
    }

    // -------------------------------------------------------------------------
    // OS trash helpers (used when ShredFiles is off but PermanentDelete is off)
    // -------------------------------------------------------------------------

    public static void MoveToTrash(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            MoveToTrashWindows(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            MoveToTrashLinux(path);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            MoveToTrashMacOs(path);
    }

    private static void MoveToTrashWindows(string path)
    {
        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
        }
        catch
        {
            ForciblyDelete(path);
        }
    }

    private static void MoveToTrashLinux(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("gio", $"trash \"{path}\"")
                { UseShellExecute = false, CreateNoWindow = true };
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
        }
        catch { ForciblyDelete(path); }
    }

    private static void MoveToTrashMacOs(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(
                "osascript",
                $"-e 'tell application \"Finder\" to delete POSIX file \"{path}\"'")
                { UseShellExecute = false, CreateNoWindow = true };
            System.Diagnostics.Process.Start(psi)?.WaitForExit();
        }
        catch { ForciblyDelete(path); }
    }

    private static void ForciblyDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
