using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WordReviewReminder.Pages;

public sealed partial class CalendarPage : Page
{
    public CalendarPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAsync();
        var days = App.Data.GetCalendarActivity();
        CalendarRepeater.ItemsSource = days.Select(day => new CalendarCell(
            day.Date,
            day.Date.Day.ToString(),
            day.Count == 1 ? "1 review" : $"{day.Count} reviews",
            $"{day.DayLabel}: {day.Count} reviews",
            CreateBrush(day.Level))).ToList();
        SummaryText.Text = $"{days.Sum(day => day.Count):N0} reviews across {days.Count(day => day.Count > 0):N0} active days";
    }

    private async void CalendarDay_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CalendarCell day })
        {
            return;
        }

        var events = App.Data.RecentEvents
            .Where(item => item.Timestamp.ToLocalTime().Date == day.Date.Date)
            .OrderByDescending(item => item.Timestamp)
            .ToList();
        var text = events.Count == 0
            ? "No reviews on this day."
            : string.Join(Environment.NewLine, events.Take(30).Select(item => $"{item.Timestamp.ToLocalTime():HH:mm}  {item.Term}  {item.Action}"));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = day.Date.ToString("dddd, MMMM d"),
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.WrapWholeWords },
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }

    private static SolidColorBrush CreateBrush(int level)
    {
        return new SolidColorBrush(level switch
        {
            0 => Color.FromArgb(255, 57, 47, 50),
            1 => Color.FromArgb(255, 89, 77, 95),
            2 => Color.FromArgb(255, 143, 102, 94),
            _ => Color.FromArgb(255, 255, 122, 95)
        });
    }

    private sealed record CalendarCell(DateTime Date, string Day, string Count, string Tooltip, Brush Background);
}
