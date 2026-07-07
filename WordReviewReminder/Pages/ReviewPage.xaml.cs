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
    }

    private void Render()
    {
        var hasWord = _currentWord is not null;
        KnowButton.IsEnabled = hasWord;
        LaterButton.IsEnabled = hasWord;
        SkipButton.IsEnabled = hasWord;
        RevealButton.IsEnabled = hasWord;
        ProgressText.Text = $"{_sessionCount} / {SessionGoal}";
        SessionProgressBar.Value = _sessionCount;

        if (_currentWord is null)
        {
            WordText.Text = "No words due";
            MetaText.Text = "Enable or import a wordlist to start reviewing.";
            HiddenPromptText.Text = "";
            MeaningText.Text = "";
            return;
        }

        WordText.Text = _currentWord.Term;
        MetaText.Text = $"{_currentWord.PartOfSpeech}  {_currentWord.Pronunciation}".Trim();
        HiddenPromptText.Visibility = _revealed ? Visibility.Collapsed : Visibility.Visible;
        MeaningText.Visibility = _revealed ? Visibility.Visible : Visibility.Collapsed;
        MeaningText.Text = _currentWord.ShortMeaning ?? "";
        RevealButton.Content = _revealed ? "Meaning Revealed" : "Reveal Meaning";
        StatusText.Text = App.Data.FindListForWord(_currentWord)?.Title ?? "";
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e)
    {
        _revealed = true;
        Render();
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
    }

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Space:
                _revealed = true;
                Render();
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
