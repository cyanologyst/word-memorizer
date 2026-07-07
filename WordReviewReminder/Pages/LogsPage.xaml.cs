using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
                    review.WordListId
                };
            })
            .ToList();
        LogsView.ItemsSource = timeline;
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

        File.Copy(App.Data.Store.LogsPath, file.Path, overwrite: true);
        StatusText.Text = "Exported";
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }
}
