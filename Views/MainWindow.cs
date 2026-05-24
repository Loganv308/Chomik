using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform;
using Chomik.Helpers;
using Chomik.Models;
using Chomik.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Chomik.Views;

public class MainWindow : Window
{
    // -------------------------------------------------------------------------
    // Services
    // -------------------------------------------------------------------------
    private readonly SettingsService _settingsService = new();
    private readonly AnimationService _animationService = new();
    private readonly FileShredderService _shredderService = new();
    private readonly KeyboardMonitorService _keyboardMonitor = new();
    private readonly PixelHitTestHelper _hitTest = new();
    private MusicMonitorService _musicMonitor = new([]);

    private AppSettings _settings = new();

    private const int WS_EX_LAYERED = 0x00080000;

    // -------------------------------------------------------------------------
    // Animation state machine
    // -------------------------------------------------------------------------
    private HamsterState _state = HamsterState.Idle;
    private List<AnimationFrame> _currentFrames = [];
    private int _frameIndex = 0;
    private string _currentAnimName = AnimNames.MainIdle;

    // Idle sub-state
    private int _idleLoopCounter = 0;
    private int _maxIdleLoops = 1;
    private bool _isRandomIdleSequence = false;
    private string _idleStart = "";
    private string _idleLoop = "";
    private string _idleFinish = "";

    // Write-mode
    private string _bubbleText = "";
    private BubbleWindow? _bubbleWindow;
    private DispatcherTimer? _bubbleFollowTimer;

    // -------------------------------------------------------------------------
    // Timers
    // -------------------------------------------------------------------------
    private DispatcherTimer? _animTimer;
    private DispatcherTimer? _musicCheckTimer;
    private DispatcherTimer? _typingCheckTimer;
    private DispatcherTimer? _idleDelayTimer;
    private DispatcherTimer? _afkCheckTimer;

    // -------------------------------------------------------------------------
    // Typing detection
    // -------------------------------------------------------------------------
    private DateTime _lastKeyPressTime = DateTime.MinValue;
    private DateTime _typingSessionStart = DateTime.MinValue;
    private const int TypingThresholdMs = 2000;
    private bool _typingAnimActive = false;

    // -------------------------------------------------------------------------
    // User activity / AFK
    // -------------------------------------------------------------------------
    private DateTime _lastActivityTime = DateTime.Now;

    // -------------------------------------------------------------------------
    // Drag state
    // -------------------------------------------------------------------------
    private Point _mouseOffset;
    private bool _mouseDown = false;

