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
    public void ValidatorRejectsAnImportedListIdThatAlreadyExists()
    {
        var existing = NewList(new WordEntry("old-one", "Halt", null, null, null, null, 1, null));
        var imported = NewList(new WordEntry("new-one", "Begin", null, null, null, null, 1, null));

        var result = WordListValidator.Validate(imported, [existing]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("already exists"));
    }

    [Fact]
    public void ImportPlannerCanSkipDuplicatesAcrossAndWithinLists()
    {
        var existing = NewList(new WordEntry("old-one", "Halt", null, null, null, null, 1, null));
        var imported = new WordList(1, "new-list", "New List", "en", null,
        [
            new WordEntry("one", "Halt", null, null, null, null, 1, null),
            new WordEntry("two", "Begin", null, null, null, null, 2, null),
            new WordEntry("three", " begin ", null, null, null, null, 3, null)
        ]);

        var plan = WordListImportPlanner.Create(imported, [existing], DuplicateImportPolicy.Skip);

        Assert.True(plan.CanImport);
        Assert.Equal(3, plan.OriginalWordCount);
        Assert.Equal(1, plan.ImportWordCount);
        Assert.Equal(2, plan.SkippedDuplicateCount);
        Assert.Equal("Begin", plan.PreparedList!.Words.Single().Term);
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
    public void KnownReviewsGrowStabilityAndScheduleLongerIntervals()
    {
        var scheduler = new ReviewScheduler(seed: 1);
        var progress = new ReviewProgress();
        var word = new WordEntry("word-1", "Dissolve", "verb", null, null, null, 1, null);
        var firstReview = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        scheduler.RecordReview(progress, word, ReviewAction.Known, firstReview, responseSeconds: 4.5);
        var firstStability = progress.Entries[word.Id].StabilityDays;
        var firstDueAt = progress.Entries[word.Id].DueAt;

        scheduler.RecordReview(progress, word, ReviewAction.Known, firstDueAt, responseSeconds: 2.2);
        var entry = progress.Entries[word.Id];

        Assert.True(entry.StabilityDays > firstStability);
        Assert.True(entry.DueAt > firstDueAt.AddDays(firstStability));
        Assert.Equal(2, entry.ConsecutiveCorrect);
        Assert.Equal(2.2, entry.LastResponseSeconds);
    }

    [Fact]
    public void SkippedReviewsIncreaseLapsesAndDifficulty()
    {
        var scheduler = new ReviewScheduler(seed: 1);
        var progress = new ReviewProgress();
        var word = new WordEntry("word-1", "Dissolve", "verb", null, null, null, 1, null);
        var now = new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

        scheduler.RecordReview(progress, word, ReviewAction.Skipped, now);
        var entry = progress.Entries[word.Id];

        Assert.Equal(1, entry.Lapses);
        Assert.True(entry.MemoryDifficulty > 5);
        Assert.Equal(0, entry.ConsecutiveCorrect);
        Assert.Equal(now.AddMinutes(5), entry.DueAt);
    }

    [Fact]
    public void SessionPlannerCapsGoalAndSummarizesDueAndNewWords()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var words = Enumerable.Range(1, 4)
            .Select(index => new WordEntry($"word-{index}", $"Word {index}", null, null, null, null, index, null))
            .ToArray();
        var progress = new ReviewProgress();
        progress.Entries["word-1"] = new ReviewProgressEntry
        {
            WordId = "word-1",
            TimesSeen = 1,
            DueAt = now.AddMinutes(-5)
        };
        progress.Entries["word-2"] = new ReviewProgressEntry
        {
            WordId = "word-2",
            TimesSeen = 1,
            DueAt = now.AddDays(2)
        };

        var plan = ReviewSessionPlanner.Create([NewList(words)], progress, now, requestedGoal: 20);

        Assert.Equal(4, plan.Options.Goal);
        Assert.Equal(4, plan.EligibleCount);
        Assert.Equal(1, plan.DueCount);
        Assert.Equal(2, plan.NewCount);
        Assert.Equal(2, plan.EstimatedMinutes);
    }

    [Fact]
    public void SessionPlannerExplainsWhenNoDifficultWordsQualify()
    {
        var plan = ReviewSessionPlanner.Create(
            [NewList(new WordEntry("word-1", "Dissolve", null, null, null, null, 1, null))],
            new ReviewProgress(),
            DateTimeOffset.UtcNow,
            requestedGoal: 20,
            difficultOnly: true);

        Assert.False(plan.HasEligibleWords);
        Assert.Equal(0, plan.Options.Goal);
        Assert.Contains("difficult-word criteria", plan.Reason);
    }

    [Fact]
    public void SessionFilterRestrictsRetryToIncludedUnreviewedWords()
    {
        var words = new[]
        {
            new WordEntry("one", "One", null, null, null, null, 1, null),
            new WordEntry("two", "Two", null, null, null, null, 2, null),
            new WordEntry("three", "Three", null, null, null, null, 3, null)
        };
        var options = new ReviewSessionOptions { IncludedWordIds = ["one", "three"] };

        var filtered = ReviewSessionFilter.Apply(
            [NewList(words)],
            new ReviewProgress(),
            options,
            new HashSet<string>(["one"], StringComparer.OrdinalIgnoreCase));

        var remaining = Assert.Single(filtered).Words;
        Assert.Single(remaining);
        Assert.Equal("three", remaining[0].Id);
    }

    [Fact]
    public void MistakeQualifierExplainsUrgencyAndQualification()
    {
        var difficult = MistakeQualifier.Evaluate(new ReviewProgressEntry
        {
            WordId = "word-1",
            TimesSkipped = 2,
            Lapses = 1,
            MemoryDifficulty = 8.2
        });
        var clean = MistakeQualifier.Evaluate(new ReviewProgressEntry { WordId = "word-2" });

        Assert.True(difficult.Qualifies);
        Assert.Equal("High", difficult.Urgency);
        Assert.Equal("High memory difficulty", difficult.Reason);
        Assert.False(clean.Qualifies);
        Assert.Equal("No difficulty signal", clean.Reason);
    }

    [Fact]
    public async Task EnrichedWordFieldsRoundTripThroughJsonStorage()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalDataStore(root);
            var word = new WordEntry("word-1", "Dissolve", "verb", "/dɪˈzɑːlv/", "melt", 1, 1, ["TOEFL"])
            {
                ExampleSentences = ["The tablet will dissolve in water."],
                Synonyms = ["melt"],
                Antonyms = ["solidify"],
                Notes = "Review the second meaning.",
                Difficulty = 3,
                EnrichmentSource = "dictionaryapi.dev"
            };

            await store.SaveWordListAsync(NewList(word));
            var loaded = (await store.LoadWordListsAsync()).Single().Words.Single();

            Assert.Equal(word.ExampleSentences, loaded.ExampleSentences);
            Assert.Equal(word.Synonyms, loaded.Synonyms);
            Assert.Equal(word.Antonyms, loaded.Antonyms);
            Assert.Equal(word.Notes, loaded.Notes);
            Assert.Equal(word.Difficulty, loaded.Difficulty);
            Assert.Equal(word.EnrichmentSource, loaded.EnrichmentSource);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LocalStoreDeletesOnlyTheRequestedWordlistFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalDataStore(root);
            var first = NewList(new WordEntry("word-1", "First", null, null, null, null, 1, null));
            var second = new WordList(1, "second-list", "Second", "en", null,
            [
                new WordEntry("word-2", "Second", null, null, null, null, 1, null)
            ]);

            await store.SaveWordListAsync(first);
            await store.SaveWordListAsync(second);
            await store.DeleteWordListAsync(first.Id);

            var remaining = await store.LoadWordListsAsync();
            Assert.Single(remaining);
            Assert.Equal(second.Id, remaining[0].Id);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void LearningAnalyticsRespectsLocalDateRangeAndCalculatesRecall()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test +0330", TimeSpan.FromHours(3.5), "Test", "Test");
        var now = new DateTimeOffset(2026, 7, 13, 20, 0, 0, TimeSpan.Zero);
        var events = new List<ReviewEvent>
        {
            NewReview("one", ReviewAction.Known, new DateTimeOffset(2026, 7, 10, 19, 0, 0, TimeSpan.Zero)),
            NewReview("one", ReviewAction.Later, new DateTimeOffset(2026, 7, 11, 21, 0, 0, TimeSpan.Zero)),
            NewReview("two", ReviewAction.Known, new DateTimeOffset(2026, 7, 12, 22, 0, 0, TimeSpan.Zero)),
            NewReview("three", ReviewAction.Skipped, new DateTimeOffset(2026, 7, 13, 18, 0, 0, TimeSpan.Zero))
        };

        var snapshot = LearningAnalytics.Build(events, [], 3, now, timeZone);

        Assert.Equal(new DateTime(2026, 7, 11), snapshot.StartDate);
        Assert.Equal(new DateTime(2026, 7, 13), snapshot.EndDate);
        Assert.Equal(3, snapshot.TotalReviews);
        Assert.Equal(1, snapshot.Known);
        Assert.Equal(2, snapshot.DifficultResponses);
        Assert.Equal(100.0 / 3, snapshot.RecallRate, 6);
        Assert.Equal(2, snapshot.ActiveDays);
    }

    [Fact]
    public void LearningAnalyticsSeparatesFirstAndRepeatReviewsAndComparesLists()
    {
        var now = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var list = new WordList(1, "list", "Core List", "en", null,
        [
            new WordEntry("one", "One", null, null, null, null, 1, null),
            new WordEntry("two", "Two", null, null, null, null, 2, null)
        ]);
        var events = new List<ReviewEvent>
        {
            NewReview("one", ReviewAction.Known, now.AddDays(-2), "list"),
            NewReview("one", ReviewAction.Known, now.AddDays(-1), "list"),
            NewReview("two", ReviewAction.Later, now, "list")
        };

        var snapshot = LearningAnalytics.Build(events, [list], 7, now, TimeZoneInfo.Utc);

        Assert.Equal(2, snapshot.FirstReviews);
        Assert.Equal(1, snapshot.RepeatReviews);
        Assert.Equal(2, snapshot.UniqueWords);
        var comparison = Assert.Single(snapshot.WordLists);
        Assert.Equal("Core List", comparison.Title);
        Assert.Equal(3, comparison.Reviews);
        Assert.Equal(2, comparison.Known);
        Assert.Equal(2, snapshot.MasteryAtStart.New);
        Assert.Equal(1, snapshot.MasteryAtEnd.Learning);
        Assert.Equal(1, snapshot.MasteryAtEnd.Familiar);
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
    public async Task ReviewLogQueriesCombinedFiltersAndPagesNewestFirst()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new ReviewLogService(Path.Combine(root, "review-log.jsonl"));
            var start = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
            for (var index = 0; index < 12; index++)
            {
                await service.AppendAsync(NewReview(
                    index % 2 == 0 ? $"alpha-{index}" : $"beta-{index}",
                    index % 3 == 0 ? ReviewAction.Later : ReviewAction.Known,
                    start.AddDays(index),
                    index < 8 ? "core" : "extra"));
            }

            var page = await service.QueryAsync(new ReviewLogQuery
            {
                Search = "alpha",
                WordListId = "core",
                From = start.AddDays(2),
                To = start.AddDays(7).AddHours(1),
                PageSize = 2
            });

            Assert.Equal(3, page.TotalCount);
            Assert.Equal(2, page.Items.Count);
            Assert.True(page.HasNext);
            Assert.True(page.Items[0].Timestamp > page.Items[1].Timestamp);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReviewLogExportHonorsTheActiveQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new ReviewLogService(Path.Combine(root, "review-log.jsonl"));
            await service.AppendAsync(NewReview("one", ReviewAction.Known, DateTimeOffset.UtcNow));
            await service.AppendAsync(NewReview("two", ReviewAction.Skipped, DateTimeOffset.UtcNow));
            var destination = Path.Combine(root, "filtered.jsonl");

            var count = await service.ExportAsync(
                new ReviewLogQuery { Action = ReviewAction.Known },
                destination);

            Assert.Equal(1, count);
            Assert.Single(await File.ReadAllLinesAsync(destination));
            Assert.Contains("one", await File.ReadAllTextAsync(destination));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsRoundTripLastVisitedPage()
    {
        var root = Path.Combine(Path.GetTempPath(), "WordReviewReminderTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalDataStore(root);
            var settings = new UserSettings
            {
                LastPageTag = "statistics",
                DictionaryLookupEnabled = false,
                ReminderIntervalMinutes = 45,
                SoundEnabled = true,
                LastSessionGoal = 12,
                LastSessionWordListId = "core",
                LastSessionDifficultOnly = true,
                LastSessionTimed = true,
                LastSessionFocusMode = false
            };

            await store.SaveSettingsAsync(settings);
            var loaded = await store.LoadSettingsAsync();

            Assert.Equal("statistics", loaded.LastPageTag);
            Assert.False(loaded.DictionaryLookupEnabled);
            Assert.Equal(45, loaded.ReminderIntervalMinutes);
            Assert.True(loaded.SoundEnabled);
            Assert.Equal(12, loaded.LastSessionGoal);
            Assert.Equal("core", loaded.LastSessionWordListId);
            Assert.True(loaded.LastSessionDifficultOnly);
            Assert.True(loaded.LastSessionTimed);
            Assert.False(loaded.LastSessionFocusMode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
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

    private static ReviewEvent NewReview(
        string wordId,
        ReviewAction action,
        DateTimeOffset timestamp,
        string wordListId = "test-list")
    {
        return new ReviewEvent
        {
            Timestamp = timestamp,
            WordListId = wordListId,
            WordId = wordId,
            Term = wordId,
            Action = action
        };
    }
}
