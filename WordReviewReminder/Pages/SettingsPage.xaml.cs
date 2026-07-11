using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WordReviewReminder.Core;
using WordReviewReminder.Services;
using Windows.Media.SpeechSynthesis;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace WordReviewReminder.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _loading = true;
    private readonly SpeechService _speech = new();
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    public SettingsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        _loading = true;
        var settings = App.Data.Settings;
        IntervalBox.Value = settings.ReminderIntervalMinutes;
        IntervalSlider.Value = settings.ReminderIntervalMinutes;
        NotificationModeBox.SelectedIndex = settings.NotificationMode switch
        {
            NotificationMode.Popup => 0,
            NotificationMode.Toast => 1,
            _ => 2
        };
        PopupDurationBox.Value = settings.PopupDurationSeconds;
        PopupDurationSlider.Value = settings.PopupDurationSeconds;
        SelectionModeBox.SelectedIndex = settings.SelectionMode == ReviewSelectionMode.Random ? 1 : 0;
        QuietHoursToggle.IsOn = settings.QuietHoursEnabled;
        UpdateQuietHoursState(settings.QuietHoursEnabled);
        QuietStartPicker.Time = settings.QuietHoursStart.ToTimeSpan();
        QuietEndPicker.Time = settings.QuietHoursEnd.ToTimeSpan();
        StartWithWindowsToggle.IsOn = settings.StartWithWindows;
        GlobalHotkeyToggle.IsOn = settings.GlobalHotkeyEnabled;
        ClipboardQuickAddToggle.IsOn = settings.ClipboardQuickAddEnabled;
        CompactFullscreenToggle.IsOn = settings.CompactNotificationsWhenFullscreen;
        SoundToggle.IsOn = settings.SoundEnabled;
        DefaultSessionSizeBox.Value = settings.DefaultSessionSize;
        VoiceBox.ItemsSource = SpeechSynthesizer.AllVoices.Select(voice => voice.DisplayName).ToList();
        VoiceBox.SelectedItem = settings.VoiceName ?? SpeechSynthesizer.DefaultVoice?.DisplayName;
        SpeechRateSlider.Value = settings.SpeechRate;
        _loading = false;
        UpdatePreview(markDirty: false);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var showPreview = e.NewSize.Width >= 1080;
        PreviewPane.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = showPreview ? new GridLength(300) : new GridLength(0);
    }

    private void IntervalSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        IntervalBox.Value = Math.Round(e.NewValue);
        UpdatePreview();
    }

    private void IntervalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        IntervalSlider.Value = Math.Round(args.NewValue);
        UpdatePreview();
    }

    private void PopupDurationSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        PopupDurationBox.Value = Math.Round(e.NewValue);
        UpdatePreview();
    }

    private void PopupDurationBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
        {
            return;
        }

        PopupDurationSlider.Value = Math.Round(args.NewValue);
        UpdatePreview();
    }

    private void DefaultSessionSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!_loading && !double.IsNaN(args.NewValue))
        {
            UpdatePreview();
        }
    }

    private void SpeechRateSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void PreviewControl_Changed(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_loading)
        {
            if (ReferenceEquals(sender, QuietHoursToggle))
            {
                UpdateQuietHoursState(QuietHoursToggle.IsOn);
            }

            UpdatePreview();
        }
    }

    private void QuietTimePicker_SelectedTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (!_loading)
        {
            UpdatePreview();
        }
    }

    private void Preset15Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetIntervalPreset(15);
    }

    private void Preset30Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetIntervalPreset(30);
    }

    private void Preset60Button_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        SetIntervalPreset(60);
    }

    private void SetIntervalPreset(int minutes)
    {
        IntervalBox.Value = minutes;
        IntervalSlider.Value = minutes;
        UpdatePreview();
    }

    private void UpdateQuietHoursState(bool enabled)
    {
        QuietStartPicker.IsEnabled = enabled;
        QuietEndPicker.IsEnabled = enabled;
        QuietHoursFields.Opacity = enabled ? 1 : 0.48;
    }

    private void UpdatePreview(bool markDirty = true)
    {
        var interval = Math.Max(1, (int)Math.Round(IntervalBox.Value));
        var duration = Math.Max(5, (int)Math.Round(PopupDurationBox.Value));
        var mode = NotificationModeBox.SelectedIndex switch
        {
            0 => "popup card",
            1 => "toast",
            _ => "popup card and toast"
        };

        PreviewDurationBar.Value = duration;
        PreviewSummaryText.Text = $"Every {interval} min, show a {mode} for {duration} sec"
            + (QuietHoursToggle.IsOn ? ", except during quiet hours." : ".");
        if (markDirty && !_loading)
        {
            StatusText.Text = "Unsaved changes";
            ShowSaveBar();
        }
    }

    private async void SaveButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var settings = new UserSettings
        {
            ReminderIntervalMinutes = Math.Max(1, (int)IntervalBox.Value),
            NotificationMode = NotificationModeBox.SelectedIndex switch
            {
                0 => NotificationMode.Popup,
                1 => NotificationMode.Toast,
                _ => NotificationMode.Both
            },
            PopupDurationSeconds = Math.Max(5, (int)PopupDurationBox.Value),
            QuietHoursEnabled = QuietHoursToggle.IsOn,
            QuietHoursStart = TimeOnly.FromTimeSpan(QuietStartPicker.Time),
            QuietHoursEnd = TimeOnly.FromTimeSpan(QuietEndPicker.Time),
            StartWithWindows = StartWithWindowsToggle.IsOn,
            SelectionMode = SelectionModeBox.SelectedIndex == 1 ? ReviewSelectionMode.Random : ReviewSelectionMode.DueFirst,
            GlobalHotkeyEnabled = GlobalHotkeyToggle.IsOn,
            ClipboardQuickAddEnabled = ClipboardQuickAddToggle.IsOn,
            DictionaryLookupEnabled = true,
            SoundEnabled = SoundToggle.IsOn,
            CompactNotificationsWhenFullscreen = CompactFullscreenToggle.IsOn,
            VoiceName = VoiceBox.SelectedItem?.ToString(),
            SpeechRate = SpeechRateSlider.Value,
            DefaultSessionSize = Math.Max(5, (int)DefaultSessionSizeBox.Value),
            PopupLeft = App.Data.Settings.PopupLeft,
            PopupTop = App.Data.Settings.PopupTop
        };

        await App.Data.SaveSettingsAsync(settings);
        StartupService.SetStartWithWindows(settings.StartWithWindows);
        StatusText.Text = "Saved";
        UpdatePreview(markDirty: false);
        await Task.Delay(850);
        await HideSaveBarAsync();
    }

    private void ShowSaveBar()
    {
        if (SaveBar.Visibility == Visibility.Visible)
        {
            return;
        }

        SaveBar.Visibility = Visibility.Visible;
        var visual = ElementCompositionPreview.GetElementVisual(SaveBar);
        ElementCompositionPreview.SetIsTranslationEnabled(SaveBar, true);
        if (!_animationsEnabled)
        {
            visual.Opacity = 1;
            return;
        }

        visual.Opacity = 0;
        var easing = visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 1, easing);
        opacity.Duration = TimeSpan.FromMilliseconds(170);
        var translation = visual.Compositor.CreateVector3KeyFrameAnimation();
        translation.InsertKeyFrame(0, new Vector3(0, 8, 0));
        translation.InsertKeyFrame(1, Vector3.Zero, easing);
        translation.Duration = TimeSpan.FromMilliseconds(210);
        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation", translation);
    }

    private async Task HideSaveBarAsync()
    {
        if (!_animationsEnabled)
        {
            SaveBar.Visibility = Visibility.Collapsed;
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(SaveBar);
        var opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(1, 0);
        opacity.Duration = TimeSpan.FromMilliseconds(150);
        visual.StartAnimation("Opacity", opacity);
        await Task.Delay(170);
        SaveBar.Visibility = Visibility.Collapsed;
    }

    private async void TestVoiceButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var originalVoice = App.Data.Settings.VoiceName;
        var originalRate = App.Data.Settings.SpeechRate;
        App.Data.Settings.VoiceName = VoiceBox.SelectedItem?.ToString();
        App.Data.Settings.SpeechRate = SpeechRateSlider.Value;
        await _speech.SpeakAsync("Vocabulary review ready");
        App.Data.Settings.VoiceName = originalVoice;
        App.Data.Settings.SpeechRate = originalRate;
    }

    private async void BackupButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = $"word-review-{DateTimeOffset.Now:yyyyMMdd-HHmm}.wordreview" };
        picker.FileTypeChoices.Add("Word Review backup", [".zip"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await App.Data.BackupService.CreateAsync(file.Path);
        StatusText.Text = "Backup created";
    }

    private async void RestoreButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".zip");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore this backup?",
            Content = "Current local files with matching names will be replaced. The app will reload restored data immediately.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await App.Data.BackupService.RestoreAsync(file.Path);
        await App.Data.InitializeAsync();
        Page_Loaded(this, new RoutedEventArgs());
        StatusText.Text = "Backup restored";
    }
}