    // -------------------------------------------------------------------------
    // Win32 window customisation
    // -------------------------------------------------------------------------
    private const int GWLP_WNDPROC = -4;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int n);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int n, IntPtr val);
    [DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr prev, IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref Win32Point pt);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(IntPtr dest, IntPtr src1, IntPtr src2, int mode);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hrgn, bool redraw);

    [StructLayout(LayoutKind.Sequential)] private struct Win32Point { public int X, Y; }

    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    private WndProcDelegate? _customWndProc;
    private IntPtr _oldWndProc = IntPtr.Zero;

    // -------------------------------------------------------------------------
    // Controls
    // -------------------------------------------------------------------------
    private Image _hamsterImg = null!;

    // -------------------------------------------------------------------------
    // One-off random idles and uninterruptible set
    // -------------------------------------------------------------------------
    private static readonly string[] OneOffIdleAnims =
        [AnimNames.Idle1, AnimNames.Idle3, AnimNames.Idle4, AnimNames.Idle5, AnimNames.Idle6];

    private HashSet<string> _uninterruptible = [];

    // =========================================================================
    // Construction
    // =========================================================================

    public MainWindow()
    {
        Background = Avalonia.Media.Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        SystemDecorations = SystemDecorations.None;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = false;

        _hamsterImg = new Image
            {
            Stretch = Avalonia.Media.Stretch.None
        };

        Content = new Grid
        {
            Children =
        {
            _hamsterImg
        }
            };

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(ContextRequestedEvent, OnContextRequested, handledEventsToo: false);

        _settings = _settingsService.Load();
        _musicMonitor = new MusicMonitorService(_settings.MusicWhitelist);

        BuildUninterruptibleSet();
        _animationService.Load();
        LoadInitialAnimation();
        LoadMenuIcons();
        SetupTimers();
        SetupKeyboardMonitor();
    }

    // =========================================================================
    // Startup helpers
    // =========================================================================

    private void SetupTimers()
    {
        _animTimer = new DispatcherTimer();
        _animTimer.Tick += OnAnimTick;

        if (_currentFrames.Count > 0)
        {
            _hamsterImg.Source = _currentFrames[0].Image;
            UpdateWindowRegion(_currentFrames[0].Image);
            _animTimer.Interval = TimeSpan.FromMilliseconds(_currentFrames[0].DurationMs);
            _animTimer.Start();
        }

        _musicCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _musicCheckTimer.Tick += OnMusicCheckTick;
        if (_settings.MusicListeningEnabled)
        {
            _musicCheckTimer.Start();
            _ = CheckMusicAsync();
        }

        _typingCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _typingCheckTimer.Tick += OnTypingCheckTick;
        _typingCheckTimer.Start();

        _idleDelayTimer = new DispatcherTimer();
        _idleDelayTimer.Tick += OnIdleDelayTick;

        _afkCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _afkCheckTimer.Tick += OnAfkCheckTick;
        _afkCheckTimer.Start();
    }

    private void SetupKeyboardMonitor()
    {
        _keyboardMonitor.KeyPressed += () => Dispatcher.UIThread.Post(() =>
        {
            _lastKeyPressTime = DateTime.Now;
            UpdateUserActivity();
        });
        _keyboardMonitor.Start();
    }

    private void LoadInitialAnimation()
    {
        string[] candidates = [AnimNames.MainIdle, AnimNames.IdleLoop1, AnimNames.Idle1];
        foreach (string name in candidates)
        {
            if (TryLoadAnimation(name)) return;
        }
    }

    private void LoadMenuIcons()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var map = new Dictionary<string, string>
        {
            { "IconExit",       "icon1.ico" },
            { "IconDonate",     "icon_2.ico" },
            { "IconSettings",   "icon3.ico" },
            { "IconScreenshot", "icon4.ico" },
        };

        foreach (var (controlName, fileName) in map)
        {
            try
            {
                string path = Path.Combine(baseDir, "files", fileName);
                if (!File.Exists(path)) path = Path.Combine(baseDir, fileName);
                if (!File.Exists(path)) continue;

                var img = this.FindControl<Image>(controlName);
                if (img is null) continue;

                using var stream = File.OpenRead(path);
                img.Source = new Bitmap(stream);
            }
            catch { }
        }
    }

    private void BuildUninterruptibleSet()
    {
        _uninterruptible =
        [
            AnimNames.IdleStart1, AnimNames.IdleStart2,
            AnimNames.IdleFinish1, AnimNames.IdleFinish2,
            AnimNames.TypeStart, AnimNames.TypeStop,
            AnimNames.MusicStart, AnimNames.MusicFinish,
            AnimNames.DragFileStart, AnimNames.DragFileFinish,
            AnimNames.MoveStart, AnimNames.MoveFinish,
            AnimNames.AfkStart, AnimNames.AfkFinish,
            AnimNames.Screenshot,
            ..OneOffIdleAnims,
        ];
    }

    // =========================================================================
    // Window opened — Win32 customisation
    // =========================================================================

    protected override void OnOpened(EventArgs e)
    {

        base.OnOpened(e);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        var hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero) return;

        // Remove from taskbar, keep on top
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_LAYERED) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));

        // Subclass for click-through on transparent pixels
        _customWndProc = CustomWndProc;
        _oldWndProc = SetWindowLongPtr(hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_customWndProc));

        // ADD THIS: apply the region for the first frame now that the handle exists
        if (CurrentFrame is not null)
            UpdateWindowRegion(CurrentFrame.Image);
    }

    private IntPtr CustomWndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (msg == WM_NCHITTEST)
        {
            try
            {
                int sx = (short)(l.ToInt64() & 0xFFFF);
                int sy = (short)((l.ToInt64() >> 16) & 0xFFFF);
                var pt = new Win32Point { X = sx, Y = sy };
                ScreenToClient(hwnd, ref pt);

                var frame = CurrentFrame;
                if (frame is null || !_hitTest.IsOpaque(frame.Image, pt.X, pt.Y))
                    return new IntPtr(HTTRANSPARENT);
            }
            catch { return new IntPtr(HTTRANSPARENT); }
        }
        return CallWindowProc(_oldWndProc, hwnd, msg, w, l);
    }

    // =========================================================================
    // Animation engine
    // =========================================================================

    private AnimationFrame? CurrentFrame =>
        _currentFrames.Count > 0
            ? _currentFrames[Math.Min(_frameIndex, _currentFrames.Count - 1)]
            : null;

    private bool TryLoadAnimation(string name)
    {
        var frames = _animationService.Get(name);
        if (frames is null || frames.Count == 0) return false;

        _currentFrames = frames;
        _frameIndex = 0;
        _currentAnimName = name;

        if (_animTimer is not null)
        {
            _animTimer.Interval = TimeSpan.FromMilliseconds(_currentFrames[0].DurationMs);
            if (!_animTimer.IsEnabled) _animTimer.Start();
        }

        return true;
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (_currentFrames.Count == 0) return;

        _frameIndex++;
        bool finished = _frameIndex >= _currentFrames.Count;

        if (finished)
        {
            _frameIndex = _currentFrames.Count - 1; // hold last frame
            HandleAnimationFinished();
            return;
        }

        var frame = _currentFrames[_frameIndex];
        _hamsterImg.Source = frame.Image;
        UpdateWindowRegion(frame.Image);
        _animTimer!.Interval = TimeSpan.FromMilliseconds(frame.DurationMs);
    }

    private void HandleAnimationFinished()
    {
        _animTimer?.Stop();

        switch (_currentAnimName)
        {
            // ---- Idle start → loop
            case AnimNames.IdleStart1 when _animationService.Has(AnimNames.IdleLoop1):
                _isRandomIdleSequence = false;
                _idleLoop = AnimNames.IdleLoop1;
                _idleFinish = AnimNames.IdleFinish1;
                _idleLoopCounter = 0;
                _maxIdleLoops = new Random().Next(2, 5);
                TryLoadAnimation(AnimNames.IdleLoop1);
                return;

            case AnimNames.IdleStart2 when _animationService.Has(AnimNames.IdleLoop2):
                _isRandomIdleSequence = false;
                _idleLoop = AnimNames.IdleLoop2;
                _idleFinish = AnimNames.IdleFinish2;
                _idleLoopCounter = 0;
                _maxIdleLoops = new Random().Next(2, 5);
                TryLoadAnimation(AnimNames.IdleLoop2);
                return;

            // ---- Idle loops → finish or repeat
            case var n when n == _idleLoop && !_isRandomIdleSequence:
                _idleLoopCounter++;
                if (_idleLoopCounter < _maxIdleLoops)
                {
                    TryLoadAnimation(_idleLoop);
                    return;
                }
                if (!string.IsNullOrEmpty(_idleFinish) && TryLoadAnimation(_idleFinish)) return;
                goto case AnimNames.MainIdle; // fall through

            // ---- Idle finish → main idle
            case var n when n == _idleFinish:
            case AnimNames.MainIdle:
                StartIdleDelayTimer();
                TryLoadAnimation(AnimNames.MainIdle);
                _state = HamsterState.Idle;
                return;

            // ---- One-off random idle anims
            case var n when OneOffIdleAnims.Contains(n):
                StartIdleDelayTimer();
                TryLoadAnimation(AnimNames.MainIdle);
                _state = HamsterState.Idle;
                return;

            // ---- Typing: start → loop
            case AnimNames.TypeStart:
                TryLoadAnimation(AnimNames.Typing);
                return;

            // ---- Typing: stop → idle
            case AnimNames.TypeStop:
                _typingAnimActive = false;
                GoIdle();
                return;

            // ---- Music: start → loop
            case AnimNames.MusicStart:
                TryLoadAnimation(AnimNames.MusicLoop);
                return;

            // ---- Music: finish → idle
            case AnimNames.MusicFinish:
                _state = HamsterState.Idle;
                GoIdle();
                return;

            // ---- Drag character: start → moving
            case AnimNames.MoveStart:
                TryLoadAnimation(AnimNames.Moving);
                return;

            // ---- Drag character: finish → idle
            case AnimNames.MoveFinish:
                _state = HamsterState.Idle;
                GoIdle();
                return;

            // ---- File drag: start → processing
            case AnimNames.DragFileStart:
                TryLoadAnimation(AnimNames.DragFileProcessing);
                return;

            // ---- File drag: finish → idle
            case AnimNames.DragFileFinish:
                _state = HamsterState.Idle;
                _idleDelayTimer?.Stop();
                GoIdle();
                return;

            // ---- AFK: start → loop
            case AnimNames.AfkStart:
                TryLoadAnimation(AnimNames.AfkLoop);
                return;

            // ---- AFK: finish → idle
            case AnimNames.AfkFinish:
                _state = HamsterState.Idle;
                GoIdle();
                return;

            // ---- Screenshot finish → idle
            case AnimNames.Screenshot:
                _ = TakeScreenshotAsync();
                _state = HamsterState.Idle;
                GoIdle();
                return;
        }
    }

    private void GoIdle()
    {
        _state = HamsterState.Idle;
        TryLoadAnimation(AnimNames.MainIdle);
        StartIdleDelayTimer();
    }

    private void StartIdleDelayTimer()
    {
        _idleDelayTimer?.Stop();
        _idleDelayTimer!.Interval = TimeSpan.FromSeconds(_settings.IdleDelaySeconds);
        _idleDelayTimer.Start();
    }

    private void OnIdleDelayTick(object? sender, EventArgs e)
    {
        _idleDelayTimer?.Stop();
        if (_state != HamsterState.Idle) return;

        TryPlayRandomIdle();
    }

    private void TryPlayRandomIdle()
    {
        var rnd = new Random();

        // Pick a random named idle sequence (start/loop/finish) or one-off
        string[] starts = [AnimNames.IdleStart1, AnimNames.IdleStart2];
        var available = starts.Where(_animationService.Has).ToArray();
        var oneOffs = OneOffIdleAnims.Where(_animationService.Has).ToArray();

        if (available.Length == 0 && oneOffs.Length == 0) return;

        bool pickOneOff = oneOffs.Length > 0 && (available.Length == 0 || rnd.Next(2) == 0);

        if (pickOneOff)
        {
            string pick = oneOffs[rnd.Next(oneOffs.Length)];
            _animTimer?.Stop();
            _isRandomIdleSequence = true;
            TryLoadAnimation(pick);
        }
        else
        {
            string pick = available[rnd.Next(available.Length)];
            _animTimer?.Stop();
            _isRandomIdleSequence = false;
            TryLoadAnimation(pick);
        }
    }

    // =========================================================================
    // Music monitor
    // =========================================================================

    private async void OnMusicCheckTick(object? sender, EventArgs e) => await CheckMusicAsync();

    private async Task CheckMusicAsync()
    {
        bool playing = await _musicMonitor.IsMusicPlayingAsync();
        bool wasPlaying = _state == HamsterState.Music;

        if (playing == wasPlaying) return;

        if (playing && CanTransitionTo(HamsterState.Music))
        {
            _state = HamsterState.Music;
            _idleDelayTimer?.Stop();
            _animTimer?.Stop();
            TryLoadAnimation(AnimNames.MusicStart);
        }
        else if (!playing && wasPlaying)
        {
            _state = HamsterState.Idle;
            _animTimer?.Stop();
            if (_animationService.Has(AnimNames.MusicFinish))
                TryLoadAnimation(AnimNames.MusicFinish);
            else
                GoIdle();
        }
    }

    // =========================================================================
    // Typing detection
    // =========================================================================

    private void OnTypingCheckTick(object? sender, EventArgs e)
    {
        if (!CanTransitionTo(HamsterState.Typing)) return;

        bool typing = (DateTime.Now - _lastKeyPressTime).TotalMilliseconds < TypingThresholdMs;

        if (typing)
        {
            if (!_typingAnimActive)
            {
                if (_typingSessionStart == DateTime.MinValue)
                    _typingSessionStart = DateTime.Now;

                if ((DateTime.Now - _typingSessionStart).TotalMilliseconds >= TypingThresholdMs)
                {
                    _state = HamsterState.Typing;
                    _idleDelayTimer?.Stop();
                    _animTimer?.Stop();
                    _typingAnimActive = true;

                    if (!TryLoadAnimation(AnimNames.TypeStart))
                        TryLoadAnimation(AnimNames.Typing);
                }
            }
        }
        else
        {
            _typingSessionStart = DateTime.MinValue;

            if (_typingAnimActive && _currentAnimName != AnimNames.TypeStop)
            {
                _animTimer?.Stop();
                if (!TryLoadAnimation(AnimNames.TypeStop))
                {
                    _typingAnimActive = false;
                    _state = HamsterState.Idle;
                    GoIdle();
                }
            }
        }
    }

    // =========================================================================
    // AFK detection
    // =========================================================================

    private void OnAfkCheckTick(object? sender, EventArgs e)
    {
        if (_state == HamsterState.Afk) return;
        if (!CanTransitionTo(HamsterState.Afk)) return;

        if ((DateTime.Now - _lastActivityTime).TotalMinutes >= _settings.AfkTimeoutMinutes)
            StartAfk();
    }

    private void StartAfk()
    {
        if (!_animationService.Has(AnimNames.AfkStart)) return;
        _state = HamsterState.Afk;
        _idleDelayTimer?.Stop();
        _animTimer?.Stop();
        TryLoadAnimation(AnimNames.AfkStart);
    }

    private void EndAfk()
    {
        if (_state != HamsterState.Afk) return;
        _state = HamsterState.Idle;

        _animTimer?.Stop();
        if (_currentAnimName is AnimNames.AfkStart or AnimNames.AfkLoop
            && _animationService.Has(AnimNames.AfkFinish))
            TryLoadAnimation(AnimNames.AfkFinish);
        else
            GoIdle();
    }

    private void UpdateUserActivity()
    {
        _lastActivityTime = DateTime.Now;
        if (_state == HamsterState.Afk) EndAfk();
    }

    // =========================================================================
    // State guard
    // =========================================================================

    private bool CanTransitionTo(HamsterState target) => _state switch
    {
        HamsterState.Afk        => target == HamsterState.Idle || target == HamsterState.Afk,
        HamsterState.Writing    => false,
        HamsterState.Screenshot => false,
        HamsterState.FileDrag   => false,
        HamsterState.Dragging   => target == HamsterState.Idle,
        _ when _uninterruptible.Contains(_currentAnimName) => false,
        _ => true,
    };

    // =========================================================================
    // Pointer / drag events
    // =========================================================================

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (!_hitTest.IsOpaque(CurrentFrame?.Image, (int)pos.X, (int)pos.Y))
        { e.Handled = true; return; }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var screen = this.PointToScreen(pos);
            _mouseOffset = new Point(Position.X - screen.X, Position.Y - screen.Y);
            _mouseDown = true;
            UpdateUserActivity();
            _idleDelayTimer?.Stop();

            if (CanTransitionTo(HamsterState.Dragging))
            {
                _state = HamsterState.Dragging;
                _animTimer?.Stop();
                if (!TryLoadAnimation(AnimNames.MoveStart))
                    TryLoadAnimation(AnimNames.Moving);
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_mouseDown) return;

        ContextMenu?.Close();
        var screen = this.PointToScreen(e.GetPosition(this));
        Position = new PixelPoint(
            (int)(screen.X + _mouseOffset.X),
            (int)(screen.Y + _mouseOffset.Y));

        if (_state == HamsterState.Dragging && _currentAnimName != AnimNames.Moving)
        {
            _animTimer?.Stop();
            TryLoadAnimation(AnimNames.Moving);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        _mouseDown = false;
        UpdateUserActivity();

        if (_state == HamsterState.Dragging)
        {
            _animTimer?.Stop();
            if (!TryLoadAnimation(AnimNames.MoveFinish))
                GoIdle();
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.TryGetPosition(this, out var pos))
        {
            if (!_hitTest.IsOpaque(CurrentFrame?.Image, (int)pos.X, (int)pos.Y))
                e.Handled = true;
        }
    }

    // =========================================================================
    // File drag and drop + shredder
    // =========================================================================

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (_state is HamsterState.Afk or HamsterState.Screenshot or HamsterState.Writing)
        { e.DragEffects = DragDropEffects.None; return; }

        e.DragEffects = DragDropEffects.Copy;
        if (_state == HamsterState.FileDrag) return;

        _state = HamsterState.FileDrag;
        _mouseDown = false;
        _idleDelayTimer?.Stop();
        _animTimer?.Stop();

        if (!TryLoadAnimation(AnimNames.DragFileStart))
            TryLoadAnimation(AnimNames.DragFileProcessing);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_state != HamsterState.FileDrag) return;

        var files = e.Data.GetFiles()?.ToList();
        if (files?.Count > 0)
            await ProcessDroppedFilesAsync(files.Select(f => f.Path.LocalPath));

        _animTimer?.Stop();
        if (!TryLoadAnimation(AnimNames.DragFileFinish))
            GoIdle();
    }

    private async Task ProcessDroppedFilesAsync(IEnumerable<string> paths)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        foreach (string path in paths)
        {
            try
            {
                if (_settings.ShredFiles)
                {
                    // Secure shred — encrypt + multi-pass overwrite + rename + delete
                    await _shredderService.ShredAsync(path, cts.Token);
                }
                else if (_settings.PermanentDelete)
                {
                    // Plain permanent delete (no overwrite)
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                }
                else
                {
                    // Move to OS trash
                    FileShredderService.MoveToTrash(path);
                }
            }
            catch { /* per-file failure is non-fatal */ }
        }
    }

    // =========================================================================
    // Menu actions
    // =========================================================================

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Environment.Exit(0);

    private void OnDonateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo
            { FileName = "https://donatepay.ru/don/1493944", UseShellExecute = true });

    private async void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var frames = _animationService.Get(AnimNames.MusicLoop);
        var box = new MessageBox("created with love ❤\nauthor: blaing", frames);
        await box.ShowDialog(this);
    }

    private async void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var dlg = new SettingsDialog(_settings);
            bool ok = await dlg.ShowDialog<bool>(this);
            if (!ok) return;

            _settings = dlg.Result;
            _settingsService.Save(_settings);
            _musicMonitor.UpdateWhitelist(_settings.MusicWhitelist);

            if (_settings.MusicListeningEnabled && _musicCheckTimer?.IsEnabled == false)
            {
                _musicCheckTimer.Start();
                _ = CheckMusicAsync();
            }
            else if (!_settings.MusicListeningEnabled && _musicCheckTimer?.IsEnabled == true)
            {
                _musicCheckTimer.Stop();
                if (_state == HamsterState.Music)
                {
                    _state = HamsterState.Idle;
                    GoIdle();
                }
            }
        }
        catch { }
    }

    private void OnScreenshotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!CanTransitionTo(HamsterState.Screenshot)) return;
        _state = HamsterState.Screenshot;
        _idleDelayTimer?.Stop();
        _animTimer?.Stop();
        TryLoadAnimation(AnimNames.Screenshot);
    }

    private async void OnWriteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dlg = new WriteDialog();
        string? text = await dlg.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(text)) return;

        _bubbleText = text;
        _state = HamsterState.Writing;
        _idleDelayTimer?.Stop();
        _animTimer?.Stop();

        if (!TryLoadAnimation(AnimNames.TypeStart) &&
            !TryLoadAnimation(AnimNames.Typing) &&
            !TryLoadAnimation(AnimNames.TypeStop))
        {
            _state = HamsterState.Idle;
            ShowBubble();
        }
    }

    // =========================================================================
    // Speech bubble
    // =========================================================================

    private void ShowBubble()
    {
        _bubbleFollowTimer?.Stop();
        _bubbleWindow?.Close();

        var anchor = GetBubbleAnchor();
        _bubbleWindow = new BubbleWindow(_bubbleText, anchor);
        _bubbleWindow.Closed += (_, _) => { _bubbleFollowTimer?.Stop(); _bubbleFollowTimer = null; };
        _bubbleWindow.Show(this);

        _bubbleFollowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _bubbleFollowTimer.Tick += OnBubbleFollowTick;
        _bubbleFollowTimer.Start();

        _state = HamsterState.Idle;
        GoIdle();
    }

    private PixelPoint GetBubbleAnchor()
    {
        var (cx, topY) = GetHamsterVisualCenter();
        return new PixelPoint(Position.X + cx, Position.Y + topY);
    }

    private void OnBubbleFollowTick(object? sender, EventArgs e)
    {
        if (_bubbleWindow is null || !_bubbleWindow.IsVisible)
        { _bubbleFollowTimer?.Stop(); return; }

        var target = GetBubbleAnchor();
        int bw = (int)_bubbleWindow.Bounds.Width;
        int bh = (int)_bubbleWindow.Bounds.Height;
        var desired = new PixelPoint(target.X - bw / 2, target.Y - bh - 6);
        var cur = _bubbleWindow.Position;

        int dx = desired.X - cur.X;
        int dy = desired.Y - cur.Y;
        if (dx == 0 && dy == 0) return;

        int nx = cur.X + Math.Max(1, (int)(dx * 0.07)) * Math.Sign(dx);
        int ny = cur.Y + Math.Max(1, (int)(dy * 0.07)) * Math.Sign(dy);
        _bubbleWindow.Position = new PixelPoint(nx, ny);
    }

    private (int cx, int topY) GetHamsterVisualCenter()
    {
        var frame = CurrentFrame;
        if (frame is null) return ((int)_hamsterImg.Bounds.Width / 2, 0);

        int w = frame.Image.PixelSize.Width;
        int h = frame.Image.PixelSize.Height;
        byte[] alpha = _hitTest.GetAlphaData(frame.Image);

        int minX = w, maxX = 0, minY = h;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (alpha[y * w + x] > 10)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                }

        if (minX > maxX) return (w / 2, 0);
        return ((minX + maxX) / 2, minY);
    }

    // =========================================================================
    // Screenshot
    // =========================================================================

    private async Task TakeScreenshotAsync()
    {
        try
        {
            Hide();
            await Task.Delay(200);

            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "screenshot.png");

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                foreach (var (tool, args) in new[]
                    { ("scrot", $"\"{outPath}\""),
                      ("import", $"-window root \"{outPath}\""),
                      ("gnome-screenshot", $"-f \"{outPath}\"") })
                {
                    try
                    {
                        using var p = Process.Start(new ProcessStartInfo(tool, args)
                            { UseShellExecute = false, CreateNoWindow = true });
                        p?.WaitForExit();
                        if (File.Exists(outPath)) break;
                    }
                    catch { }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                using var p = Process.Start(new ProcessStartInfo("screencapture", $"\"{outPath}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                p?.WaitForExit();
            }
            else // Windows
            {
                var screen = Screens.Primary;
                if (screen is null) return;
                var rect = screen.Bounds;
                var rtb = new RenderTargetBitmap(
                    new PixelSize(rect.Width, rect.Height), new Vector(96, 96));
                rtb.Render(this);
                rtb.Save(outPath);
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null && File.Exists(outPath))
            {
                var data = new DataObject();
                data.Set(DataFormats.Files, new[] { outPath });
                await topLevel.Clipboard.SetDataObjectAsync(data);
            }
        }
        catch { }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(Show);
        }
    }

    // =========================================================================
    // Window region (click-through shape)
    // =========================================================================

    private void UpdateWindowRegion(Bitmap? bitmap)
    {
        return;
        //if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        //if (bitmap is null) return;

        //var hwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        //if (hwnd == IntPtr.Zero) return;

        //try
        //{
        //    int w = bitmap.PixelSize.Width;
        //    int h = bitmap.PixelSize.Height;
        //    byte[] alpha = _hitTest.GetAlphaData(bitmap);

        //    IntPtr region = CreateRectRgn(0, 0, 0, 0);
        //    for (int y = 0; y < h; y++)
        //    {
        //        int? start = null;
        //        for (int x = 0; x <= w; x++)
        //        {
        //            bool opaque = x < w && alpha[y * w + x] > 10;
        //            if (opaque && start is null) start = x;
        //            else if (!opaque && start.HasValue)
        //            {
        //                var row = CreateRectRgn(start.Value, y, x, y + 1);
        //                CombineRgn(region, region, row, 2 /* RGN_OR */);
        //                DeleteObject(row);
        //                start = null;
        //            }
        //        }
        //    }
        //    SetWindowRgn(hwnd, region, false);
        //}
        //catch { }
    }

    // =========================================================================
    // Cleanup
    // =========================================================================

    protected override void OnClosed(EventArgs e)
    {
        _keyboardMonitor.Dispose();
        _hitTest.Clear();
        base.OnClosed(e);
    }
}

