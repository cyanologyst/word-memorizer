using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Reflection;

namespace WordReviewReminder.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
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

    private static void Open(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }
}
