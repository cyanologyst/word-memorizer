using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class ReviewPage : Page
{
    private const int SessionGoal = 20;
    private readonly SpeechService _speech = new();
    private WordEntry? _currentWord;
    private int _sessionCount;
    private bool _revealed;

    public ReviewPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Focus(FocusState.Programmatic);
        await LoadNextAsync(resetSession: true);
    }

    private async Task LoadNextAsync(bool resetSession = false)
    {
        await App.Data.RefreshAsync();
        if (resetSession)
        {
            _sessionCount = 0;
        }

        _currentWord = App.Data.PickNextWord(DateTimeOffset.Now);
        _revealed = false;
        Render();
        CardEntranceStoryboard.Begin();
    }

    private void Render()
    {
        var hasWord = _currentWord is not null;
        var sessionComplete = _sessionCount >= SessionGoal;
        CompletionPanel.Visibility = sessionComplete ? Visibility.Visible : Visibility.Collapsed;
        FocusCard.Visibility = sessionComplete ? Visibility.Collapsed : Visibility.Visible;
        KnowButton.IsEnabled = hasWord;
        LaterButton.IsEnabled = hasWord;
        SkipButton.IsEnabled = hasWord;
        RevealButton.IsEnabled = hasWord;
        ProgressText.Text = $"{_sessionCount} / {SessionGoal}";
        SessionProgressBar.Value = _sessionCount;

        if (sessionComplete)
        {
            StatusText.Text = "Session complete";
            return;
        }

        if (_currentWord is null)
        {
            WordText.Text = "No words due";
            MetaText.Text = "Enable or import a wordlist to start reviewing.";
            MeaningText.Text = "";
            HiddenPromptText.Visibility = Visibility.Collapsed;
            return;
        }

        WordText.Text = _currentWord.Term;
        MetaText.Text = $"{_currentWord.PartOfSpeech}  {_currentWord.Pronunciation}".Trim();
        SourceText.Text = App.Data.FindListForWord(_currentWord)?.Title ?? "Wordlist";
        HiddenPromptText.Visibility = _revealed ? Visibility.Collapsed : Visibility.Visible;
        HiddenPromptText.Opacity = _revealed ? 0 : 1;
        MeaningText.Opacity = _revealed ? 1 : 0;
        MeaningText.Text = _currentWord.ShortMeaning ?? "";
        RevealButtonText.Text = _revealed ? "Meaning Revealed" : "Reveal Meaning";
        StatusText.Text = App.Data.FindListForWord(_currentWord)?.Title ?? "";
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        RevealMeaning();
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
        _sessionCount = Math.Min(SessionGoal, _sessionCount + 1);
        _currentWord = App.Data.PickNextWord(DateTimeOffset.Now);
        _revealed = false;
        Render();
        if (_sessionCount < SessionGoal)
        {
            CardEntranceStoryboard.Begin();
        }
    }

    private async void RestartSessionButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadNextAsync(resetSession: true);
    }

    private void RevealMeaning()
    {
        if (_currentWord is null || _revealed)
        {
            return;
        }

        _revealed = true;
        HiddenPromptText.Visibility = Visibility.Visible;
        MeaningText.Opacity = 0;
        MeaningTransform.TranslateY = 14;
        RevealButtonText.Text = "Meaning Revealed";
        RevealStoryboard.Begin();
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Space:
                RevealMeaning();
                e.Handled = true;
                break;
            case VirtualKey.Number1:
            case VirtualKey.NumberPad1:
                await RecordAsync(ReviewAction.Known);
                e.Handled = true;
                break;
            case VirtualKey.Number2:
            case VirtualKey.NumberPad2:
                await RecordAsync(ReviewAction.Later);
                e.Handled = true;
                break;
            case VirtualKey.Number3:
            case VirtualKey.NumberPad3:
                await RecordAsync(ReviewAction.Skipped);
                e.Handled = true;
                break;
        }
    }
}
