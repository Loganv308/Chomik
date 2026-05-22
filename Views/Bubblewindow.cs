using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;

namespace Chomik.Views;

/// <summary>
/// A borderless, always-on-top speech bubble that floats above the hamster
/// and displays user-entered text. Closes itself after a timeout.
/// Positioned and followed by MainWindow via the bubble follow timer.
/// </summary>
public sealed class BubbleWindow : Window
{
    private const int AutoCloseSecs = 8;

    public BubbleWindow(string text, PixelPoint initialAnchor)
    {
        // Window chrome
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        // Position: centre above the anchor point (MainWindow adjusts this
        // each tick via the bubble follow timer)
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(initialAnchor.X - 80, initialAnchor.Y - 60);

        // Layout
        var label = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Black,
            FontSize = 13,
            FontFamily = new FontFamily("Inter,Arial,sans-serif"),
            MaxWidth = 220,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 8),
        };

        // Rounded rectangle bubble background
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(180, 180, 200)),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(10),
            Child = label,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 8,
                OffsetX = 0,
                OffsetY = 2,
                Color = Color.FromArgb(40, 0, 0, 0),
            }),
        };

        // Small triangle tail pointing downward (drawn as a rotated polygon)
        var tail = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            Points =
            [
                new Point(0, 0),
                new Point(14, 0),
                new Point(7, 10),
            ],
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var tailBorder = new Polygon
        {
            Fill = Brushes.Transparent,
            Stroke = new SolidColorBrush(Color.FromRgb(180, 180, 200)),
            StrokeThickness = 1.5,
            Points =
            [
                new Point(0, 0),
                new Point(14, 0),
                new Point(7, 10),
            ],
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var tailCanvas = new Canvas
        {
            Width = 14,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { tailBorder, tail },
        };

        Content = new StackPanel
        {
            Spacing = 0,
            Children = { border, tailCanvas },
        };

        // Click to dismiss
        PointerPressed += (_, _) => Close();

        // Auto-close after timeout
        var timer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(AutoCloseSecs),
        };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}