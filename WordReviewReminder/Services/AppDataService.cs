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
    }

    public LocalDataStore Store { get; }
    public ReviewLogService LogService { get; }
    public UserSettings Settings { get; private set; } = new();
    public ReviewProgress Progress { get; private set; } = new();
    public List<WordList> WordLists { get; private set; } = [];
    public IReadOnlyList<ReviewEvent> RecentEvents { get; private set; } = [];

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

    public async Task InitializeAsync()
    {
        Store.EnsureCreated();
        var seedDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Wordlists");
        await Store.SeedWordListsAsync(seedDirectory);
        Settings = await Store.LoadSettingsAsync();
        Progress = await Store.LoadProgressAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        WordLists = [.. await Store.LoadWordListsAsync()];
        RecentEvents = (await LogService.ReadAllAsync())
            .OrderByDescending(review => review.Timestamp)
            .Take(200)
            .ToList();
    }

    public WordEntry? PickNextWord(DateTimeOffset now)
    {
        return _scheduler.PickNextWord(WordLists, Progress, Settings, now);
    }

    public DateTimeOffset? GetNextReminderAt(DateTimeOffset now)
    {
        return _scheduler.GetNextReminderAt(Settings, now);
    }

    public bool IsQuietTime(DateTimeOffset now)
    {
        return _scheduler.IsQuietTime(Settings, now);
    }

    public WordList? FindListForWord(WordEntry word)
    {
        return WordLists.FirstOrDefault(list => list.Words.Any(candidate => candidate.Id == word.Id));
    }

    public async Task RecordReviewAsync(WordEntry word, ReviewAction action)
    {
        var now = DateTimeOffset.UtcNow;
        _scheduler.RecordReview(Progress, word, action, now);
        await Store.SaveProgressAsync(Progress);

        var list = FindListForWord(word);
        await LogService.AppendAsync(new ReviewEvent
        {
            Timestamp = now,
            WordListId = list?.Id ?? "",
            WordId = word.Id,
            Term = word.Term,
            Action = action
        });

        await RefreshAsync();
    }

    public async Task SaveSettingsAsync(UserSettings settings)
    {
        Settings = settings;
        await Store.SaveSettingsAsync(settings);
    }

    public async Task ImportWordListAsync(string filePath)
    {
        await Store.ImportWordListAsync(filePath, WordLists);
        await RefreshAsync();
    }

    public async Task SaveWordListAsync(WordList list)
    {
        await Store.SaveWordListAsync(list);
        await RefreshAsync();
    }
}

public sealed record ActivityDay(string DayLabel, int Count, double Progress);
