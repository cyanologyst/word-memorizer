using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var settings = App.Data.Settings;
        IntervalBox.Value = settings.ReminderIntervalMinutes;
        NotificationModeBox.SelectedIndex = settings.NotificationMode switch
        {
            NotificationMode.Popup => 0,
            NotificationMode.Toast => 1,
            _ => 2
        };
        PopupDurationBox.Value = settings.PopupDurationSeconds;
        SelectionModeBox.SelectedIndex = settings.SelectionMode == ReviewSelectionMode.Random ? 1 : 0;
        QuietHoursToggle.IsOn = settings.QuietHoursEnabled;
        QuietStartPicker.Time = settings.QuietHoursStart.ToTimeSpan();
        QuietEndPicker.Time = settings.QuietHoursEnd.ToTimeSpan();
        StartWithWindowsToggle.IsOn = settings.StartWithWindows;
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
    }
}
