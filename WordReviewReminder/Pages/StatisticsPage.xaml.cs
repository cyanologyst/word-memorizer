using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class StatisticsPage : Page
{
    public StatisticsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAsync();
        Render();
    }

    private void Render()
    {
        DailyGoalText.Text = $"{App.Data.ReviewedToday:N0}/{App.Data.DailyGoalCount}";
        DailyGoalBar.Value = App.Data.DailyGoalProgress;
        DailyGoalRing.Value = App.Data.DailyGoalProgress;
        StreakText.Text = $"{App.Data.ReviewStreakDays:N0} days";
        DueNowText.Text = App.Data.DueNowCount.ToString("N0");
        TotalActiveText.Text = App.Data.TotalWords.ToString("N0");
        InsightText.Text = App.Data.ReviewedToday >= App.Data.DailyGoalCount
            ? "Goal complete. Keep the streak warm or take a clean break."
            : $"{Math.Max(0, App.Data.DailyGoalCount - App.Data.ReviewedToday):N0} reviews left to finish today's goal.";

        var mastery = App.Data.GetMasterySummary();
        var total = Math.Max(1, App.Data.TotalWords);
        MasterySummaryText.Text = $"{mastery.Mastered:N0} mastered";
        RenderMastery(NewText, NewBar, mastery.New, total);
        RenderMastery(LearningText, LearningBar, mastery.Learning, total);
        RenderMastery(FamiliarText, FamiliarBar, mastery.Familiar, total);
        RenderMastery(MasteredText, MasteredBar, mastery.Mastered, total);

        var missed = App.Data.GetMostMissedWords();
        MissedRepeater.ItemsSource = missed;
        NoMissesText.Visibility = missed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActivityRepeater.ItemsSource = App.Data.GetWeeklyActivity();
        ApplyResponsiveLayout(ActualWidth);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < UiLayout.MediumPageWidth;

        HeaderActionColumn.Width = compact ? new GridLength(0) : new GridLength(330);
        Grid.SetRow(NextActionCard, compact ? 1 : 0);
        Grid.SetColumn(NextActionCard, compact ? 0 : 1);
        NextActionCard.Margin = compact ? new Thickness(0) : new Thickness(0);

        MetricColumn3.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MetricColumn4.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(DueNowCard, compact ? 1 : 0);
        Grid.SetColumn(DueNowCard, compact ? 0 : 2);
        Grid.SetRow(TotalActiveCard, compact ? 1 : 0);
        Grid.SetColumn(TotalActiveCard, compact ? 1 : 3);

        InsightColumn2.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(NeedsAttentionCard, compact ? 1 : 0);
        Grid.SetColumn(NeedsAttentionCard, compact ? 0 : 1);
    }

    private static void RenderMastery(TextBlock label, ProgressBar bar, int value, int total)
    {
        label.Text = value.ToString("N0");
        bar.Value = value * 100.0 / total;
    }

    private void StartReviewButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.NavigateTo("review");
    }
}
