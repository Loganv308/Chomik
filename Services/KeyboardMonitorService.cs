using System.Runtime.InteropServices;

namespace Chomik.Services;

/// <summary>
/// Raises <see cref="KeyPressed"/> whenever the user types a printable character.
/// Abstracts over the Windows low-level keyboard hook and the Linux X11 key poll
/// so callers don't need to know the platform.
/// </summary>
public sealed class KeyboardMonitorService : IDisposable
{
    public event Action? KeyPressed;

    // Windows
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint VkCode, ScanCode, Flags, Time;
        public IntPtr DwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string? lpModuleName);

    // Linux X11
    [DllImport("libX11.so.6", EntryPoint = "XOpenDisplay")] private static extern IntPtr XOpenDisplay(string? display);
    [DllImport("libX11.so.6", EntryPoint = "XQueryKeymap")] private static extern int XQueryKeymap(IntPtr display, byte[] keysMap);

    private IntPtr _windowsHookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _windowsHookProc;
    private IntPtr _x11Display = IntPtr.Zero;
    private byte[] _x11PrevKeys = new byte[32];
    private System.Threading.Timer? _x11Timer;

    public void Start()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            StartWindows();
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            StartX11();
        // macOS: no hook without accessibility permissions; skip silently
    }

    public void Dispose()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _windowsHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_windowsHookId);
            _windowsHookId = IntPtr.Zero;
        }

        _x11Timer?.Dispose();
    }

    // -------------------------------------------------------------------------

    private void StartWindows()
    {
        _windowsHookProc = WindowsHookCallback;
        using var proc = System.Diagnostics.Process.GetCurrentProcess();
        using var module = proc.MainModule;
        if (module?.ModuleName is not null)
            _windowsHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _windowsHookProc,
                GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr WindowsHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kb = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
            // Only fire for printable key range
            if (kb.VkCode >= 0x20 && kb.VkCode <= 0xFE)
                KeyPressed?.Invoke();
        }
        return CallNextHookEx(_windowsHookId, nCode, wParam, lParam);
    }

    private void StartX11()
    {
        try
        {
            _x11Display = XOpenDisplay(null);
            if (_x11Display == IntPtr.Zero) return;

            _x11Timer = new System.Threading.Timer(_ => PollX11(), null,
                TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
        }
        catch { /* X11 not available */ }
    }

    private void PollX11()
    {
        if (_x11Display == IntPtr.Zero) return;
        try
        {
            byte[] keys = new byte[32];
            XQueryKeymap(_x11Display, keys);

            for (int i = 0; i < 32; i++)
            {
                byte diff = (byte)(keys[i] & ~_x11PrevKeys[i]);
                if (diff != 0)
                {
                    KeyPressed?.Invoke();
                    break;
                }
            }
            _x11PrevKeys = keys;
        }
        catch { }
    }
}
