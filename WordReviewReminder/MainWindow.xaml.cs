using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private ReminderOverlayWindow? _reminderWindow;
    private readonly DispatcherTimer _achievementDismissTimer = new();
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        NavFrame.Navigate(typeof(HomePage));
        App.Data.AchievementUnlocked += Data_AchievementUnlocked;
        _achievementDismissTimer.Interval = TimeSpan.FromSeconds(5.5);
        _achievementDismissTimer.Tick += AchievementDismissTimer_Tick;
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
                case "review":
                    NavFrame.Navigate(typeof(ReviewPage));
                    break;
                case "wordlists":
                    NavFrame.Navigate(typeof(WordlistsPage));
                    break;
                case "statistics":
                    NavFrame.Navigate(typeof(StatisticsPage));
                    break;
                case "achievements":
                    NavFrame.Navigate(typeof(AchievementsPage));
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

    public void NavigateTo(string tag)
    {
        var item = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private void Data_AchievementUnlocked(object? sender, AchievementUnlockedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => ShowAchievementUnlock(e.Achievement));
    }

    private void ShowAchievementUnlock(AchievementSnapshot achievement)
    {
        UnlockBadgeImage.Source = new BitmapImage(new Uri($"ms-appx:///Assets/Achievements/{achievement.Definition.IconFileName}"));
        UnlockTitleText.Text = achievement.Definition.Title;
        UnlockDescriptionText.Text = achievement.Definition.Description;
        UnlockCelebrationHost.Visibility = Visibility.Visible;
        _achievementDismissTimer.Stop();
        _achievementDismissTimer.Start();

        if (!_animationsEnabled)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            ElementCompositionPreview.SetIsTranslationEnabled(UnlockCelebrationHost, true);
            var visual = ElementCompositionPreview.GetElementVisual(UnlockCelebrationHost);
            visual.CenterPoint = new Vector3((float)UnlockCelebrationHost.ActualWidth, 0, 0);
            visual.Opacity = 0;
            visual.Scale = new Vector3(0.96f, 0.96f, 1);
            var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));

            var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(1, 1, easing);
            opacity.Duration = TimeSpan.FromMilliseconds(300);
            var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
            scale.InsertKeyFrame(1, Vector3.One, easing);
            scale.Duration = TimeSpan.FromMilliseconds(420);
            var translation = visual.Compositor.CreateScalarKeyFrameAnimation();
            translation.InsertKeyFrame(0, -14);
            translation.InsertKeyFrame(1, 0, easing);
            translation.Duration = TimeSpan.FromMilliseconds(420);

            visual.StartAnimation("Opacity", opacity);
            visual.StartAnimation("Scale", scale);
            visual.StartAnimation("Translation.Y", translation);
        });
    }

    private void AchievementDismissTimer_Tick(object? sender, object e)
    {
        _achievementDismissTimer.Stop();
        HideAchievementUnlock();
    }

    private void UnlockToastClose_Click(object sender, RoutedEventArgs e)
    {
        _achievementDismissTimer.Stop();
        HideAchievementUnlock();
    }

    private void HideAchievementUnlock()
    {
        if (UnlockCelebrationHost.Visibility != Visibility.Visible)
        {
            return;
        }

        if (!_animationsEnabled)
        {
            UnlockCelebrationHost.Visibility = Visibility.Collapsed;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(UnlockCelebrationHost);
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 0);
        opacity.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Opacity", opacity);

        var collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
        collapseTimer.Tick += (_, _) =>
        {
            collapseTimer.Stop();
            UnlockCelebrationHost.Visibility = Visibility.Collapsed;
        };
        collapseTimer.Start();
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
            _reminderWindow = new ReminderOverlayWindow(word, App.Data.Settings.PopupDurationSeconds, async action =>
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
