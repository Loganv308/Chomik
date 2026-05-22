using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Chomik.Models;

namespace Chomik.Views;

/// <summary>
/// Custom message / about dialog. Optionally shows a looping animation
/// (used by the About menu item to display the music-loop animation).
/// Styled to match the dark hamster aesthetic from the original WinForms version.
/// </summary>
public sealed class MessageBox : Window
{
    private readonly List<AnimationFrame>? _frames;
    private int _frameIndex = 0;
    private readonly Image _animImg;
    private DispatcherTimer? _animTimer;

    public MessageBox(string message, List<AnimationFrame>? animFrames = null)
    {
        _frames = animFrames;

        SystemDecorations = SystemDecorations.None;
        Background = new SolidColorBrush(Color.FromRgb(8, 8, 12));
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Optional animation preview
        _animImg = new Image
        {
            Width = 80,
            Height = 80,
            Stretch = Stretch.Uniform,
            IsVisible = _frames?.Count > 0,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (_frames?.Count > 0)
            _animImg.Source = _frames[0].Image;

        var msgLabel = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var okBtn = new Button
        {
            Content = "OK",
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        okBtn.Click += (_, _) => Close();

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(148, 0, 211)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(20),
            Child = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { _animImg, msgLabel, okBtn },
            },
        };

        Content = border;

        // Drag to move (borderless window)
        Point dragOffset = default;
        bool dragging = false;
        border.PointerPressed += (_, e) =>
        {
            var pos = e.GetPosition(this);
            var screen = this.PointToScreen(pos);
            dragOffset = new Point(Position.X - screen.X, Position.Y - screen.Y);
            dragging = true;
            e.Pointer.Capture(border);
        };
        border.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var screen = this.PointToScreen(e.GetPosition(this));
            Position = new PixelPoint(
                (int)(screen.X + dragOffset.X),
                (int)(screen.Y + dragOffset.Y));
        };
        border.PointerReleased += (_, _) => dragging = false;

        Opened += (_, _) => StartAnimation();
        Closed += (_, _) => _animTimer?.Stop();
    }

    private void StartAnimation()
    {
        if (_frames is null || _frames.Count == 0) return;

        _animTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(_frames[0].DurationMs),
        };

        _animTimer.Tick += (_, _) =>
        {
            _frameIndex = (_frameIndex + 1) % _frames.Count;
            _animImg.Source = _frames[_frameIndex].Image;
            _animTimer.Interval = TimeSpan.FromMilliseconds(_frames[_frameIndex].DurationMs);
        };

        _animTimer.Start();
    }
}