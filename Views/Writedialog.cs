using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Chomik.Views;

/// <summary>
/// Simple text-entry dialog for the "Write" menu item.
/// Returns the entered string (or null if cancelled) via ShowDialog.
/// </summary>
public sealed class WriteDialog : Window
{
    private readonly TextBox _input;

    public WriteDialog()
    {
        Title = "write something";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SystemDecorations = SystemDecorations.BorderOnly;
        Background = new SolidColorBrush(Color.FromRgb(18, 18, 24));

        _input = new TextBox
        {
            Watermark = "what should the hamster say?",
            MaxLength = 200,
            AcceptsReturn = false,
            Margin = new Thickness(0, 0, 0, 0),
        };

        // Submit on Enter
        _input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) Submit();
            else if (e.Key == Key.Escape) Close(null);
        };

        var okBtn = new Button
        {
            Content = "Say it",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        okBtn.Click += (_, _) => Submit();

        var cancelBtn = new Button
        {
            Content = "Cancel",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        cancelBtn.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = "Make the hamster talk",
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 14,
                },
                _input,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,8,*"),
                    Children =
                    {
                        WithColumn(cancelBtn, 0),
                        WithColumn(okBtn, 2),
                    },
                },
            },
        };

        Opened += (_, _) =>
        {
            _input.Focus();
        };
    }

    private void Submit()
    {
        string text = _input.Text?.Trim() ?? "";
        Close(string.IsNullOrEmpty(text) ? null : text);
    }

    private static Control WithColumn(Control c, int col)
    {
        Grid.SetColumn(c, col);
        return c;
    }
}