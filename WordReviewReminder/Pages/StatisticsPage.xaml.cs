using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class StatisticsPage : Page
{
    private bool _loaded;

    public StatisticsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _loaded = true;
        await RefreshAsync();
    }

    private async void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
        {
            await RefreshAsync();
        }
    }

    private async Task RefreshAsync()
    {
        AnalyticsProgress.IsActive = true;
        AnalyticsProgress.Visibility = Visibility.Visible;
        RangeBox.IsEnabled = false;
        try
        {
            await App.Data.RefreshAsync();
            var analytics = await App.Data.GetLearningAnalyticsAsync(SelectedRangeDays());
            Render(analytics);
        }
        catch (Exception exception)
        {
            App.Feedback.Error("Statistics could not be loaded", exception.Message);
            PeriodInsightText.Text = "Your review history could not be analyzed. Try refreshing the page.";
        }
        finally
        {
            RangeBox.IsEnabled = true;
            AnalyticsProgress.IsActive = false;
            AnalyticsProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void Render(LearningAnalyticsSnapshot analytics)
    {
        ReviewsText.Text = analytics.TotalReviews.ToString("N0");
        ReviewsCaptionText.Text = $"{analytics.UniqueWords:N0} unique words";
        RecallText.Text = analytics.TotalReviews == 0 ? "-" : $"{analytics.RecallRate:0}%";
        RecallCaptionText.Text = analytics.TotalReviews == 0
            ? "No responses in this period"
            : $"{analytics.Known:N0} of {analytics.TotalReviews:N0} marked Known";
        ActiveDaysText.Text = $"{analytics.ActiveDays:N0}/{analytics.RangeDays:N0}";
        ConsistencyText.Text = $"{analytics.Consistency:0}% consistency";
        DifficultText.Text = analytics.DifficultResponses.ToString("N0");
        PeriodInsightText.Text = analytics.TotalReviews == 0
            ? $"No reviews from {analytics.StartDate:MMM d} to {analytics.EndDate:MMM d}."
            : $"{analytics.FirstReviews:N0} first reviews and {analytics.RepeatReviews:N0} repeat reviews. Best day: {analytics.BestDay!.Date:MMM d} with {analytics.BestDay.Reviews:N0}.";

        NoAnalyticsPanel.Visibility = analytics.TotalReviews == 0 ? Visibility.Visible : Visibility.Collapsed;
        InsightGrid.Visibility = analytics.TotalReviews > 0 ? Visibility.Visible : Visibility.Collapsed;
        WordlistComparisonCard.Visibility = analytics.WordLists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var maxReviews = Math.Max(1, analytics.Trend.Max(point => point.Reviews));
        TrendContextText.Text = analytics.Trend.Count == analytics.RangeDays ? "Daily" : "Grouped periods";
        TrendRepeater.ItemsSource = analytics.Trend.Select(point => new TrendDisplay(
            point.Label,
            point.Reviews * 100.0 / maxReviews,
            $"{point.Reviews:N0}",
            point.Reviews == 0 ? "No reviews" : $"{point.RecallRate:0}% recall")).ToList();

        var mastery = analytics.MasteryAtEnd;
        var total = Math.Max(1, App.Data.TotalWords);
        MasteryRepeater.ItemsSource = new[]
        {
            new MasteryDisplay("New", mastery.New * 100.0 / total, mastery.New.ToString("N0")),
            new MasteryDisplay("Learning", mastery.Learning * 100.0 / total, mastery.Learning.ToString("N0")),
            new MasteryDisplay("Familiar", mastery.Familiar * 100.0 / total, mastery.Familiar.ToString("N0")),
            new MasteryDisplay("Mastered", mastery.Mastered * 100.0 / total, mastery.Mastered.ToString("N0"))
        };
        var familiarChange = mastery.Familiar - analytics.MasteryAtStart.Familiar;
        var masteredChange = mastery.Mastered - analytics.MasteryAtStart.Mastered;
        MasteryChangeText.Text = analytics.TotalReviews == 0
            ? "No mastery movement in this period."
            : $"{FormatChange(masteredChange)} mastered · {FormatChange(familiarChange)} familiar in this period";

        var missed = analytics.DifficultWords
            .Take(6)
            .Select(item => new MissedDisplay(item.Term, $"{item.Responses:N0} responses"))
            .ToList();
        MissedRepeater.ItemsSource = missed;
        NoMissesText.Visibility = missed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PracticeDifficultButton.IsEnabled = App.Data.GetMistakeCandidates().Count > 0;

        WordlistRepeater.ItemsSource = analytics.WordLists.Select(item => new WordlistDisplay(
            item.Title,
            $"{item.Reviews:N0} reviews",
            $"{item.RecallRate:0}%")).ToList();
        ApplyResponsiveLayout(ActualWidth);
    }

    private int SelectedRangeDays() => RangeBox.SelectedIndex switch
    {
        0 => 7,
        2 => 90,
        _ => 30
    };

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout(e.NewSize.Width);

    private void ApplyResponsiveLayout(double width)
    {
        var compact = width < UiLayout.MediumPageWidth;
        HeaderActionColumn.Width = compact ? new GridLength(0) : new GridLength(180);
        Grid.SetRow(RangeBox, compact ? 1 : 0);
        Grid.SetColumn(RangeBox, compact ? 0 : 1);
        RangeBox.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        RangeBox.Width = compact ? 180 : double.NaN;

        MetricColumn3.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MetricColumn4.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(ActiveDaysCard, compact ? 1 : 0);
        Grid.SetColumn(ActiveDaysCard, compact ? 0 : 2);
        Grid.SetRow(DifficultCard, compact ? 1 : 0);
        Grid.SetColumn(DifficultCard, compact ? 1 : 3);

        InsightColumn2.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(NeedsAttentionCard, compact ? 1 : 0);
        Grid.SetColumn(NeedsAttentionCard, compact ? 0 : 1);
        LowerColumn2.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(WordlistComparisonCard, compact ? 1 : 0);
        Grid.SetColumn(WordlistComparisonCard, compact ? 0 : 1);
    }

    private void StartReviewButton_Click(object sender, RoutedEventArgs e) =>
        (App.MainWindow as MainWindow)?.NavigateTo("review");

    private void PracticeDifficultButton_Click(object sender, RoutedEventArgs e) =>
        (App.MainWindow as MainWindow)?.StartReviewSession(new ReviewSessionOptions
        {
            Goal = Math.Min(20, Math.Max(1, App.Data.GetMistakeCandidates().Count)),
            DifficultOnly = true,
            FocusMode = true
        });

    private static string FormatChange(int value) => value > 0 ? $"+{value:N0}" : value.ToString("N0");

    private sealed record TrendDisplay(string Label, double Volume, string ReviewsLabel, string RecallLabel);
    private sealed record MasteryDisplay(string Label, double Progress, string CountLabel);
    private sealed record MissedDisplay(string Term, string MissesLabel);
    private sealed record WordlistDisplay(string Title, string ReviewsLabel, string RecallLabel);
}
