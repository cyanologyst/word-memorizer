using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class ReviewPage : Page
{
    private readonly SpeechService _speech = new();
    private readonly DispatcherTimer _recallTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private readonly List<ReviewAction> _sessionActions = [];
    private readonly List<SessionReviewResult> _sessionResults = [];
    private readonly HashSet<string> _reviewedWordIds = new(StringComparer.OrdinalIgnoreCase);
    private WordEntry? _currentWord;
    private ReviewSessionOptions _options = new();
    private ReviewSessionOptions? _pendingOptions;
    private int _sessionCount;
    private int _sessionGoal = 20;
    private int _secondsRemaining;
    private bool _revealed;
    private DateTimeOffset _sessionStartedAt;
    private DateTimeOffset _wordShownAt;
    private DateTimeOffset? _pauseStartedAt;
    private TimeSpan _wordPausedDuration;
    private TimeSpan _sessionPausedDuration;
    private ReviewSessionPlan? _recommendedPlan;
    private MasterySummary _startingMastery = new(0, 0, 0, 0);
    private bool _recording;
    private bool _paused;
    private bool _sessionEnded;
    private bool _sessionRecorded;
    private bool _sessionEndedEarly;

    public ReviewPage()
    {
        InitializeComponent();
        _recallTimer.Tick += RecallTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pendingOptions = e.Parameter as ReviewSessionOptions;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAsync();
        SessionWordListBox.ItemsSource = new[] { new WordListOption(null, "All enabled wordlists") }
            .Concat(App.Data.WordLists.Where(list => list.IsEnabled).Select(list => new WordListOption(list.Id, list.Title)))
            .ToList();
        SessionWordListBox.SelectedItem = (SessionWordListBox.ItemsSource as IEnumerable<WordListOption>)?
            .FirstOrDefault(item => string.Equals(item.Id, App.Data.Settings.LastSessionWordListId, StringComparison.OrdinalIgnoreCase));
        SessionWordListBox.SelectedIndex = SessionWordListBox.SelectedIndex < 0 ? 0 : SessionWordListBox.SelectedIndex;
        SessionGoalBox.Value = Math.Clamp(App.Data.Settings.LastSessionGoal, 1, 100);
        SessionModeBox.SelectedIndex = App.Data.Settings.LastSessionDifficultOnly ? 1 : 0;
        TimedSessionToggle.IsOn = App.Data.Settings.LastSessionTimed;
        FocusModeToggle.IsOn = App.Data.Settings.LastSessionFocusMode;
        UpdateSessionPlans();

        if (_pendingOptions is not null)
        {
            ApplyOptionsToControls(_pendingOptions);
            await StartSessionAsync(_pendingOptions);
            _pendingOptions = null;
        }
    }

    private async void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _recallTimer.Stop();
        for (var attempt = 0; _recording && attempt < 50; attempt++)
        {
            await Task.Delay(20);
        }

        if (!_sessionRecorded && _sessionActions.Count > 0)
        {
            _sessionRecorded = true;
            await App.Data.RecordReviewSessionAsync(_sessionStartedAt, DateTimeOffset.UtcNow, _sessionActions);
        }

        (App.MainWindow as MainWindow)?.SetFocusMode(false);
        (App.MainWindow as MainWindow)?.ClearTaskbarProgress();
    }

    private async void StartConfiguredSessionButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedList = SessionWordListBox.SelectedItem as WordListOption;
        var plan = App.Data.PlanReviewSession(
            Math.Max(1, (int)SessionGoalBox.Value),
            selectedList?.Id,
            SessionModeBox.SelectedIndex == 1);
        if (!plan.HasEligibleWords)
        {
            App.Feedback.Show("No words match this session", plan.Reason, AppFeedbackSeverity.Warning);
            return;
        }

        var options = plan.Options with
        {
            Timed = TimedSessionToggle.IsOn,
            FocusMode = FocusModeToggle.IsOn
        };
        await App.Data.SaveReviewSessionPreferencesAsync(options);
        await StartSessionAsync(options);
    }

    private async void StartRecommendedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recommendedPlan is not { HasEligibleWords: true } plan)
        {
            App.Feedback.Show("No words are ready", _recommendedPlan?.Reason ?? "Enable a wordlist to begin.");
            return;
        }

        await StartSessionAsync(plan.Options);
    }

    private void SessionGoalPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Tag: string value } &&
            int.TryParse(value, out var goal))
        {
            SessionGoalBox.Value = goal;
            UpdateGoalPresetChecks(goal);
            UpdateSessionPlans();
        }
    }

    private void SessionGoalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (!double.IsNaN(args.NewValue))
        {
            UpdateGoalPresetChecks((int)Math.Round(args.NewValue));
            UpdateSessionPlans();
        }
    }

    private void SessionConfiguration_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateSessionPlans();
    }

    private void UpdateSessionPlans()
    {
        if (!IsLoaded || SessionWordListBox.SelectedItem is not WordListOption selectedList)
        {
            return;
        }

        _recommendedPlan = App.Data.PlanReviewSession(App.Data.Settings.DefaultSessionSize);
        RecommendedSummaryText.Text = FormatPlan(_recommendedPlan);
        RecommendedReasonText.Text = _recommendedPlan.Reason;
        StartRecommendedButton.IsEnabled = _recommendedPlan.HasEligibleWords;

        var customPlan = App.Data.PlanReviewSession(
            Math.Max(1, (int)Math.Round(SessionGoalBox.Value)),
            selectedList.Id,
            SessionModeBox.SelectedIndex == 1);
        CustomSessionSummaryText.Text = FormatPlan(customPlan);
        StartConfiguredSessionButton.IsEnabled = customPlan.HasEligibleWords;
        NoEligibleWordsText.Text = customPlan.Reason;
        NoEligibleWordsText.Visibility = customPlan.HasEligibleWords ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatPlan(ReviewSessionPlan plan)
    {
        if (!plan.HasEligibleWords)
        {
            return "No eligible words";
        }

        return $"{plan.Options.Goal} words | about {plan.EstimatedMinutes} min | {plan.DueCount} due | {plan.NewCount} new";
    }

    private void UpdateGoalPresetChecks(int goal)
    {
        Goal10Button.IsChecked = goal == 10;
        Goal20Button.IsChecked = goal == 20;
        Goal30Button.IsChecked = goal == 30;
    }

    private async Task StartSessionAsync(ReviewSessionOptions options)
    {
        _options = options;
        _sessionGoal = Math.Clamp(options.Goal, 1, 100);
        _sessionCount = 0;
        _sessionActions.Clear();
        _sessionResults.Clear();
        _reviewedWordIds.Clear();
        _sessionStartedAt = DateTimeOffset.UtcNow;
        _pauseStartedAt = null;
        _wordPausedDuration = TimeSpan.Zero;
        _sessionPausedDuration = TimeSpan.Zero;
        _recording = false;
        _paused = false;
        _sessionEnded = false;
        _sessionRecorded = false;
        _sessionEndedEarly = false;
        _startingMastery = App.Data.GetMasterySummary();
        SessionSetupPanel.Visibility = Visibility.Collapsed;
        CompletionPanel.Visibility = Visibility.Collapsed;
        PausedPanel.Visibility = Visibility.Collapsed;
        FocusCard.Visibility = Visibility.Visible;
        SessionControlPanel.Visibility = Visibility.Visible;
        (App.MainWindow as MainWindow)?.SetFocusMode(options.FocusMode);
        await LoadNextAsync();
        Focus(FocusState.Programmatic);
    }

    private async Task LoadNextAsync()
    {
        await App.Data.RefreshAsync();
        _currentWord = App.Data.PickNextWord(DateTimeOffset.Now, _options, _reviewedWordIds);
        if (_currentWord is null)
        {
            await CompleteSessionAsync(endedEarly: _sessionCount < _sessionGoal);
            return;
        }

        _revealed = false;
        _wordShownAt = DateTimeOffset.UtcNow;
        _wordPausedDuration = TimeSpan.Zero;
        Render();
        StartRecallTimer();
        if (_animationsEnabled)
        {
            CardEntranceStoryboard.Begin();
        }
        else
        {
            FocusCard.Opacity = 1;
            FocusCardTransform.TranslateY = 0;
        }
    }

    private void Render()
    {
        var hasWord = _currentWord is not null;
        var sessionComplete = _sessionEnded;
        CompletionPanel.Visibility = sessionComplete ? Visibility.Visible : Visibility.Collapsed;
        FocusCard.Visibility = sessionComplete || _paused ? Visibility.Collapsed : Visibility.Visible;
        PausedPanel.Visibility = _paused && !sessionComplete ? Visibility.Visible : Visibility.Collapsed;
        SessionControlPanel.Visibility = sessionComplete ? Visibility.Collapsed : Visibility.Visible;
        KnowButton.IsEnabled = hasWord && _revealed && !_recording && !_paused;
        LaterButton.IsEnabled = hasWord && _revealed && !_recording && !_paused;
        SkipButton.IsEnabled = hasWord && !_recording && !_paused;
        RevealButton.IsEnabled = hasWord && !_recording && !_paused;
        ProgressText.Text = $"{_sessionCount} / {_sessionGoal}";
        SessionProgressBar.Maximum = _sessionGoal;
        SessionProgressBar.Value = _sessionCount;
        (App.MainWindow as MainWindow)?.SetTaskbarProgress(_sessionCount, _sessionGoal);

        if (sessionComplete)
        {
            RenderSummary();
            return;
        }

        if (_currentWord is null)
        {
            WordText.Text = "No matching words";
            MetaText.Text = "Change the session filters and try again.";
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
        RevealButtonText.Text = _revealed ? "Revealed" : "Reveal";
        AdditionalDetailsExpander.IsExpanded = false;
        PopulateAdditionalDetails(_currentWord, _revealed);
        StatusText.Text = App.Data.FindListForWord(_currentWord)?.Title ?? "";
    }

    private void RenderSummary()
    {
        _recallTimer.Stop();
        (App.MainWindow as MainWindow)?.SetFocusMode(false);
        (App.MainWindow as MainWindow)?.SetTaskbarProgress(_sessionCount, Math.Max(1, _sessionCount));
        var known = _sessionActions.Count(action => action == ReviewAction.Known);
        var later = _sessionActions.Count(action => action == ReviewAction.Later);
        var skipped = _sessionActions.Count(action => action == ReviewAction.Skipped);
        var duration = DateTimeOffset.UtcNow - _sessionStartedAt - _sessionPausedDuration;
        var summary = new SessionSummary(_sessionActions.Count, known, later, skipped, duration);
        CompletionTitleText.Text = _sessionEndedEarly ? "Session ended" : "Session complete";
        SessionSummaryText.Text = $"Reviewed {summary.Total:N0} word{(summary.Total == 1 ? "" : "s")} in {duration:mm\\:ss}.";
        KnownSummaryText.Text = known.ToString("N0");
        LaterSummaryText.Text = later.ToString("N0");
        SkippedSummaryText.Text = skipped.ToString("N0");
        AccuracySummaryText.Text = summary.Total == 0 ? "-" : $"{summary.Accuracy:N0}%";
        var mastery = App.Data.GetMasterySummary();
        MasteryMovementText.Text = summary.Total == 0
            ? "No responses were recorded."
            : $"Mastery movement: {FormatChange(mastery.Mastered - _startingMastery.Mastered)} mastered · {FormatChange(mastery.Familiar - _startingMastery.Familiar)} familiar";
        var retryCount = _sessionResults.Count(item => item.Action != ReviewAction.Known);
        ReviewMissedButton.IsEnabled = retryCount > 0;
        ReviewMissedButton.Visibility = retryCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = _sessionEndedEarly ? "Session ended safely" : "Session complete";
        SoundService.Play("session-complete");
    }

    private async Task CompleteSessionAsync(bool endedEarly)
    {
        if (_sessionEnded)
        {
            return;
        }

        FinishPauseIfNeeded();
        _recallTimer.Stop();
        _sessionEnded = true;
        _sessionEndedEarly = endedEarly;
        _currentWord = null;
        if (!_sessionRecorded && _sessionActions.Count > 0)
        {
            _sessionRecorded = true;
            await App.Data.RecordReviewSessionAsync(_sessionStartedAt, DateTimeOffset.UtcNow, _sessionActions);
        }

        await App.Data.RefreshAsync();
        Render();
    }

    private void StartRecallTimer(bool reset = true)
    {
        _recallTimer.Stop();
        if (!_options.Timed || _currentWord is null)
        {
            return;
        }

        if (reset)
        {
            _secondsRemaining = 15;
        }

        StatusText.Text = $"{_secondsRemaining} seconds to decide";
        _recallTimer.Start();
    }

    private async void RecallTimer_Tick(object? sender, object e)
    {
        _secondsRemaining--;
        StatusText.Text = $"{Math.Max(0, _secondsRemaining)} seconds to decide";
        if (_secondsRemaining <= 0)
        {
            _recallTimer.Stop();
            await RecordAsync(ReviewAction.Skipped);
        }
    }

    private void RevealButton_Click(object sender, RoutedEventArgs e) => RevealMeaning();

    private async void SpeakButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentWord is not null)
        {
            await _speech.SpeakAsync(_currentWord.Term);
            await App.Data.RecordPronunciationAsync(_currentWord);
        }
    }

    private async void KnowButton_Click(object sender, RoutedEventArgs e) => await RecordAsync(ReviewAction.Known);
    private async void LaterButton_Click(object sender, RoutedEventArgs e) => await RecordAsync(ReviewAction.Later);
    private async void SkipButton_Click(object sender, RoutedEventArgs e) => await RecordAsync(ReviewAction.Skipped);

    private async Task RecordAsync(ReviewAction action)
    {
        if (_recording || _paused || _sessionEnded || _currentWord is null || _sessionCount >= _sessionGoal)
        {
            return;
        }

        if (!_revealed && action is ReviewAction.Known or ReviewAction.Later)
        {
            App.Feedback.Show("Reveal the meaning first", "Press Space, then rate how well you remembered the word.");
            return;
        }

        _recording = true;
        UpdateActionAvailability();
        try
        {
            _recallTimer.Stop();
            var responseSeconds = Math.Max(0, (DateTimeOffset.UtcNow - _wordShownAt - _wordPausedDuration).TotalSeconds);
            var reviewedWord = _currentWord;
            await App.Data.RecordReviewAsync(reviewedWord, action, responseSeconds);
            _reviewedWordIds.Add(reviewedWord.Id);
            _sessionActions.Add(action);
            _sessionResults.Add(new SessionReviewResult(reviewedWord.Id, action));
            _sessionCount = Math.Min(_sessionGoal, _sessionCount + 1);
            SoundService.Play(action == ReviewAction.Known ? "review-known" : "review-later");

            if (_sessionCount == _sessionGoal)
            {
                await CompleteSessionAsync(endedEarly: false);
                return;
            }

            await LoadNextAsync();
        }
        catch (Exception exception)
        {
            App.Feedback.Error("Review was not recorded", exception.Message);
            StatusText.Text = "Try that response again";
        }
        finally
        {
            _recording = false;
            UpdateActionAvailability();
        }
    }

    private void PauseSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionEnded || _paused || _currentWord is null || _recording)
        {
            return;
        }

        _paused = true;
        _pauseStartedAt = DateTimeOffset.UtcNow;
        _recallTimer.Stop();
        PausedSummaryText.Text = $"{_sessionCount:N0} of {_sessionGoal:N0} words completed. The current word and timer are preserved.";
        StatusText.Text = "Session paused";
        Render();
    }

    private void ResumeSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_paused || _sessionEnded)
        {
            return;
        }

        FinishPauseIfNeeded();
        StatusText.Text = "Session resumed";
        Render();
        StartRecallTimer(reset: false);
        Focus(FocusState.Programmatic);
    }

    private async void EndSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionEnded || _recording)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "End this review session?",
            Content = $"{_sessionCount:N0} of {_sessionGoal:N0} words are complete. Recorded responses are saved; the current word will not be scored.",
            PrimaryButtonText = "End session",
            CloseButtonText = "Keep reviewing",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await CompleteSessionAsync(endedEarly: true);
        }
    }

    private void FinishPauseIfNeeded()
    {
        if (_pauseStartedAt is not DateTimeOffset pauseStarted)
        {
            _paused = false;
            return;
        }

        var duration = DateTimeOffset.UtcNow - pauseStarted;
        _wordPausedDuration += duration;
        _sessionPausedDuration += duration;
        _pauseStartedAt = null;
        _paused = false;
    }

    private void UpdateActionAvailability()
    {
        var hasWord = _currentWord is not null && !_sessionEnded && !_paused && !_recording;
        RevealButton.IsEnabled = hasWord;
        KnowButton.IsEnabled = hasWord && _revealed;
        LaterButton.IsEnabled = hasWord && _revealed;
        SkipButton.IsEnabled = hasWord;
    }

    private async void ReviewMissedButton_Click(object sender, RoutedEventArgs e)
    {
        var retryIds = _sessionResults
            .Where(item => item.Action != ReviewAction.Known)
            .Select(item => item.WordId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (retryIds.Count == 0)
        {
            return;
        }

        await StartSessionAsync(_options with
        {
            Goal = retryIds.Count,
            DifficultOnly = false,
            IncludedWordIds = retryIds
        });
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.ClearTaskbarProgress();
        (App.MainWindow as MainWindow)?.NavigateTo("home");
    }

    private void RestartSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _recallTimer.Stop();
        (App.MainWindow as MainWindow)?.SetFocusMode(false);
        (App.MainWindow as MainWindow)?.ClearTaskbarProgress();
        CompletionPanel.Visibility = Visibility.Collapsed;
        FocusCard.Visibility = Visibility.Collapsed;
        PausedPanel.Visibility = Visibility.Collapsed;
        SessionControlPanel.Visibility = Visibility.Collapsed;
        SessionSetupPanel.Visibility = Visibility.Visible;
    }

    private void RevealMeaning()
    {
        if (_currentWord is null || _revealed || _paused || _sessionEnded)
        {
            return;
        }

        _revealed = true;
        KnowButton.IsEnabled = true;
        LaterButton.IsEnabled = true;
        HiddenPromptText.Visibility = Visibility.Visible;
        MeaningText.Opacity = 0;
        MeaningTransform.TranslateY = 8;
        RevealButtonText.Text = "Revealed";
        PopulateAdditionalDetails(_currentWord, revealed: true);
        StatusText.Text = "Meaning revealed · Rate your recall";
        if (_animationsEnabled)
        {
            RevealStoryboard.Begin();
        }
        else
        {
            HiddenPromptText.Visibility = Visibility.Collapsed;
            HiddenPromptText.Opacity = 0;
            MeaningText.Opacity = 1;
            MeaningTransform.TranslateY = 0;
        }
    }

    private void PopulateAdditionalDetails(WordEntry word, bool revealed)
    {
        var examples = word.ExampleSentences.Where(value => !string.IsNullOrWhiteSpace(value)).Take(2).ToList();
        var related = word.Synonyms.Concat(word.Antonyms).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        var tags = word.Tags?.Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToList() ?? [];
        var hasDetails = examples.Count > 0 || related.Count > 0 || tags.Count > 0 || !string.IsNullOrWhiteSpace(word.Notes);
        AdditionalDetailsExpander.Visibility = revealed && hasDetails ? Visibility.Visible : Visibility.Collapsed;
        ExamplesText.Text = examples.Count == 0 ? "" : $"Example: {string.Join("  ", examples)}";
        ExamplesText.Visibility = examples.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RelatedWordsText.Text = related.Count == 0 ? "" : $"Related: {string.Join(", ", related)}";
        RelatedWordsText.Visibility = related.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var noteParts = new[]
        {
            tags.Count == 0 ? null : $"Tags: {string.Join(", ", tags)}",
            string.IsNullOrWhiteSpace(word.Notes) ? null : word.Notes
        }.Where(value => value is not null);
        NotesText.Text = string.Join(" · ", noteParts!);
        NotesText.Visibility = string.IsNullOrWhiteSpace(NotesText.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string FormatChange(int value) => value > 0 ? $"+{value:N0}" : value.ToString("N0");

    private async void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                if (!_sessionEnded && _currentWord is not null)
                {
                    if (_paused)
                    {
                        ResumeSessionButton_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        PauseSessionButton_Click(this, new RoutedEventArgs());
                    }

                    e.Handled = true;
                }
                break;
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

    private void ApplyOptionsToControls(ReviewSessionOptions options)
    {
        SessionGoalBox.Value = options.Goal;
        SessionModeBox.SelectedIndex = options.DifficultOnly ? 1 : 0;
        TimedSessionToggle.IsOn = options.Timed;
        FocusModeToggle.IsOn = options.FocusMode;
        if (!string.IsNullOrWhiteSpace(options.WordListId))
        {
            SessionWordListBox.SelectedItem = (SessionWordListBox.ItemsSource as IEnumerable<WordListOption>)?.FirstOrDefault(item => item.Id == options.WordListId);
        }
    }

    private sealed record WordListOption(string? Id, string Title);
    private sealed record SessionReviewResult(string WordId, ReviewAction Action);
}
