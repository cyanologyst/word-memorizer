using WordReviewReminder.Core;

namespace WordReviewReminder.Tests;

public sealed class CoreTests
{
    [Fact]
    public void ValidatorRejectsEmptyRequiredFields()
    {
        var list = new WordList(2, "", "", "en", null, []);

        var result = WordListValidator.Validate(list);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("schemaVersion"));
        Assert.Contains(result.Errors, error => error.Contains("id is required"));
        Assert.Contains(result.Errors, error => error.Contains("title is required"));
    }

    [Fact]
    public void ValidatorWarnsAboutDuplicateTerms()
    {
        var list = NewList(
            new WordEntry("one", "Dissolve", "verb", null, null, null, 1, null),
            new WordEntry("two", " dissolve ", "verb", null, null, null, 2, null));

        var result = WordListValidator.Validate(list);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("Duplicate term"));
    }

    [Fact]
    public void ValidatorHandlesDuplicateTermsInExistingLists()
    {
        var existing = NewList(
            new WordEntry("old-one", "Halt", "verb", null, null, null, 1, null),
            new WordEntry("old-two", "halt", "noun", null, null, null, 2, null));
        var imported = new WordList(1, "new-list", "New List", "en", null,
        [
            new WordEntry("new-one", "Halt", "verb", null, null, null, 1, null)
        ]);

        var result = WordListValidator.Validate(imported, [existing]);

        Assert.True(result.IsValid);
        Assert.Contains(result.Warnings, warning => warning.Contains("Already in another list"));
    }

    [Fact]
    public void SchedulerSkipsQuietHoursAcrossMidnight()
    {
        var scheduler = new ReviewScheduler(seed: 1);
        var settings = new UserSettings
        {
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(7, 0)
        };

        var now = new DateTimeOffset(2026, 7, 7, 23, 15, 0, TimeSpan.Zero);

        Assert.True(scheduler.IsQuietTime(settings, now));
        Assert.Equal(new DateTimeOffset(2026, 7, 8, 7, 0, 0, TimeSpan.Zero), scheduler.GetNextReminderAt(settings, now));
    }

    [Fact]
    public void SchedulerRecordsDueDatesByAction()
    {
        var scheduler = new ReviewScheduler(seed: 1);
        var progress = new ReviewProgress();
        var word = new WordEntry("word-1", "Dissolve", "verb", null, null, null, 1, null);
        var now = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        scheduler.RecordReview(progress, word, ReviewAction.Later, now);

        var entry = progress.Entries["word-1"];
        Assert.Equal(1, entry.TimesSeen);
        Assert.Equal(1, entry.TimesLater);
        Assert.Equal(now.AddMinutes(10), entry.DueAt);
    }

    [Fact]
    public async Task ReviewLogRoundTripsEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logPath = Path.Combine(root, "review-log.jsonl");
            var service = new ReviewLogService(logPath);
            var reviewEvent = new ReviewEvent
            {
                Timestamp = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero),
                WordListId = "list",
                WordId = "word",
                Term = "Dissolve",
                Action = ReviewAction.Known
            };

            await service.AppendAsync(reviewEvent);
            var events = await service.ReadAllAsync();

            Assert.Single(events);
            Assert.Equal("Dissolve", events[0].Term);
            Assert.Equal(ReviewAction.Known, events[0].Action);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AchievementsUnlockFromUniqueReviewProgress()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(1, 10)
            .Select(index => NewReviewEvent($"word-{index}", now.AddMinutes(index), ReviewAction.Known))
            .ToList();

        var result = AchievementEvaluator.Evaluate(
            new AchievementState(),
            events,
            new ReviewProgress(),
            [NewList()],
            now.AddHours(1));

        Assert.True(result.Snapshots.Single(item => item.Definition.Id == "first-review").IsUnlocked);
        Assert.True(result.Snapshots.Single(item => item.Definition.Id == "words-10").IsUnlocked);
        Assert.False(result.Snapshots.Single(item => item.Definition.Id == "words-50").IsUnlocked);
    }

    [Fact]
    public void AchievementsUseLongestHistoricalStreak()
    {
        var start = new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 7)
            .Select(index => NewReviewEvent($"word-{index}", start.AddDays(index), ReviewAction.Known))
            .ToList();

        var result = AchievementEvaluator.Evaluate(
            new AchievementState(),
            events,
            new ReviewProgress(),
            [NewList()],
            start.AddDays(8));

        Assert.True(result.Snapshots.Single(item => item.Definition.Id == "streak-7").IsUnlocked);
        Assert.False(result.Snapshots.Single(item => item.Definition.Id == "streak-30").IsUnlocked);
    }

    [Fact]
    public void AchievementsResolveWordsReviewedLater()
    {
        var now = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);
        var events = new List<ReviewEvent>();
        for (var index = 0; index < 25; index++)
        {
            events.Add(NewReviewEvent($"word-{index}", now.AddMinutes(index * 2), ReviewAction.Later));
            events.Add(NewReviewEvent($"word-{index}", now.AddMinutes(index * 2 + 1), ReviewAction.Known));
        }

        var result = AchievementEvaluator.Evaluate(
            new AchievementState(),
            events,
            new ReviewProgress(),
            [NewList()],
            now.AddHours(2));

        Assert.True(result.Snapshots.Single(item => item.Definition.Id == "later-resolved").IsUnlocked);
    }

    [Fact]
    public async Task AchievementStateRoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalDataStore(root);
            var state = new AchievementState();
            state.PronouncedWordIds.Add("word-1");
            state.Unlocks["first-review"] = new AchievementUnlockRecord
            {
                AchievementId = "first-review",
                UnlockedAt = new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero)
            };

            await store.SaveAchievementStateAsync(state);
            var loaded = await store.LoadAchievementStateAsync();

            Assert.Contains("word-1", loaded.PronouncedWordIds);
            Assert.True(loaded.Unlocks.ContainsKey("first-review"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static WordList NewList(params WordEntry[] words)
    {
        return new WordList(1, "test-list", "Test List", "en", null, [.. words]);
    }

    private static ReviewEvent NewReviewEvent(string wordId, DateTimeOffset timestamp, ReviewAction action)
    {
        return new ReviewEvent
        {
            Timestamp = timestamp,
            WordListId = "test-list",
            WordId = wordId,
            Term = wordId,
            Action = action
        };
    }
}
