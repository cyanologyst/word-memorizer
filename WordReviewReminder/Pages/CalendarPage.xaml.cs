using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class CalendarPage : Page
{
    private bool _loaded;

    public CalendarPage()
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
        ActivityProgress.IsActive = true;
        ActivityProgress.Visibility = Visibility.Visible;
        RangeBox.IsEnabled = false;
        try
        {
            await App.Data.RefreshAsync();
            var analytics = await App.Data.GetLearningAnalyticsAsync(SelectedRangeDays());
            Render(analytics);
        }
        catch (Exception exception)
        {
            App.Feedback.Error("Activity could not be loaded", exception.Message);
        }
        finally
        {
            RangeBox.IsEnabled = true;
            ActivityProgress.IsActive = false;
            ActivityProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void Render(LearningAnalyticsSnapshot analytics)
    {
        TotalReviewsText.Text = analytics.TotalReviews.ToString("N0");
        UniqueWordsText.Text = $"{analytics.UniqueWords:N0} unique words";
        ActiveDaysText.Text = $"{analytics.ActiveDays:N0}/{analytics.RangeDays:N0}";
        ConsistencyText.Text = $"{analytics.Consistency:0}% consistency";
        BestDayText.Text = analytics.BestDay?.Date.ToString("MMM d") ?? "-";
        BestDayCaptionText.Text = analytics.BestDay is null ? "No active day yet" : $"{analytics.BestDay.Reviews:N0} reviews";
        RecallText.Text = analytics.TotalReviews == 0 ? "-" : $"{analytics.RecallRate:0}%";
        CalendarPeriodText.Text = $"{analytics.StartDate:MMM d, yyyy} to {analytics.EndDate:MMM d, yyyy}";
        EmptyActivityPanel.Visibility = analytics.TotalReviews == 0 ? Visibility.Visible : Visibility.Collapsed;
        BuildCalendar(analytics.Days);
        ApplyResponsiveLayout(ActualWidth);
    }

    private void BuildCalendar(IReadOnlyList<AnalyticsDay> days)
    {
        ActivityCalendarGrid.Children.Clear();
        ActivityCalendarGrid.ColumnDefinitions.Clear();
        ActivityCalendarGrid.RowDefinitions.Clear();
        ActivityCalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        ActivityCalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        for (var row = 0; row < 7; row++)
        {
            ActivityCalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) });
            var date = new DateTime(2026, 7, 6).AddDays(row);
            var weekday = new TextBlock
            {
                Text = date.ToString("ddd"),
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(weekday, row + 1);
            ActivityCalendarGrid.Children.Add(weekday);
        }

        var first = days.First().Date;
        var last = days.Last().Date;
        var weekStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var weekEnd = last.AddDays(6 - (((int)last.DayOfWeek + 6) % 7));
        var weekCount = (weekEnd - weekStart).Days / 7 + 1;
        var byDate = days.ToDictionary(day => day.Date);

        for (var week = 0; week < weekCount; week++)
        {
            ActivityCalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            var monday = weekStart.AddDays(week * 7);
            if (week == 0 || monday.Month != monday.AddDays(-7).Month || monday.Day <= 7)
            {
                var month = new TextBlock
                {
                    Text = monday.ToString("MMM"),
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                Grid.SetColumn(month, week + 1);
                ActivityCalendarGrid.Children.Add(month);
            }

            for (var weekday = 0; weekday < 7; weekday++)
            {
                var date = monday.AddDays(weekday);
                if (date < first || date > last || !byDate.TryGetValue(date, out var day))
                {
                    continue;
                }

                var tooltip = day.Reviews == 0
                    ? $"{date:dddd, MMMM d}: no reviews"
                    : $"{date:dddd, MMMM d}: {day.Reviews:N0} reviews, {day.RecallRate:0}% recall, {day.DifficultResponses:N0} difficult responses";
                var button = new Button
                {
                    Width = 42,
                    Height = 42,
                    Padding = new Thickness(2),
                    Margin = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Background = ActivityBrush(day.Reviews),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    Tag = new CalendarCell(day.Date, day.Reviews),
                    Content = new StackPanel
                    {
                        Spacing = 1,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Children =
                        {
                            new TextBlock { Text = date.Day.ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center },
                            new TextBlock { Text = day.Reviews.ToString("N0"), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }
                        }
                    }
                };
                AutomationProperties.SetName(button, tooltip);
                ToolTipService.SetToolTip(button, tooltip);
                button.Click += CalendarDay_Click;
                Grid.SetColumn(button, week + 1);
                Grid.SetRow(button, weekday + 1);
                ActivityCalendarGrid.Children.Add(button);
            }
        }
    }

    private static Brush ActivityBrush(int reviews)
    {
        var key = reviews switch
        {
            0 => "PremiumLayerBrush",
            <= 2 => "PremiumAccentSoftBrush",
            <= 5 => "PremiumWarningSoftBrush",
            _ => "PremiumAccentBrush"
        };
        return (Brush)Application.Current.Resources[key];
    }

    private async void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CalendarCell day })
        {
            return;
        }

        var allEvents = await App.Data.LogService.ReadAllAsync();
        var events = allEvents
            .Where(item => item.Timestamp.ToLocalTime().Date == day.Date)
            .OrderByDescending(item => item.Timestamp)
            .ToList();
        var text = events.Count == 0
            ? "No reviews on this day."
            : string.Join(Environment.NewLine, events.Take(40).Select(item => $"{item.Timestamp.ToLocalTime():HH:mm}  {item.Term}  {ActionLabel(item.Action)}"));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = day.Date.ToString("dddd, MMMM d"),
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.WrapWholeWords }
            },
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }

    private int SelectedRangeDays() => RangeBox.SelectedIndex switch
    {
        0 => 28,
        1 => 56,
        _ => 91
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

        SummaryColumn3.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SummaryColumn4.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetRow(BestDayCard, compact ? 1 : 0);
        Grid.SetColumn(BestDayCard, compact ? 0 : 2);
        Grid.SetRow(RecallCard, compact ? 1 : 0);
        Grid.SetColumn(RecallCard, compact ? 1 : 3);
    }

    private static string ActionLabel(ReviewAction action) => action switch
    {
        ReviewAction.Known => "Known",
        ReviewAction.Later => "Review Later",
        _ => "Skipped"
    };

    private sealed record CalendarCell(DateTime Date, int Reviews);
}
