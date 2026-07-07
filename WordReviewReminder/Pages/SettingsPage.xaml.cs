using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _loading = true;
        var settings = App.Data.Settings;
        IntervalBox.Value = settings.ReminderIntervalMinutes;
        IntervalSlider.Value = settings.ReminderIntervalMinutes;
        NotificationModeBox.SelectedIndex = settings.NotificationMode switch
        {
            NotificationMode.Popup => 0,
            NotificationMode.Toast => 1,
            _ => 2
        };
        PopupDurationBox.Value = settings.PopupDurationSeconds;
        PopupDurationSlider.Value = settings.PopupDurationSeconds;
        SelectionModeBox.SelectedIndex = settings.SelectionMode == ReviewSelectionMode.Random ? 1 : 0;
        QuietHoursToggle.IsOn = settings.QuietHoursEnabled;
        QuietStartPicker.Time = settings.QuietHoursStart.ToTimeSpan();
        QuietEndPicker.Time = settings.QuietHoursEnd.ToTimeSpan();
        StartWithWindowsToggle.IsOn = settings.StartWithWindows;
        _loading = false;
        UpdatePreview();
    }

    private void IntervalSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        IntervalBox.Value = Math.Round(e.NewValue);
        UpdatePreview();
    }

    private void IntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        IntervalSlider.Value = Math.Round(args.NewValue);
        UpdatePreview();
    }

    private void PopupDurationSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        PopupDurationBox.Value = Math.Round(e.NewValue);
        UpdatePreview();
    }

    private void PopupDurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        PopupDurationSlider.Value = Math.Round(args.NewValue);
        UpdatePreview();
    }

    private void PreviewControl_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void Preset15Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetIntervalPreset(15);
    }

    private void Preset30Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetIntervalPreset(30);
    }

    private void Preset60Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetIntervalPreset(60);
    }

    private void SetIntervalPreset(int minutes)
    {
        IntervalBox.Value = minutes;
        IntervalSlider.Value = minutes;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var interval = Math.Max(1, (int)Math.Round(IntervalBox.Value));
        var duration = Math.Max(5, (int)Math.Round(PopupDurationBox.Value));
        var mode = NotificationModeBox.SelectedIndex switch
        {
            0 => "popup card",
            1 => "toast",
            _ => "popup card and toast"
        };

        PreviewDurationBar.Value = duration;
        PreviewSummaryText.Text = $"Every {interval} min, show a {mode} for {duration} sec"
            + (QuietHoursToggle.IsOn ? ", except during quiet hours." : ".");
    }

    private async void SaveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var settings = new UserSettings
        {
            ReminderIntervalMinutes = Math.Max(1, (int)IntervalBox.Value),
            NotificationMode = NotificationModeBox.SelectedIndex switch
            {
                0 => NotificationMode.Popup,
                1 => NotificationMode.Toast,
                _ => NotificationMode.Both
            },
            PopupDurationSeconds = Math.Max(5, (int)PopupDurationBox.Value),
            QuietHoursEnabled = QuietHoursToggle.IsOn,
            QuietHoursStart = TimeOnly.FromTimeSpan(QuietStartPicker.Time),
            QuietHoursEnd = TimeOnly.FromTimeSpan(QuietEndPicker.Time),
            StartWithWindows = StartWithWindowsToggle.IsOn,
            SelectionMode = SelectionModeBox.SelectedIndex == 1 ? ReviewSelectionMode.Random : ReviewSelectionMode.DueFirst
        };

        await App.Data.SaveSettingsAsync(settings);
        StartupService.SetStartWithWindows(settings.StartWithWindows);
        StatusText.Text = "Saved";
        UpdatePreview();
    }
}
