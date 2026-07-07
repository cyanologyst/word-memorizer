using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    }

    private static void RenderMastery(TextBlock label, ProgressBar bar, int value, int total)
    {
        label.Text = value.ToString("N0");
        bar.Value = value * 100.0 / total;
    }
}
