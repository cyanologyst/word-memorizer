using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder;

public sealed partial class ReminderWindow : Window
{
    private readonly Func<ReviewAction, Task> _recordActionAsync;
    private readonly SpeechService _speech = new();
    private readonly WordEntry _word;
    private readonly DispatcherTimer _closeTimer = new();
    private bool _recorded;

    public ReminderWindow(WordEntry word, int durationSeconds, Func<ReviewAction, Task> recordActionAsync)
    {
        InitializeComponent();
        _word = word;
        _recordActionAsync = recordActionAsync;

        WordText.Text = word.Term;
        MetaText.Text = $"{word.PartOfSpeech}  {word.Pronunciation}".Trim();
        MeaningText.Text = word.ShortMeaning ?? "";

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        PositionWindow();

        _closeTimer.Interval = TimeSpan.FromSeconds(Math.Max(5, durationSeconds));
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            Close();
        };
        _closeTimer.Start();
    }

    private async void KnowButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAndCloseAsync(ReviewAction.Known);
    }

    private async void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAndCloseAsync(ReviewAction.Later);
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAndCloseAsync(ReviewAction.Skipped);
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.Activate();
    }

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        await _speech.SpeakAsync(_word.Term);
    }

    private async Task RecordAndCloseAsync(ReviewAction action)
    {
        if (_recorded)
        {
            return;
        }

        _recorded = true;
        _closeTimer.Stop();
        await _recordActionAsync(action);
        Close();
    }

    private void PositionWindow()
    {
        const int width = 460;
        const int height = 290;

        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            displayArea.WorkArea.X + displayArea.WorkArea.Width - width - 24,
            displayArea.WorkArea.Y + displayArea.WorkArea.Height - height - 24));
    }
}
