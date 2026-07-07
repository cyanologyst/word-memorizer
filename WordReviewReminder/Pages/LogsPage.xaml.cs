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
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        await App.Data.RefreshAsync();
        LogsView.ItemsSource = App.Data.RecentEvents
            .Select(review => new
            {
                Timestamp = review.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                review.Term,
                review.Action,
                review.WordListId
            })
            .ToList();
        StatusText.Text = $"{App.Data.RecentEvents.Count} recent events";
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
