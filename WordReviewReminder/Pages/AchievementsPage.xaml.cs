using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using WordReviewReminder.Core;
using WordReviewReminder.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WordReviewReminder.Pages;

public sealed partial class AchievementsPage : Page
{
    private readonly List<AchievementCardViewModel> _allAchievements = [];
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private string _filter = "All";
    private int _entranceIndex;

    public AchievementsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAchievementsAsync();
        LoadAchievements();
    }

    private void LoadAchievements()
    {
        _allAchievements.Clear();
        _allAchievements.AddRange(App.Data.Achievements
            .Select(snapshot => new AchievementCardViewModel(snapshot))
            .OrderByDescending(item => item.IsUnlocked)
            .ThenByDescending(item => item.ProgressPercent)
            .ThenBy(item => item.Title));

        var unlocked = _allAchievements.Count(item => item.IsUnlocked);
        var total = Math.Max(1, _allAchievements.Count);
        var completion = unlocked * 100.0 / total;
        UnlockedSummaryText.Text = $"{unlocked:N0} of {_allAchievements.Count:N0} unlocked";
        CompletionSummaryText.Text = $"{completion:N0}% complete";
        CollectionPercentText.Text = $"{completion:N0}%";
        CollectionProgressBar.Value = completion;
        CollectionHintText.Text = unlocked == 0
            ? "Your first review opens the collection."
            : $"{Math.Max(0, _allAchievements.Count - unlocked):N0} achievements still waiting.";

        RenderFeatured();
        ApplyFilter();
    }

    private void RenderFeatured()
    {
        var featured = _allAchievements
            .Where(item => !item.IsUnlocked)
            .OrderByDescending(item => item.ProgressPercent)
            .FirstOrDefault()
            ?? _allAchievements.OrderByDescending(item => item.UnlockedAt).FirstOrDefault();

        if (featured is null)
        {
            return;
        }

        FeaturedEyebrowText.Text = featured.IsUnlocked ? "LATEST UNLOCK" : "CLOSEST MILESTONE";
        FeaturedTitleText.Text = featured.Title;
        FeaturedDescriptionText.Text = featured.Description;
        FeaturedProgressText.Text = featured.ProgressLabel;
        FeaturedProgressBar.Value = featured.ProgressPercent;
        FeaturedBadgeImage.Source = new BitmapImage(new Uri(featured.IconUri));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FeaturedBadgeImage, $"{featured.Title} achievement badge");

        if (!_animationsEnabled)
        {
            return;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(FeaturedBadgeImage, true);
        var visual = ElementCompositionPreview.GetElementVisual(FeaturedBadgeImage);
        var floatAnimation = visual.Compositor.CreateScalarKeyFrameAnimation();
        floatAnimation.InsertKeyFrame(0, 0);
        floatAnimation.InsertKeyFrame(0.5f, -4, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0), new Vector2(0.2f, 1)));
        floatAnimation.InsertKeyFrame(1, 0);
        floatAnimation.Duration = TimeSpan.FromSeconds(4.2);
        floatAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation.Y", floatAnimation);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton selected || selected.Tag is not string filter)
        {
            return;
        }

        _filter = filter;
        AllFilter.IsChecked = selected == AllFilter;
        UnlockedFilter.IsChecked = selected == UnlockedFilter;
        ProgressFilter.IsChecked = selected == ProgressFilter;
        LockedFilter.IsChecked = selected == LockedFilter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<AchievementCardViewModel> filtered = _filter switch
        {
            "Unlocked" => _allAchievements.Where(item => item.IsUnlocked),
            "InProgress" => _allAchievements.Where(item => !item.IsUnlocked && item.ProgressPercent > 0),
            "Locked" => _allAchievements.Where(item => !item.IsUnlocked),
            _ => _allAchievements
        };

        var visible = filtered.ToList();
        _entranceIndex = 0;
        AchievementRepeater.ItemsSource = visible;
        ResultsSummaryText.Text = visible.Count == 1 ? "1 badge" : $"{visible.Count:N0} badges";
    }

    private async void AchievementCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AchievementCardViewModel item })
        {
            return;
        }

        var image = new Image
        {
            Source = new BitmapImage(new Uri(item.IconUri)),
            Width = 176,
            Height = 176,
            Opacity = item.BadgeOpacity,
            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(image);
        content.Children.Add(new TextBlock
        {
            Text = item.Description,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        content.Children.Add(new ProgressBar { Height = 7, Maximum = 100, Value = item.ProgressPercent });
        content.Children.Add(new TextBlock { Text = item.ProgressLabel, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock
        {
            Text = item.StatusLabel,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = item.Title,
            Content = content,
            PrimaryButtonText = item.IsUnlocked ? "Export milestone card" : null,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var picker = new FileSavePicker { SuggestedFileName = $"{item.Title.ToLowerInvariant().Replace(' ', '-')}" };
            picker.FileTypeChoices.Add("PNG image", [".png"]);
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
            var file = await picker.PickSaveFileAsync();
            if (file is not null)
            {
                await MilestoneCardService.ExportAsync(item.Snapshot, file.Path);
            }
        }
    }

    private void BadgeCard_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        if (!_animationsEnabled)
        {
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
            return;
        }

        visual.Opacity = 0;
        visual.Scale = new Vector3(0.96f, 0.96f, 1);

        var delay = TimeSpan.FromMilliseconds(Math.Min(_entranceIndex++, 12) * 42);
        var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 1, easing);
        opacity.Duration = TimeSpan.FromMilliseconds(380);
        opacity.DelayTime = delay;
        var scale = visual.Compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1, Vector3.One, easing);
        scale.Duration = TimeSpan.FromMilliseconds(440);
        scale.DelayTime = delay;
        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Scale", scale);
    }

    private void BadgeCard_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_animationsEnabled)
        {
            AnimateCardScale(sender as FrameworkElement, 1.018f, 180);
        }
    }

    private void BadgeCard_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_animationsEnabled)
        {
            AnimateCardScale(sender as FrameworkElement, 1, 220);
        }
    }

    private void BadgeCard_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_animationsEnabled)
        {
            AnimateCardScale(sender as FrameworkElement, 0.988f, 90);
        }
    }

    private void BadgeCard_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_animationsEnabled)
        {
            AnimateCardScale(sender as FrameworkElement, 1.018f, 140);
        }
    }

    private void BadgeCard_PointerCanceled(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_animationsEnabled)
        {
            AnimateCardScale(sender as FrameworkElement, 1, 160);
        }
    }

    private static void AnimateCardScale(FrameworkElement? element, float target, int durationMs)
    {
        if (element is null)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1, new Vector3(target, target, 1), visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0, 1)));
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        visual.StartAnimation("Scale", animation);
    }
}

