using WordReviewReminder.Core;

namespace WordReviewReminder.Services;

public sealed class AppDataService
{
    private readonly ReviewScheduler _scheduler = new();
    private const int DailyGoal = 20;

    public AppDataService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WordReviewReminder");

        Store = new LocalDataStore(root);
        LogService = new ReviewLogService(Store.LogsPath);
        EnrichmentService = new WordEnrichmentService(Store);
        BackupService = new BackupService(Store);
    }

    public LocalDataStore Store { get; }
    public ReviewLogService LogService { get; }
    public WordEnrichmentService EnrichmentService { get; }
    public BackupService BackupService { get; }
    public UserSettings Settings { get; private set; } = new();
    public ReviewProgress Progress { get; private set; } = new();
    public AchievementState AchievementState { get; private set; } = new();
    public IReadOnlyList<AchievementSnapshot> Achievements { get; private set; } = [];
    public List<WordList> WordLists { get; private set; } = [];
    public IReadOnlyList<ReviewEvent> RecentEvents { get; private set; } = [];
    public DateTimeOffset? PausedUntil { get; private set; }
    public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
    public event EventHandler? SettingsChanged;

    public int UnlockedAchievementCount => Achievements.Count(item => item.IsUnlocked);

    public int TotalWords => WordLists.Where(list => list.IsEnabled).Sum(list => list.Words.Count);

    public int ReviewedToday
    {
        get
        {
            var today = DateTimeOffset.Now.Date;
            return RecentEvents.Count(review => review.Timestamp.ToLocalTime().Date == today);
        }
    }

    public int DailyGoalCount => DailyGoal;

    public double DailyGoalProgress => Math.Min(100, ReviewedToday * 100.0 / DailyGoal);

    public int ReviewStreakDays
    {
        get
        {
            var reviewedDates = RecentEvents
                .Select(review => review.Timestamp.ToLocalTime().Date)
                .Distinct()
                .ToHashSet();

            var streak = 0;
            for (var date = DateTimeOffset.Now.Date; reviewedDates.Contains(date); date = date.AddDays(-1))
            {
                streak++;
            }

            return streak;
        }
    }

    public IReadOnlyList<ActivityDay> GetWeeklyActivity()
    {
        var today = DateTimeOffset.Now.Date;
        return Enumerable.Range(0, 7)
            .Select(offset => today.AddDays(offset - 6))
            .Select(date =>
            {
                var count = RecentEvents.Count(review => review.Timestamp.ToLocalTime().Date == date);
                return new ActivityDay(date.ToString("ddd"), count, Math.Min(100, count * 100.0 / DailyGoal));
            })
            .ToList();
    }

    public IReadOnlyList<WordEntry> AllEnabledWords => WordLists
        .Where(list => list.IsEnabled)
        .SelectMany(list => list.Words)
        .ToList();

    public int DueNowCount => AllEnabledWords.Count(word =>
        !Progress.Entries.TryGetValue(word.Id, out var entry) || entry.DueAt <= DateTimeOffset.UtcNow);

    public MasterySummary GetMasterySummary()
    {
        var words = AllEnabledWords;
        var fresh = words.Count(word => !Progress.Entries.ContainsKey(word.Id));
        var learning = words.Count(word =>
            Progress.Entries.TryGetValue(word.Id, out var entry) &&
            entry.TimesKnown < 2);
        var familiar = words.Count(word =>
            Progress.Entries.TryGetValue(word.Id, out var entry) &&
            entry.TimesKnown >= 2 &&
            entry.TimesKnown < 5);
        var mastered = words.Count(word =>
            Progress.Entries.TryGetValue(word.Id, out var entry) &&
            entry.TimesKnown >= 5);

        return new MasterySummary(fresh, learning, familiar, mastered);
    }

    public IReadOnlyList<MissedWord> GetMostMissedWords(int count = 5)
    {
        var wordsById = AllEnabledWords.ToDictionary(word => word.Id, StringComparer.OrdinalIgnoreCase);
        return Progress.Entries.Values
            .Select(entry =>
            {
                wordsById.TryGetValue(entry.WordId, out var word);
                return new MissedWord(word?.Term ?? entry.WordId, entry.TimesLater + entry.TimesSkipped);
            })
            .Where(item => item.Misses > 0)
            .OrderByDescending(item => item.Misses)
            .ThenBy(item => item.Term)
            .Take(count)
            .ToList();
    }

    public async Task InitializeAsync()
    {
        Store.EnsureCreated();
        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Wordlists");
        await Store.SeedWordListsAsync(seedDirectory);
        Settings = await Store.LoadSettingsAsync();
        Progress = await Store.LoadProgressAsync();
        AchievementState = await Store.LoadAchievementStateAsync();
        await RefreshAsync();
        await EvaluateAchievementsAsync(raiseEvents: false);
    }

    public async Task RefreshAsync()
    {
        WordLists = [.. await Store.LoadWordListsAsync()];
        RecentEvents = (await LogService.ReadAllAsync())
            .OrderByDescending(review => review.Timestamp)
            .Take(2000)
            .ToList();
    }

    public WordEntry? PickNextWord(DateTimeOffset now)
    {
        if (IsPaused(now))
        {
            return null;
        }

        return _scheduler.PickNextWord(WordLists, Progress, Settings, now);
    }

    public WordEntry? PickNextWord(DateTimeOffset now, ReviewSessionOptions options, IReadOnlySet<string>? excludedWordIds = null)
    {
        if (IsPaused(now))
        {
            return null;
        }

        var filtered = WordLists
            .Where(list => string.IsNullOrWhiteSpace(options.WordListId) || list.Id == options.WordListId)
            .Select(list => list with
            {
                Words = list.Words
                    .Where(word => excludedWordIds is null || !excludedWordIds.Contains(word.Id))
                    .Where(word => !options.DifficultOnly ||
                        Progress.Entries.TryGetValue(word.Id, out var progress) &&
                        (progress.Lapses > 0 || progress.TimesLater + progress.TimesSkipped >= 2 || progress.MemoryDifficulty >= 6))
                    .ToList()
            })
            .Where(list => list.Words.Count > 0)
            .ToList();

        return _scheduler.PickNextWord(filtered, Progress, Settings, now);
    }

    public DateTimeOffset? GetNextReminderAt(DateTimeOffset now)
    {
        return _scheduler.GetNextReminderAt(Settings, now);
    }

    public bool IsQuietTime(DateTimeOffset now)
    {
        return IsPaused(now) || _scheduler.IsQuietTime(Settings, now);
    }

    public bool IsPaused(DateTimeOffset now)
    {
        return PausedUntil is not null && PausedUntil > now;
    }

    public void PauseFor(TimeSpan duration)
    {
        PausedUntil = DateTimeOffset.Now.Add(duration);
    }

    public WordList? FindListForWord(WordEntry word)
    {
        return WordLists.FirstOrDefault(list => list.Words.Any(candidate => candidate.Id == word.Id));
    }

    public async Task RecordReviewAsync(WordEntry word, ReviewAction action, double responseSeconds = 0)
    {
        var now = DateTimeOffset.UtcNow;
        _scheduler.RecordReview(Progress, word, action, now, responseSeconds);
        await Store.SaveProgressAsync(Progress);

        var list = FindListForWord(word);
        await LogService.AppendAsync(new ReviewEvent
        {
            Timestamp = now,
            WordListId = list?.Id ?? "",
            WordId = word.Id,
            Term = word.Term,
            Action = action,
            ResponseSeconds = Math.Max(0, responseSeconds)
        });

        await RefreshAsync();
        await EvaluateAchievementsAsync(raiseEvents: true);
    }

    public async Task RecordPronunciationAsync(WordEntry word)
    {
        if (!AchievementState.PronouncedWordIds.Add(word.Id))
        {
            return;
        }

        await EvaluateAchievementsAsync(raiseEvents: true);
    }

    public async Task RecordReviewSessionAsync(
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        IReadOnlyList<ReviewAction> actions)
    {
        if (actions.Count == 0)
        {
            return;
        }

        AchievementState.Sessions.Add(new ReviewSessionRecord
        {
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Actions = [.. actions]
        });

        if (AchievementState.Sessions.Count > 200)
        {
            AchievementState.Sessions.RemoveRange(0, AchievementState.Sessions.Count - 200);
        }

        await EvaluateAchievementsAsync(raiseEvents: true);
    }

    public Task RefreshAchievementsAsync()
    {
        return EvaluateAchievementsAsync(raiseEvents: false);
    }

    private async Task EvaluateAchievementsAsync(bool raiseEvents)
    {
        var allEvents = await LogService.ReadAllAsync();
        var evaluation = AchievementEvaluator.Evaluate(
            AchievementState,
            allEvents,
            Progress,
            WordLists,
            DateTimeOffset.UtcNow);

        Achievements = evaluation.Snapshots;
        await Store.SaveAchievementStateAsync(AchievementState);

        if (!raiseEvents)
        {
            return;
        }

        foreach (var achievement in evaluation.NewlyUnlocked)
        {
            AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs(achievement));
        }
    }

    public async Task SaveSettingsAsync(UserSettings settings)
    {
        Settings = settings;
        await Store.SaveSettingsAsync(settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ImportWordListAsync(string filePath)
    {
        await Store.ImportWordListAsync(filePath, WordLists);
        await RefreshAsync();
        await EvaluateAchievementsAsync(raiseEvents: false);
    }

    public async Task SaveWordListAsync(WordList list)
    {
        await Store.SaveWordListAsync(list);
        await RefreshAsync();
        await EvaluateAchievementsAsync(raiseEvents: false);
    }

    public IReadOnlyList<MistakeWordItem> GetMistakeCandidates(int count = 100)
    {
        var words = AllEnabledWords.ToDictionary(word => word.Id, StringComparer.OrdinalIgnoreCase);
        return Progress.Entries.Values
            .Where(entry => entry.Lapses > 0 || entry.TimesLater + entry.TimesSkipped > 0)
            .Select(entry =>
            {
                words.TryGetValue(entry.WordId, out var word);
                return new MistakeWordItem(
                    word ?? new WordEntry(entry.WordId, entry.WordId, null, null, null, null, null, null),
                    entry.TimesLater + entry.TimesSkipped,
                    entry.Lapses,
                    entry.MemoryDifficulty,
                    entry.DueAt);
            })
            .OrderByDescending(item => item.Lapses)
            .ThenByDescending(item => item.Misses)
            .ThenByDescending(item => item.MemoryDifficulty)
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<CalendarDayItem> GetCalendarActivity(int days = 91)
    {
        var today = DateTimeOffset.Now.Date;
        var counts = RecentEvents
            .GroupBy(item => item.Timestamp.ToLocalTime().Date)
            .ToDictionary(group => group.Key, group => group.Count());
        return Enumerable.Range(0, days)
            .Select(offset => today.AddDays(offset - days + 1))
            .Select(date => new CalendarDayItem(date, counts.GetValueOrDefault(date), Math.Min(4, counts.GetValueOrDefault(date) / 5 + (counts.GetValueOrDefault(date) > 0 ? 1 : 0))))
            .ToList();
    }

    public async Task<WordEntry> AddPersonalWordAsync(string term, WordEnrichment? enrichment = null)
    {
        var list = WordLists.FirstOrDefault(candidate => candidate.Id == "personal-words")
                   ?? new WordList(1, "personal-words", "Personal Words", "en", "Added inside the app", []);
        var normalized = WordNormalizer.NormalizeTerm(term);
        var existing = list.Words.FirstOrDefault(word => WordNormalizer.NormalizeTerm(word.Term) == normalized);
        if (existing is not null)
        {
            return existing;
        }

        var word = new WordEntry(
            $"personal-{Guid.NewGuid():N}",
            term.Trim(),
            enrichment?.PartOfSpeech,
            enrichment?.Pronunciation,
            enrichment?.Definition,
            null,
            list.Words.Count + 1,
            ["Personal"])
        {
            ExampleSentences = enrichment?.Examples ?? [],
            Synonyms = enrichment?.Synonyms ?? [],
            Antonyms = enrichment?.Antonyms ?? [],
            EnrichmentSource = enrichment?.Source
        };
        list.Words.Add(word);
        await SaveWordListAsync(list);
        return word;
    }

    public async Task<WordEntry> EnrichWordAsync(WordEntry word, CancellationToken cancellationToken = default)
    {
        var details = await EnrichmentService.GetAsync(word, Settings.DictionaryLookupEnabled, cancellationToken);
        var enriched = word with
        {
            PartOfSpeech = details.PartOfSpeech ?? word.PartOfSpeech,
            Pronunciation = details.Pronunciation ?? word.Pronunciation,
            ShortMeaning = details.Definition ?? word.ShortMeaning,
            ExampleSentences = details.Examples,
            Synonyms = details.Synonyms,
            Antonyms = details.Antonyms,
            EnrichmentSource = details.Source
        };
        var list = FindListForWord(word);
        if (list is not null)
        {
            var index = list.Words.FindIndex(candidate => candidate.Id == word.Id);
            if (index >= 0)
            {
                list.Words[index] = enriched;
                await SaveWordListAsync(list);
            }
        }

        return enriched;
    }

    public async Task SaveWordNotesAsync(WordEntry word, string? notes)
    {
        var list = FindListForWord(word);
        if (list is null)
        {
            return;
        }

        var index = list.Words.FindIndex(candidate => candidate.Id == word.Id);
        if (index < 0)
        {
            return;
        }

        list.Words[index] = word with { Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim() };
        await SaveWordListAsync(list);
    }

    public async Task SnoozeWordAsync(WordEntry word, TimeSpan duration)
    {
        if (!Progress.Entries.TryGetValue(word.Id, out var entry))
        {
            entry = new ReviewProgressEntry { WordId = word.Id };
            Progress.Entries[word.Id] = entry;
        }

        entry.DueAt = DateTimeOffset.UtcNow.Add(duration);
        await Store.SaveProgressAsync(Progress);
    }

    public async Task SavePopupPositionAsync(double left, double top)
    {
        Settings.PopupLeft = left;
        Settings.PopupTop = top;
        await Store.SaveSettingsAsync(Settings);
    }
}

public sealed record ActivityDay(string DayLabel, int Count, double Progress);
public sealed record MasterySummary(int New, int Learning, int Familiar, int Mastered);
public sealed record MissedWord(string Term, int Misses);
public sealed record MistakeWordItem(WordEntry Word, int Misses, int Lapses, double MemoryDifficulty, DateTimeOffset DueAt)
{
    public string Term => Word.Term;
    public string Meaning => Word.ShortMeaning ?? "";
    public string Urgency => Lapses >= 3 || MemoryDifficulty >= 8 ? "High" : Misses >= 2 ? "Medium" : "Low";
}
public sealed record CalendarDayItem(DateTime Date, int Count, int Level)
{
    public string DayLabel => Date.ToString("ddd, MMM d");
}
