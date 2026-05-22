using Avalonia.Controls;
using Avalonia.Layout;
using Chomik.Models;
using System.Diagnostics;

namespace Chomik.Views;

/// <summary>
/// Settings dialog combining both codebases:
/// - Music listening toggle + whitelist (from both versions)
/// - Idle delay slider (from WinForms version)
/// - AFK timeout (from Avalonia version)
/// - File handling: Shred / Permanent delete / Recycle (unified)
/// </summary>
public sealed class SettingsDialog : Window
{
    public AppSettings Result { get; private set; }

    private readonly CheckBox _musicCheck;
    private readonly Slider _idleSlider;
    private readonly TextBlock _idleValueLabel;
    private readonly NumericUpDown _afkSpinner;
    private readonly CheckBox _shredCheck;
    private readonly CheckBox _permanentCheck;
    private readonly ListBox _whitelistBox;
    private readonly TextBox _whitelistEntry;

    public SettingsDialog(AppSettings current)
    {
        Result = new AppSettings
        {
            MusicListeningEnabled = current.MusicListeningEnabled,
            ShredFiles = current.ShredFiles,
            PermanentDelete = current.PermanentDelete,
            IdleDelaySeconds = current.IdleDelaySeconds,
            AfkTimeoutMinutes = current.AfkTimeoutMinutes,
            MusicWhitelist = new List<string>(current.MusicWhitelist)
        };

        Title = "settings";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // ---- Music ----
        _musicCheck = new CheckBox
        {
            Content = "listen for music",
            IsChecked = current.MusicListeningEnabled,
        };

        _whitelistBox = new ListBox
        {
            Height = 90,
            ItemsSource = Result.MusicWhitelist,
        };

        _whitelistEntry = new TextBox { Watermark = "process name…", Width = 200 };

        var addBtn = new Button { Content = "Add" };
        addBtn.Click += (_, _) =>
        {
            string t = _whitelistEntry.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(t) && !Result.MusicWhitelist.Contains(t))
            {
                Result.MusicWhitelist.Add(t);
                _whitelistBox.ItemsSource = null;
                _whitelistBox.ItemsSource = Result.MusicWhitelist;
                _whitelistEntry.Text = "";
            }
        };

        var removeBtn = new Button { Content = "Remove" };
        removeBtn.Click += (_, _) =>
        {
            if (_whitelistBox.SelectedItem is string sel)
            {
                Result.MusicWhitelist.Remove(sel);
                _whitelistBox.ItemsSource = null;
                _whitelistBox.ItemsSource = Result.MusicWhitelist;
            }
        };

        var detectBtn = new Button { Content = "Detect running apps" };
        detectBtn.Click += (_, _) =>
        {
            var apps = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                .Select(p => p.ProcessName)
                .Distinct()
                .OrderBy(n => n)
                .ToList();
            _whitelistEntry.Text = apps.FirstOrDefault() ?? "";
        };

        // ---- Idle delay ----
        _idleSlider = new Slider { Minimum = 1, Maximum = 50, Value = current.IdleDelaySeconds, Width = 220 };
        _idleValueLabel = new TextBlock { Text = current.IdleDelaySeconds.ToString("0.0") + "s", VerticalAlignment = VerticalAlignment.Center };
        _idleSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Slider.Value))
                _idleValueLabel.Text = _idleSlider.Value.ToString("0.0") + "s";
        };

        // ---- AFK ----
        _afkSpinner = new NumericUpDown
        {
            Minimum = 1, Maximum = 60,
            Value = current.AfkTimeoutMinutes,
            Width = 80,
        };

        // ---- File handling ----
        _shredCheck = new CheckBox
        {
            Content = "Secure shred (AES-256 encrypt + 7-pass overwrite)",
            IsChecked = current.ShredFiles,
        };
        _shredCheck.IsCheckedChanged += (_, _) =>
        {
            if (_shredCheck.IsChecked == true) _permanentCheck.IsChecked = false;
        };

        _permanentCheck = new CheckBox
        {
            Content = "Permanent delete (no overwrite)",
            IsChecked = current.PermanentDelete && !current.ShredFiles,
        };
        _permanentCheck.IsCheckedChanged += (_, _) =>
        {
            if (_permanentCheck.IsChecked == true) _shredCheck.IsChecked = false;
        };

        // ---- Buttons ----
        var okBtn = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
        okBtn.Click += (_, _) =>
        {
            Result.MusicListeningEnabled = _musicCheck.IsChecked == true;
            Result.IdleDelaySeconds = _idleSlider.Value;
            Result.AfkTimeoutMinutes = (int)(_afkSpinner.Value ?? 3);
            Result.ShredFiles = _shredCheck.IsChecked == true;
            Result.PermanentDelete = _permanentCheck.IsChecked == true && _shredCheck.IsChecked != true;

            Close(true);
        };

        var cancelBtn = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Right };
        cancelBtn.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Music", FontWeight = Avalonia.Media.FontWeight.Bold },
                _musicCheck,
                new TextBlock { Text = "App whitelist (leave empty = react to any music)" },
                _whitelistBox,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { _whitelistEntry, addBtn, removeBtn, detectBtn } },

                new Separator(),
                new TextBlock { Text = "Behaviour", FontWeight = Avalonia.Media.FontWeight.Bold },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = {
                        new TextBlock { Text = "Idle delay:", VerticalAlignment = VerticalAlignment.Center },
                        _idleSlider,
                        _idleValueLabel
                    }
                },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = {
                        new TextBlock { Text = "AFK timeout (minutes):", VerticalAlignment = VerticalAlignment.Center },
                        _afkSpinner
                    }
                },

                new Separator(),
                new TextBlock { Text = "File eating", FontWeight = Avalonia.Media.FontWeight.Bold },
                _shredCheck,
                _permanentCheck,
                new TextBlock
                {
                    Text = "If neither is checked, files go to the recycle bin / trash.",
                    Foreground = Avalonia.Media.Brushes.Gray,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },

                new Separator(),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { cancelBtn, okBtn }
                },
            }
        };
    }
}
