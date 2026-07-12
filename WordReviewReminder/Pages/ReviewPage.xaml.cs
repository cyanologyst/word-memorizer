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
    private ReviewSessionPlan? _recommendedPlan;

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
        SessionWordListBox.SelectedIndex = 0;
        SessionGoalBox.Value = App.Data.Settings.DefaultSessionSize;
        UpdateSessionPlans();

        if (_pendingOptions is not null)
        {
            ApplyOptionsToControls(_pendingOptions);
            await StartSessionAsync(_pendingOptions);
            _pendingOptions = null;
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _recallTimer.Stop();
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
        _reviewedWordIds.Clear();
        _sessionStartedAt = DateTimeOffset.UtcNow;
        SessionSetupPanel.Visibility = Visibility.Collapsed;
        CompletionPanel.Visibility = Visibility.Collapsed;
        FocusCard.Visibility = Visibility.Visible;
        (App.MainWindow as MainWindow)?.SetFocusMode(options.FocusMode);
        await LoadNextAsync();
        Focus(FocusState.Programmatic);
    }

    private async Task LoadNextAsync()
    {
        await App.Data.RefreshAsync();
        _currentWord = App.Data.PickNextWord(DateTimeOffset.Now, _options, _reviewedWordIds);
        if (_currentWord is null && _reviewedWordIds.Count > 0)
        {
            _reviewedWordIds.Clear();
            _currentWord = App.Data.PickNextWord(DateTimeOffset.Now, _options, _reviewedWordIds);
        }

        _revealed = false;
        _wordShownAt = DateTimeOffset.UtcNow;
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
        var sessionComplete = _sessionCount >= _sessionGoal;
        CompletionPanel.Visibility = sessionComplete ? Visibility.Visible : Visibility.Collapsed;
        FocusCard.Visibility = sessionComplete ? Visibility.Collapsed : Visibility.Visible;
        KnowButton.IsEnabled = hasWord && _revealed;
        LaterButton.IsEnabled = hasWord && _revealed;
        SkipButton.IsEnabled = hasWord;
        RevealButton.IsEnabled = hasWord;
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
        StatusText.Text = App.Data.FindListForWord(_currentWord)?.Title ?? "";
    }

    private void RenderSummary()
    {
        _recallTimer.Stop();
        (App.MainWindow as MainWindow)?.SetFocusMode(false);
        (App.MainWindow as MainWindow)?.SetTaskbarProgress(_sessionGoal, _sessionGoal);
        var known = _sessionActions.Count(action => action == ReviewAction.Known);
        var later = _sessionActions.Count(action => action == ReviewAction.Later);
        var skipped = _sessionActions.Count(action => action == ReviewAction.Skipped);
        var duration = DateTimeOffset.UtcNow - _sessionStartedAt;
        var summary = new SessionSummary(_sessionActions.Count, known, later, skipped, duration);
        SessionSummaryText.Text = $"{summary.Accuracy:N0}% recall accuracy in {duration:mm\\:ss}.";
        SessionBreakdownText.Text = $"{known} known   {later} later   {skipped} skipped";
        StatusText.Text = "Session complete";
        SoundService.Play("session-complete");
    }

    private void StartRecallTimer()
    {
        _recallTimer.Stop();
        if (!_options.Timed || _currentWord is null)
        {
            return;
        }

        _secondsRemaining = 15;
        StatusText.Text = $"15 seconds to decide";
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
        if (_currentWord is null || _sessionCount >= _sessionGoal)
        {
            return;
        }

        if (!_revealed && action is ReviewAction.Known or ReviewAction.Later)
        {
            App.Feedback.Show("Reveal the meaning first", "Press Space, then rate how well you remembered the word.");
            return;
        }

        _recallTimer.Stop();
        var responseSeconds = Math.Max(0, (DateTimeOffset.UtcNow - _wordShownAt).TotalSeconds);
        var reviewedWord = _currentWord;
        await App.Data.RecordReviewAsync(reviewedWord, action, responseSeconds);
        _reviewedWordIds.Add(reviewedWord.Id);
        _sessionActions.Add(action);
        _sessionCount = Math.Min(_sessionGoal, _sessionCount + 1);
        SoundService.Play(action == ReviewAction.Known ? "review-known" : "review-later");

        if (_sessionCount == _sessionGoal)
        {
            await App.Data.RecordReviewSessionAsync(_sessionStartedAt, DateTimeOffset.UtcNow, _sessionActions);
            _currentWord = null;
            Render();
            return;
        }

        await LoadNextAsync();
    }

    private void RestartSessionButton_Click(object sender, RoutedEventArgs e)
    {
        _recallTimer.Stop();
        (App.MainWindow as MainWindow)?.SetFocusMode(false);
        (App.MainWindow as MainWindow)?.ClearTaskbarProgress();
        CompletionPanel.Visibility = Visibility.Collapsed;
        FocusCard.Visibility = Visibility.Collapsed;
        SessionSetupPanel.Visibility = Visibility.Visible;
    }

    private void RevealMeaning()
    {
        if (_currentWord is null || _revealed)
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
}
