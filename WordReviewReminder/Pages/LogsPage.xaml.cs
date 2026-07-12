using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WordReviewReminder.Core;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WordReviewReminder.Pages;

public sealed partial class LogsPage : Page
{
    public LogsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        FilterBox.SelectedIndex = 0;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await App.Data.RefreshAsync();
        var selectedFilter = FilterBox.SelectedIndex switch
        {
            1 => "Known",
            2 => "Later",
            3 => "Skipped",
            _ => ""
        };
        var events = App.Data.RecentEvents.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(selectedFilter))
        {
            events = events.Where(review => review.Action.ToString().Equals(selectedFilter, StringComparison.OrdinalIgnoreCase));
        }

        var timeline = events
            .Select(review =>
            {
                var local = review.Timestamp.ToLocalTime();
                return new
                {
                    Date = local.ToString("yyyy-MM-dd"),
                    Time = local.ToString("HH:mm"),
                    review.Term,
                    review.Action,
                    review.WordListId,
                    ActionGlyph = review.Action switch
                    {
                        ReviewAction.Known => "\uE73E",
                        ReviewAction.Later => "\uE916",
                        _ => "\uE711"
                    },
                    ActionBrush = new SolidColorBrush(review.Action switch
                    {
                        ReviewAction.Known => Color.FromArgb(255, 98, 209, 135),
                        ReviewAction.Later => Color.FromArgb(255, 242, 195, 107),
                        _ => Color.FromArgb(255, 255, 139, 139)
                    })
                };
            })
            .ToList();
        LogsView.ItemsSource = timeline;
        LogsView.Visibility = timeline.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLogsPanel.Visibility = timeline.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyLogsTitle.Text = App.Data.RecentEvents.Count == 0 ? "No review history yet" : "No matching review events";
        EmptyLogsMessage.Text = App.Data.RecentEvents.Count == 0
            ? "Complete a review and the result will appear here."
            : "Choose another event type to broaden the results.";
        StatusText.Text = $"{timeline.Count} visible events - {App.Data.RecentEvents.Count} total";
    }

    private async void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await RefreshAsync();
        }
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(App.Data.Store.LogsPath))
        {
            StatusText.Text = "No log file yet";
            App.Feedback.Show("Nothing to export", "Complete a review first, then export its history.");
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = $"word-review-log-{DateTimeOffset.Now:yyyyMMdd-HHmm}"
        };
        picker.FileTypeChoices.Add("JSON Lines", [".jsonl"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            File.Copy(App.Data.Store.LogsPath, file.Path, overwrite: true);
            StatusText.Text = "Exported";
            App.Feedback.Success("Logs exported", $"Review history was saved to {file.Name}.");
        }
        catch (Exception exception)
        {
            StatusText.Text = "Export failed";
            App.Feedback.Error("Export failed", exception.Message);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }
}