/// <summary>
/// Central place for all animation name string constants.
/// Eliminates magic strings scattered across the codebase.
/// </summary>
internal static class AnimNames
{
    public const string MainIdle       = "AnimMainIdle";
    public const string IdleStart1     = "AnimIdleStart1";
    public const string IdleStart2     = "AnimIdleStart2";
    public const string IdleLoop1      = "AnimIdleLoop1";
    public const string IdleLoop2      = "AnimIdleLoop2";
    public const string IdleFinish1    = "AnimIdleFinish1";
    public const string IdleFinish2    = "AnimIdleFinish2";
    public const string Idle1          = "AnimIdle1";
    public const string Idle3          = "AnimIdle3";
    public const string Idle4          = "AnimIdle4";
    public const string Idle5          = "AnimIdle5";
    public const string Idle6          = "AnimIdle6";
    public const string TypeStart      = "AnimTypingStart";
    public const string Typing         = "AnimTyping";
    public const string TypeStop       = "AnimTypingStop";
    public const string MusicStart     = "AnimMusicStart";
    public const string MusicLoop      = "AnimMusicLoop";
    public const string MusicFinish    = "AnimMusicFinish";
    public const string MoveStart      = "AnimCharacterMoveStart";
    public const string Moving         = "AnimCharacterMoving";
    public const string MoveFinish     = "AnimCharacterMoveFinish";
    public const string DragFileStart      = "AnimDragFileStart";
    public const string DragFileProcessing = "AnimDragFileProcessing";
    public const string DragFileFinish     = "AnimDragFileFinish";
    public const string AfkStart       = "AnimIdleStart3";
    public const string AfkLoop        = "AnimIdleLoop3";
    public const string AfkFinish      = "AnimIdleFinish3";
    public const string Screenshot     = "AnimScreenshotFinish";
}
