using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class AboutPage : Page
{
    private readonly UpdateService _updateService = new();
    private string _diagnostics = "";

    public AboutPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var version = UpdateService.GetCurrentVersion();
        VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}";
        StoragePathText.Text = App.Data.Store.RootPath;
        _diagnostics = BuildDiagnostics(version);
        DiagnosticsSummaryText.Text = $"Windows {Environment.OSVersion.Version} · .NET {Environment.Version} · {App.Data.WordLists.Count:N0} wordlists · {App.Data.TotalWords:N0} active words";
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

    private void CopyDiagnosticsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_diagnostics);
        Clipboard.SetContent(package);
        App.Feedback.Success("Diagnostics copied", "Version, runtime, data location, and library counts are ready to paste.");
    }

    private void SupportButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        Open("https://github.com/cyanologyst/word-memorizer/issues");
    }

    private void LicensesButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "Docs", "third-party-notices.md");
        Open(File.Exists(localPath)
            ? localPath
            : "https://github.com/cyanologyst/word-memorizer/blob/main/docs/third-party-notices.md");
    }

    private void Page_SizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < UiLayout.MediumPageWidth;
        InfoColumn2.Width = compact ? new Microsoft.UI.Xaml.GridLength(0) : new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(LocalDataCard, compact ? 1 : 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(LocalDataCard, compact ? 0 : 1);
        SupportColumn2.Width = compact ? new Microsoft.UI.Xaml.GridLength(0) : new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star);
        Microsoft.UI.Xaml.Controls.Grid.SetRow(DiagnosticsCard, compact ? 1 : 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(DiagnosticsCard, compact ? 0 : 1);
    }

    private static string BuildDiagnostics(Version? version)
    {
        return string.Join(Environment.NewLine,
        [
            $"Word Review Reminder {version}",
            $"OS: {Environment.OSVersion}",
            $"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $".NET: {Environment.Version}",
            $"Wordlists: {App.Data.WordLists.Count}",
            $"Active words: {App.Data.TotalWords}",
            $"Recent cached events: {App.Data.RecentEvents.Count}",
            $"Data folder: {App.Data.Store.RootPath}"
        ]);
    }

    private static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
