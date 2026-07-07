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

    private static WordList NewList(params WordEntry[] words)
    {
        return new WordList(1, "test-list", "Test List", "en", null, [.. words]);
    }
}
