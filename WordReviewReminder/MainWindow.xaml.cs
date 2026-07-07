using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;
using WordReviewReminder.Pages;
using WordReviewReminder.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WordReviewReminder;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private DateTimeOffset _nextReminderAt = DateTimeOffset.Now.AddSeconds(15);
    private ReminderWindow? _reminderWindow;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        NavFrame.Navigate(typeof(HomePage));
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "home":
                    NavFrame.Navigate(typeof(HomePage));
                    break;
                case "wordlists":
                    NavFrame.Navigate(typeof(WordlistsPage));
                    break;
                case "logs":
                    NavFrame.Navigate(typeof(LogsPage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }

    private void Timer_Tick(object? sender, object e)
    {
        var now = DateTimeOffset.Now;
        if (now < _nextReminderAt)
        {
            return;
        }

        var word = App.Data.PickNextWord(now);
        _nextReminderAt = App.Data.GetNextReminderAt(now) ?? now.AddMinutes(App.Data.Settings.ReminderIntervalMinutes);

        if (word is null)
        {
            return;
        }

        if (App.Data.Settings.NotificationMode is NotificationMode.Toast or NotificationMode.Both)
        {
            ToastService.Show(word);
        }

        if (App.Data.Settings.NotificationMode is NotificationMode.Popup or NotificationMode.Both)
        {
            _reminderWindow?.Close();
            _reminderWindow = new ReminderWindow(word, App.Data.Settings.PopupDurationSeconds, async action =>
            {
                await App.Data.RecordReviewAsync(word, action);
                if (NavFrame.Content is HomePage home)
                {
                    await home.RefreshAsync();
                }
            });
            _reminderWindow.Activate();
        }
    }
}
