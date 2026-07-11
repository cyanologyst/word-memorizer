using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class AboutPage : Page
{
    private readonly UpdateService _updateService = new();

    public AboutPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var version = UpdateService.GetCurrentVersion();
        VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
        StoragePathText.Text = App.Data.Store.RootPath;
    }

    private void DocumentationButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Open("https://github.com/cyanologyst/word-memorizer/blob/main/docs/wordlist-json-format.md");
    }

    private void GitHubButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Open("https://github.com/cyanologyst/word-memorizer");
    }

    private void OpenStorageButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Data.Store.RootPath);
        Open(App.Data.Store.RootPath);
    }

    private async void CheckUpdateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        UpdateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        UpdateProgressRing.IsActive = true;

        var status = await _updateService.CheckAsync();
        UpdateStatusText.Text = status.Message;
        InstallUpdateButton.Visibility = status.State is AppUpdateState.Available or AppUpdateState.Required
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        UpdateProgressRing.IsActive = false;
        UpdateProgressRing.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = true;
    }

    private void InstallUpdateButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        UpdateService.OpenInstaller();
    }

    private static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
