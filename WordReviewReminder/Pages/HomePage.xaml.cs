using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;

namespace WordReviewReminder.Pages;

public sealed partial class HomePage : Page
{
    private MiniWidgetWindow? _miniWidgetWindow;
    private ReviewSessionPlan? _recommendedPlan;

    public HomePage()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        try
        {
            await App.Data.RefreshAsync();
        _recommendedPlan = App.Data.PlanReviewSession(App.Data.Settings.DefaultSessionSize);
        DueReviewText.Text = _recommendedPlan.DueCount.ToString("N0");
        ReviewedTodayText.Text = $"{App.Data.ReviewedToday:N0}/{App.Data.DailyGoalCount}";
        DailyGoalBar.Value = App.Data.DailyGoalProgress;
        DailyGoalChipText.Text = App.Data.ReviewedToday >= App.Data.DailyGoalCount
            ? "Daily goal complete"
            : $"{Math.Max(0, App.Data.DailyGoalCount - App.Data.ReviewedToday):N0} left today";
        DifficultWordsText.Text = _recommendedPlan.DifficultCount.ToString("N0");
        NewWordsText.Text = _recommendedPlan.NewCount.ToString("N0");
        NextReminderChipText.Text = App.Data.IsPaused(DateTimeOffset.Now)
            ? $"Paused until {App.Data.PausedUntil?.ToLocalTime():HH:mm}"
            : App.Data.IsQuietTime(DateTimeOffset.Now)
                ? "Quiet hours active"
                : $"Next in {App.Data.Settings.ReminderIntervalMinutes} min";
        StreakChipText.Text = App.Data.ReviewStreakDays == 0
            ? "Start a streak"
            : $"{App.Data.ReviewStreakDays:N0} day streak";
        DailyBriefingText.Text = _recommendedPlan.HasEligibleWords
            ? $"{_recommendedPlan.Options.Goal} words | about {_recommendedPlan.EstimatedMinutes} min | {_recommendedPlan.DueCount} due | {_recommendedPlan.NewCount} new"
            : "No enabled words are available for review";
        DailyBriefingReasonText.Text = _recommendedPlan.Reason;
        ReviewNowButton.IsEnabled = _recommendedPlan.HasEligibleWords;
        ReviewNowButtonText.Text = _recommendedPlan.HasEligibleWords
            ? $"Review {_recommendedPlan.Options.Goal} words"
            : "Review unavailable";
        var enabledLists = App.Data.WordLists.Where(list => list.IsEnabled).ToList();
        EnabledListsRepeater.ItemsSource = enabledLists;
        NoEnabledListsPanel.Visibility = enabledLists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActivityRepeater.ItemsSource = App.Data.GetWeeklyActivity();
        }
        catch (Exception exception)
        {
            App.Feedback.Error("Dashboard could not be refreshed", exception.Message);
        }
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
        if (_recommendedPlan is { HasEligibleWords: true } plan)
        {
            (App.MainWindow as MainWindow)?.StartReviewSession(plan.Options);
        }
        else
        {
            App.Feedback.Show("No words are ready", _recommendedPlan?.Reason ?? "Enable a wordlist to begin.");
        }
    }

    private void CustomizeReviewButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.NavigateTo("review");
    }

    private void OpenWordlistsButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.NavigateTo("wordlists");
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