public sealed class AchievementCardViewModel
{
    private static readonly SolidColorBrush UnlockedForeground = CreateBrush(0xFF, 0x62, 0xD1, 0x87);
    private static readonly SolidColorBrush UnlockedBackground = CreateBrush(0x2E, 0x62, 0xD1, 0x87);
    private static readonly SolidColorBrush UnlockedBorder = CreateBrush(0x66, 0x62, 0xD1, 0x87);
    private static readonly SolidColorBrush ProgressForeground = CreateBrush(0xFF, 0xF2, 0xC3, 0x6B);
    private static readonly SolidColorBrush ProgressBackground = CreateBrush(0x2E, 0xF2, 0xC3, 0x6B);
    private static readonly SolidColorBrush ProgressBorder = CreateBrush(0x66, 0xF2, 0xC3, 0x6B);
    private static readonly SolidColorBrush LockedForeground = CreateBrush(0xFF, 0xB8, 0xAF, 0xB2);
    private static readonly SolidColorBrush LockedBackground = CreateBrush(0x24, 0xB8, 0xAF, 0xB2);
    private static readonly SolidColorBrush LockedBorder = CreateBrush(0x30, 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush ActiveCardBackground = CreateBrush(0xFF, 0x2A, 0x22, 0x25);
    private static readonly SolidColorBrush LockedCardBackground = CreateBrush(0xFF, 0x25, 0x1F, 0x22);

    public AchievementCardViewModel(AchievementSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public AchievementSnapshot Snapshot { get; }
    public string Title => Snapshot.Definition.Title;
    public string Description => Snapshot.Definition.Description;
    public string Category => Snapshot.Definition.Category;
    public string IconUri => $"ms-appx:///Assets/Achievements/{Snapshot.Definition.IconFileName}";
    public double ProgressPercent => Snapshot.ProgressPercent;
    public string ProgressLabel => Snapshot.ProgressLabel;
    public bool IsInProgress => !Snapshot.IsUnlocked && Snapshot.ProgressPercent > 0;
    public string StatusLabel => Snapshot.IsUnlocked ? "Unlocked" : IsInProgress ? "In progress" : "Locked";
    public string StateGlyph => Snapshot.IsUnlocked ? "\uE73E" : IsInProgress ? "\uE916" : "\uE72E";
    public string TierLabel => Snapshot.TierLabel;
    public bool IsUnlocked => Snapshot.IsUnlocked;
    public DateTimeOffset? UnlockedAt => Snapshot.UnlockedAt;
    public double BadgeOpacity => Snapshot.IsUnlocked ? 1 : IsInProgress ? 0.72 : 0.28;
    public string ProgressPercentText => $"{Snapshot.ProgressPercent:N0}%";
    public Brush StateForegroundBrush => Snapshot.IsUnlocked ? UnlockedForeground : IsInProgress ? ProgressForeground : LockedForeground;
    public Brush StateBackgroundBrush => Snapshot.IsUnlocked ? UnlockedBackground : IsInProgress ? ProgressBackground : LockedBackground;
    public Brush CardBorderBrush => Snapshot.IsUnlocked ? UnlockedBorder : IsInProgress ? ProgressBorder : LockedBorder;
    public Brush CardBackgroundBrush => Snapshot.IsUnlocked || IsInProgress ? ActiveCardBackground : LockedCardBackground;
    public Brush ProgressBrush => StateForegroundBrush;
    public Visibility LockVisibility => Snapshot.IsUnlocked ? Visibility.Collapsed : Visibility.Visible;
    public Visibility TierVisibility => string.IsNullOrWhiteSpace(TierLabel) ? Visibility.Collapsed : Visibility.Visible;

    private static SolidColorBrush CreateBrush(byte alpha, byte red, byte green, byte blue)
    {
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }
}
