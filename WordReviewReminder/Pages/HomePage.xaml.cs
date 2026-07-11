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
        ApplyDashboardLayout(ActualWidth);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyDashboardLayout(e.NewSize.Width);
    }

    private void ApplyDashboardLayout(double width)
    {
        var wide = width >= 920;
        Grid.SetRow(HeaderActions, wide ? 0 : 1);
        Grid.SetColumn(HeaderActions, wide ? 1 : 0);
        HeaderActions.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        HeaderActions.Margin = wide ? new Thickness(0) : new Thickness(0, 12, 0, 0);

        MetricColumn3.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        MetricColumn4.Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(StreakCard, wide ? 0 : 1);
        Grid.SetColumn(StreakCard, wide ? 2 : 0);
        StreakCard.Margin = wide ? new Thickness(0) : new Thickness(0, 10, 0, 0);
        Grid.SetRow(DueCard, wide ? 0 : 1);
        Grid.SetColumn(DueCard, wide ? 3 : 1);
        DueCard.Margin = wide ? new Thickness(0) : new Thickness(0, 10, 0, 0);

        BottomColumn2.Width = wide ? new GridLength(0.85, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(WordlistsPanel, wide ? 0 : 1);
        Grid.SetColumn(WordlistsPanel, wide ? 1 : 0);
        WordlistsPanel.Margin = wide ? new Thickness(0) : new Thickness(0, 14, 0, 0);
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
