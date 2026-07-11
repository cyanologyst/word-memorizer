using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;

namespace WordReviewReminder.Pages;

public sealed partial class HomePage : Page
{
    private MiniWidgetWindow? _miniWidgetWindow;

    public HomePage()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        await App.Data.RefreshAsync();
        ActiveWordsText.Text = App.Data.TotalWords.ToString("N0");
        ReviewedTodayText.Text = $"{App.Data.ReviewedToday:N0}/{App.Data.DailyGoalCount}";
        DailyGoalBar.Value = App.Data.DailyGoalProgress;
        DailyGoalChipText.Text = App.Data.ReviewedToday >= App.Data.DailyGoalCount
            ? "Daily goal complete"
            : $"{Math.Max(0, App.Data.DailyGoalCount - App.Data.ReviewedToday):N0} left today";
        StreakText.Text = $"{App.Data.ReviewStreakDays:N0}d";
        StreakHintText.Text = App.Data.ReviewStreakDays > 0 ? "Momentum active" : "Start a streak";
        DueNowText.Text = App.Data.DueNowCount.ToString("N0");
        NextReminderText.Text = App.Data.IsQuietTime(DateTimeOffset.Now)
            ? "Quiet hours"
            : $"{App.Data.Settings.ReminderIntervalMinutes} min";
        NextReminderChipText.Text = App.Data.IsPaused(DateTimeOffset.Now)
            ? $"Paused until {App.Data.PausedUntil?.ToLocalTime():HH:mm}"
            : App.Data.IsQuietTime(DateTimeOffset.Now)
                ? "Quiet hours active"
                : $"Next in {App.Data.Settings.ReminderIntervalMinutes} min";
        ModeChipText.Text = App.Data.Settings.NotificationMode switch
        {
            NotificationMode.Popup => "Popup mode",
            NotificationMode.Toast => "Toast mode",
            _ => "Popup + toast"
        };
        EnabledListsRepeater.ItemsSource = App.Data.WordLists.Where(list => list.IsEnabled).ToList();
        ActivityRepeater.ItemsSource = App.Data.GetWeeklyActivity();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void ReviewNowButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.NavigateTo("review");
    }

    private void MiniButton_Click(object sender, RoutedEventArgs e)
    {
        _miniWidgetWindow ??= new MiniWidgetWindow();
        _miniWidgetWindow.Activate();
    }

    private async void PauseQuickButton_Click(object sender, RoutedEventArgs e)
    {
        App.Data.PauseFor(TimeSpan.FromMinutes(30));
        await RefreshAsync();
    }
}
