using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class HomePage : Page
{
    private readonly SpeechService _speech = new();
    private ReminderWindow? _manualReminderWindow;
    private WordEntry? _currentWord;

    public HomePage()
    {
        InitializeComponent();
    }

    public async Task RefreshAsync()
    {
        await App.Data.RefreshAsync();
        ActiveWordsText.Text = App.Data.TotalWords.ToString("N0");
        ReviewedTodayText.Text = $"{App.Data.ReviewedToday:N0}/{App.Data.DailyGoalCount}";
        DailyGoalBar.Value = App.Data.DailyGoalProgress;
        StreakText.Text = $"{App.Data.ReviewStreakDays:N0}d";
        NextReminderText.Text = App.Data.IsQuietTime(DateTimeOffset.Now)
            ? "Quiet hours"
            : $"{App.Data.Settings.ReminderIntervalMinutes} min";
        EnabledListsRepeater.ItemsSource = App.Data.WordLists.Where(list => list.IsEnabled).ToList();
        ActivityRepeater.ItemsSource = App.Data.GetWeeklyActivity();

        _currentWord ??= App.Data.PickNextWord(DateTimeOffset.Now);
        RenderWord();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _currentWord = App.Data.PickNextWord(DateTimeOffset.Now);
        await RefreshAsync();
    }

    private void ReviewNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWord is null)
        {
            return;
        }

        _manualReminderWindow?.Close();
        _manualReminderWindow = new ReminderWindow(_currentWord, App.Data.Settings.PopupDurationSeconds, async action =>
        {
            await App.Data.RecordReviewAsync(_currentWord, action);
            _currentWord = App.Data.PickNextWord(DateTimeOffset.Now);
            await RefreshAsync();
        });
        _manualReminderWindow.Activate();
    }

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWord is not null)
        {
            await _speech.SpeakAsync(_currentWord.Term);
        }
    }

    private async void KnowButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAsync(ReviewAction.Known);
    }

    private async void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAsync(ReviewAction.Later);
    }

    private async void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        await RecordAsync(ReviewAction.Skipped);
    }

    private async Task RecordAsync(ReviewAction action)
    {
        if (_currentWord is null)
        {
            return;
        }

        await App.Data.RecordReviewAsync(_currentWord, action);
        _currentWord = App.Data.PickNextWord(DateTimeOffset.Now);
        await RefreshAsync();
    }

    private void RenderWord()
    {
        var hasWord = _currentWord is not null;
        KnowButton.IsEnabled = hasWord;
        LaterButton.IsEnabled = hasWord;
        SkipButton.IsEnabled = hasWord;
        DetailsButton.IsEnabled = hasWord;

        if (_currentWord is null)
        {
            WordText.Text = "No active words";
            WordMetaText.Text = "Import or enable a wordlist to begin.";
            MeaningText.Text = "";
            RenderEmptyDetails();
            return;
        }

        WordText.Text = _currentWord.Term;
        WordMetaText.Text = $"{_currentWord.PartOfSpeech}  {_currentWord.Pronunciation}".Trim();
        MeaningText.Text = _currentWord.ShortMeaning ?? "";
        RenderDetails(_currentWord);
    }

    private void RenderDetails(WordEntry word)
    {
        var list = App.Data.FindListForWord(word);
        App.Data.Progress.Entries.TryGetValue(word.Id, out var progress);

        DetailWordText.Text = word.Term;
        DetailMetaText.Text = $"{word.PartOfSpeech}  {word.Pronunciation}".Trim();
        DetailMeaningText.Text = word.ShortMeaning ?? "";
        DetailSourceText.Text = $"{list?.Title ?? "Unknown"} · Chapter {word.Chapter?.ToString() ?? "-"} · #{word.Order?.ToString() ?? "-"}";
        DetailKnownText.Text = (progress?.TimesKnown ?? 0).ToString("N0");
        DetailLaterText.Text = (progress?.TimesLater ?? 0).ToString("N0");
        DetailSeenText.Text = (progress?.TimesSeen ?? 0).ToString("N0");
    }

    private void RenderEmptyDetails()
    {
        DetailWordText.Text = "Nothing selected";
        DetailMetaText.Text = "";
        DetailMeaningText.Text = "";
        DetailSourceText.Text = "";
        DetailKnownText.Text = "0";
        DetailLaterText.Text = "0";
        DetailSeenText.Text = "0";
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        RenderWord();
    }
}
