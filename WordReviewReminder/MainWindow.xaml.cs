using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using WordReviewReminder.Core;
using WordReviewReminder.Pages;
using WordReviewReminder.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WordReviewReminder;

public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<string, Type> PageTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
    {
        ["home"] = typeof(HomePage),
        ["review"] = typeof(ReviewPage),
        ["mistakes"] = typeof(MistakeLabPage),
        ["wordlists"] = typeof(WordlistsPage),
        ["statistics"] = typeof(StatisticsPage),
        ["calendar"] = typeof(CalendarPage),
        ["achievements"] = typeof(AchievementsPage),
        ["logs"] = typeof(LogsPage),
        ["about"] = typeof(AboutPage)
    };

    private readonly DispatcherTimer _timer = new();
    private DateTimeOffset _nextReminderAt = DateTimeOffset.Now.AddSeconds(15);
    private ReminderOverlayWindow? _reminderWindow;
    private readonly DispatcherTimer _achievementDismissTimer = new();
    private readonly DispatcherTimer _feedbackDismissTimer = new();
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private ReviewSessionOptions? _pendingReviewOptions;
    private readonly HotKeyService _hotKeyService = new();
    private readonly ClipboardMonitorService _clipboardMonitor = new();
    private readonly TrayService _trayService;
    private readonly TaskbarProgressService _taskbarProgress;
    private readonly WindowSizeConstraints _windowSizeConstraints;
    private Func<Task>? _pendingFeedbackAction;
    private MiniWidgetWindow? _miniWidgetWindow;

    public MainWindow()
    {
        InitializeComponent();
        NavFrame.Navigated += NavFrame_Navigated;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _windowSizeConstraints = new WindowSizeConstraints(
            windowHandle,
            UiLayout.MinimumWindowWidth,
            UiLayout.MinimumWindowHeight);
        _taskbarProgress = new TaskbarProgressService(windowHandle);
        _hotKeyService.Pressed += (_, _) => DispatcherQueue.TryEnqueue(ReviewNow);
        _clipboardMonitor.WordDetected += ClipboardMonitor_WordDetected;
        _trayService = new TrayService(
            () => DispatcherQueue.TryEnqueue(ReviewNow),
            () => DispatcherQueue.TryEnqueue(() => App.Data.PauseFor(TimeSpan.FromMinutes(30))),
            () => DispatcherQueue.TryEnqueue(OpenMiniWidget),
            () => DispatcherQueue.TryEnqueue(async () => await CreateQuickBackupAsync()),
            () => DispatcherQueue.TryEnqueue(() => App.Current.Exit()));
        UpdateWindowsIntegrations();

        var initialPageTag = PageTypes.ContainsKey(App.Data.Settings.LastPageTag) &&
                             !string.Equals(App.Data.Settings.LastPageTag, "review", StringComparison.OrdinalIgnoreCase)
            ? App.Data.Settings.LastPageTag
            : "home";
        var initialItem = FindNavigationItem(initialPageTag);
        if (initialItem is not null)
        {
            NavView.SelectedItem = initialItem;
        }

        NavigateToPage(initialPageTag);
        App.Data.AchievementUnlocked += Data_AchievementUnlocked;
        App.Data.SettingsChanged += Data_SettingsChanged;
        App.Feedback.MessageRequested += Feedback_MessageRequested;
        _achievementDismissTimer.Interval = TimeSpan.FromSeconds(5.5);
        _achievementDismissTimer.Tick += AchievementDismissTimer_Tick;
        _feedbackDismissTimer.Tick += FeedbackDismissTimer_Tick;
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick += Timer_Tick;
        _timer.Start();
        Closed += MainWindow_Closed;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private async void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateToPage("settings");
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            var parameter = string.Equals(tag, "review", StringComparison.OrdinalIgnoreCase)
                ? _pendingReviewOptions
                : null;
            _pendingReviewOptions = null;
            NavigateToPage(tag, parameter);

            if (!string.Equals(tag, "review", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await App.Data.SaveLastPageAsync(tag);
                }
                catch (Exception exception)
                {
                    App.Feedback.Error("Navigation state was not saved", exception.Message);
                }
            }
        }
    }

    private void NavigateToPage(string tag, object? parameter = null)
    {
        var pageType = string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase)
            ? typeof(SettingsPage)
            : PageTypes.GetValueOrDefault(tag)
              ?? throw new InvalidOperationException($"Unknown navigation item tag: {tag}");

        if (NavFrame.CurrentSourcePageType == pageType && parameter is null)
        {
            return;
        }

        NavFrame.Navigate(pageType, parameter);
    }

    private NavigationViewItem? FindNavigationItem(string tag)
    {
        return NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(candidate => string.Equals(candidate.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (NavFrame.Content is not FrameworkElement content)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(content, true);
        var visual = ElementCompositionPreview.GetElementVisual(content);
        if (!_animationsEnabled)
        {
            visual.Opacity = 1;
            return;
        }

        visual.Opacity = 0;
        var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 1, easing);
        opacity.Duration = TimeSpan.FromMilliseconds(180);
        var translation = visual.Compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0, new Vector3(0, 8, 0));
        translation.InsertKeyFrame(1, Vector3.Zero, easing);
        translation.Duration = TimeSpan.FromMilliseconds(220);
        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation", translation);
    }

    public void NavigateTo(string tag)
    {
        var item = FindNavigationItem(tag);
        if (item is not null)
        {
            if (ReferenceEquals(NavView.SelectedItem, item))
            {
                NavigateToPage(tag);
            }
            else
            {
                NavView.SelectedItem = item;
            }
        }
    }

    public void StartReviewSession(ReviewSessionOptions options)
    {
        _pendingReviewOptions = options;
        var reviewItem = NavView.MenuItems.OfType<NavigationViewItem>().First(item => item.Tag?.ToString() == "review");
        if (ReferenceEquals(NavView.SelectedItem, reviewItem))
        {
            NavFrame.Navigate(typeof(ReviewPage), options);
            _pendingReviewOptions = null;
        }
        else
        {
            NavView.SelectedItem = reviewItem;
        }
    }

    public void SetFocusMode(bool enabled)
    {
        NavView.IsPaneVisible = !enabled;
        AppTitleBar.IsPaneToggleButtonVisible = !enabled;
    }

    public void SetTaskbarProgress(int value, int maximum) => _taskbarProgress.Set(value, maximum);

    public void ClearTaskbarProgress() => _taskbarProgress.Clear();

    private void ReviewNow()
    {
        Activate();
        StartReviewSession(new ReviewSessionOptions
        {
            Goal = App.Data.Settings.DefaultSessionSize,
            FocusMode = true
        });
    }

    private async void CommandPaletteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ShowCommandPaletteAsync();
    }

    private async Task ShowCommandPaletteAsync()
    {
        var entries = new List<PaletteEntry>
        {
            new("Dashboard", "Page", async () => { NavigateTo("home"); await Task.CompletedTask; }),
            new("Review", "Page", async () => { NavigateTo("review"); await Task.CompletedTask; }),
            new("Mistake Lab", "Page", async () => { NavigateTo("mistakes"); await Task.CompletedTask; }),
            new("Wordlists", "Page", async () => { NavigateTo("wordlists"); await Task.CompletedTask; }),
            new("Statistics", "Page", async () => { NavigateTo("statistics"); await Task.CompletedTask; }),
            new("Activity", "Page", async () => { NavigateTo("calendar"); await Task.CompletedTask; }),
            new("Achievements", "Page", async () => { NavigateTo("achievements"); await Task.CompletedTask; }),
            new("Logs", "Page", async () => { NavigateTo("logs"); await Task.CompletedTask; }),
            new("Start focused review", "Command", async () => { ReviewNow(); await Task.CompletedTask; }),
            new("Pause reminders for 30 minutes", "Command", async () => { App.Data.PauseFor(TimeSpan.FromMinutes(30)); await Task.CompletedTask; })
        };
        entries.AddRange(App.Data.AllEnabledWords.Take(1000).Select(word => new PaletteEntry(
            word.Term,
            word.ShortMeaning ?? "Word",
            async () =>
            {
                if (NavFrame.Content is Page page)
                {
                    await WordDetailsDialog.ShowAsync(page, word);
                }
            })));

        var search = new AutoSuggestBox { PlaceholderText = "Search words, pages, and commands", QueryIcon = new SymbolIcon(Symbol.Find) };
        var results = new ListView { Height = 360, SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = "Label" };
        void Filter()
        {
            var query = search.Text.Trim();
            results.ItemsSource = entries
                .Where(entry => query.Length == 0 || entry.Label.Contains(query, StringComparison.OrdinalIgnoreCase) || entry.Context.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Take(40)
                .ToList();
            results.SelectedIndex = results.Items.Count > 0 ? 0 : -1;
        }
        search.TextChanged += (_, _) => Filter();
        Filter();
        var content = new StackPanel { Spacing = 10, Width = 560 };
        content.Children.Add(search);
        content.Children.Add(results);
        var dialog = new ContentDialog
        {
            XamlRoot = NavFrame.XamlRoot,
            Title = "Command palette",
            Content = content,
            PrimaryButtonText = "Open",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && results.SelectedItem is PaletteEntry selected)
        {
            await selected.Action();
        }
    }

    private void OpenMiniWidget()
    {
        _miniWidgetWindow ??= new MiniWidgetWindow();
        _miniWidgetWindow.Activate();
    }

    private async Task CreateQuickBackupAsync()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Word Review Backups");
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, $"word-review-{DateTimeOffset.Now:yyyyMMdd-HHmm}.wordreview.zip");
        await App.Data.BackupService.CreateAsync(destination);
    }

    private void Data_SettingsChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateWindowsIntegrations);
    }

    private void UpdateWindowsIntegrations()
    {
        _hotKeyService.Unregister();
        if (App.Data.Settings.GlobalHotkeyEnabled)
        {
            _hotKeyService.Register(WinRT.Interop.WindowNative.GetWindowHandle(this));
        }

        _clipboardMonitor.Update(App.Data.Settings.ClipboardQuickAddEnabled);
    }

    private void ClipboardMonitor_WordDetected(object? sender, string word)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = NavFrame.XamlRoot,
                Title = "Add copied word?",
                Content = $"Add “{word}” to Personal Words and look up its details?",
                PrimaryButtonText = "Add word",
                CloseButtonText = "Not now",
                DefaultButton = ContentDialogButton.Primary
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var draft = new WordEntry("clipboard-preview", word, null, null, null, null, null, null);
            var details = await App.Data.EnrichmentService.GetAsync(draft, App.Data.Settings.DictionaryLookupEnabled);
            await App.Data.AddPersonalWordAsync(word, details);
        });
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _windowSizeConstraints.Dispose();
        _timer.Stop();
        _feedbackDismissTimer.Stop();
        App.Feedback.MessageRequested -= Feedback_MessageRequested;
        _achievementDismissTimer.Stop();
        _trayService.Dispose();
        _hotKeyService.Dispose();
        _clipboardMonitor.Update(false);
        _taskbarProgress.Clear();
    }

    private void Feedback_MessageRequested(object? sender, AppFeedbackMessage message)
    {
        DispatcherQueue.TryEnqueue(() => ShowFeedback(message));
    }

    private void ShowFeedback(AppFeedbackMessage message)
    {
        _feedbackDismissTimer.Stop();
        GlobalFeedbackBar.Title = message.Title;
        GlobalFeedbackBar.Message = message.Message;
        GlobalFeedbackBar.Severity = message.Severity switch
        {
            AppFeedbackSeverity.Success => InfoBarSeverity.Success,
            AppFeedbackSeverity.Warning => InfoBarSeverity.Warning,
            AppFeedbackSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };

        _pendingFeedbackAction = message.Action;
        FeedbackActionButton.Content = message.ActionLabel;
        FeedbackActionButton.Visibility = message.Action is null || string.IsNullOrWhiteSpace(message.ActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        GlobalFeedbackBar.IsOpen = true;
        _feedbackDismissTimer.Interval = message.Duration ?? (message.Action is null
            ? TimeSpan.FromSeconds(5)
            : TimeSpan.FromSeconds(15));
        _feedbackDismissTimer.Start();
    }

    private void FeedbackDismissTimer_Tick(object? sender, object e)
    {
        _feedbackDismissTimer.Stop();
        GlobalFeedbackBar.IsOpen = false;
        _pendingFeedbackAction = null;
    }

    private async void FeedbackActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = _pendingFeedbackAction;
        _feedbackDismissTimer.Stop();
        GlobalFeedbackBar.IsOpen = false;
        _pendingFeedbackAction = null;
        if (action is null)
        {
            return;
        }

        try
        {
            await action();
        }
        catch (Exception exception)
        {
            App.Feedback.Error("Action failed", exception.Message);
        }
    }

    private sealed record PaletteEntry(string Label, string Context, Func<Task> Action);

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
            var compact = App.Data.Settings.CompactNotificationsWhenFullscreen &&
                          FullscreenDetector.IsForegroundAppFullscreen(WinRT.Interop.WindowNative.GetWindowHandle(this));
            _reminderWindow?.Close();
            _reminderWindow = new ReminderOverlayWindow(word, App.Data.Settings.PopupDurationSeconds, async action =>
            {
                await App.Data.RecordReviewAsync(word, action);
                if (NavFrame.Content is HomePage home)
                {
                    await home.RefreshAsync();
                }
            },
            duration => App.Data.SnoozeWordAsync(word, duration),
            compact);
            SoundService.Play("reminder-arrived");
            _reminderWindow.Activate();
        }
    }
}
