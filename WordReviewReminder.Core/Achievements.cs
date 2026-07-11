namespace WordReviewReminder.Core;

public sealed record AchievementDefinition(
    string Id,
    string Title,
    string Description,
    string Category,
    string IconFileName,
    double TargetValue,
    string Unit);

public sealed record AchievementSnapshot(
    AchievementDefinition Definition,
    double CurrentValue,
    double ProgressPercent,
    bool IsUnlocked,
    DateTimeOffset? UnlockedAt,
    string ProgressLabel,
    string StatusLabel,
    string TierLabel);

public sealed record AchievementEvaluation(
    IReadOnlyList<AchievementSnapshot> Snapshots,
    IReadOnlyList<AchievementSnapshot> NewlyUnlocked);

public sealed record AchievementState
{
    public Dictionary<string, AchievementUnlockRecord> Unlocks { get; set; } = [];
    public HashSet<string> PronouncedWordIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ReviewSessionRecord> Sessions { get; set; } = [];
}

public sealed record AchievementUnlockRecord
{
    public string AchievementId { get; set; } = "";
    public DateTimeOffset UnlockedAt { get; set; }
}

public sealed record ReviewSessionRecord
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public List<ReviewAction> Actions { get; set; } = [];
}

public static class AchievementEvaluator
{
    public static IReadOnlyList<AchievementDefinition> Definitions { get; } =
    [
        new("first-review", "First Step", "Complete your first vocabulary review.", "Milestones", "first-review.png", 1, "review"),
        new("words-10", "Getting Started", "Review 10 different words.", "Vocabulary", "words-10.png", 10, "words"),
        new("words-50", "Word Collector", "Review 50 different words.", "Vocabulary", "words-50.png", 50, "words"),
        new("words-100", "Century Club", "Review 100 different words.", "Vocabulary", "words-100.png", 100, "words"),
        new("words-250", "Vocabulary Builder", "Review 250 different words.", "Vocabulary", "words-250.png", 250, "words"),
        new("words-550", "TOEFL Master", "Review every word in the bundled TOEFL collection.", "Vocabulary", "words-550.png", 550, "words"),
        new("first-known", "Knowledge Keeper", "Mark your first word as known.", "Mastery", "first-known.png", 1, "known"),
        new("known-words", "Mastery Ladder", "Build a collection of 25 known words and keep climbing.", "Mastery", "known-words.png", 25, "known"),
        new("perfect-session", "Flawless Session", "Finish a full session without skipping a word.", "Sessions", "perfect-session.png", 1, "session"),
        new("daily-goal", "Goal Crusher", "Complete 20 reviews in a single day.", "Consistency", "daily-goal.png", 20, "reviews"),
        new("streak-3", "Momentum", "Review on 3 consecutive days.", "Streaks", "streak-3.png", 3, "days"),
        new("streak-7", "Week Warrior", "Review on 7 consecutive days.", "Streaks", "streak-7.png", 7, "days"),
        new("streak-30", "Unstoppable", "Review on 30 consecutive days.", "Streaks", "streak-30.png", 30, "days"),
        new("streak-100", "Enduring Scholar", "Review on 100 consecutive days.", "Streaks", "streak-100.png", 100, "days"),
        new("early-review", "Early Scholar", "Complete 10 reviews before 8:00 AM.", "Habits", "early-review.png", 10, "reviews"),
        new("night-review", "Night Scholar", "Complete 10 reviews after 10:00 PM.", "Habits", "night-review.png", 10, "reviews"),
        new("weekend-review", "Weekend Warrior", "Review on 8 different weekend days.", "Habits", "weekend-review.png", 8, "days"),
        new("speed-review", "Quick Recall", "Finish a 20-word session within 5 minutes.", "Sessions", "speed-review.png", 1, "session"),
        new("marathon-session", "Deep Focus", "Complete a full 20-word review session.", "Sessions", "marathon-session.png", 20, "reviews"),
        new("review-comeback", "Welcome Back", "Return after a break of 14 days or longer.", "Consistency", "review-comeback.png", 1, "return"),
        new("list-complete", "List Conqueror", "Mark every word in one wordlist as known.", "Wordlists", "list-complete.png", 100, "%"),
        new("list-explorer", "Curious Mind", "Review words from 5 different wordlists.", "Wordlists", "list-explorer.png", 5, "lists"),
        new("later-resolved", "Second Chance", "Return to and learn 25 words marked Review Later.", "Mastery", "later-resolved.png", 25, "words"),
        new("pronunciation-pro", "Pronunciation Pro", "Listen to pronunciation for 100 different words.", "Learning tools", "pronunciation-pro.png", 100, "words"),
        new("complete-mastery", "Grand Lexicon", "Master 90% of the words in your enabled lists.", "Mastery", "complete-mastery.png", 90, "%")
    ];

    public static AchievementEvaluation Evaluate(
        AchievementState state,
        IReadOnlyList<ReviewEvent> reviewEvents,
        ReviewProgress progress,
        IReadOnlyList<WordList> wordLists,
        DateTimeOffset now)
    {
        var orderedEvents = reviewEvents.OrderBy(item => item.Timestamp).ToList();
        var uniqueReviewed = orderedEvents.Select(item => item.WordId).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var uniqueKnown = orderedEvents.Where(item => item.Action == ReviewAction.Known).Select(item => item.WordId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var reviewDates = orderedEvents.Select(item => item.Timestamp.ToLocalTime().Date).Distinct().OrderBy(date => date).ToList();
        var longestStreak = GetLongestStreak(reviewDates);
        var dailyBest = orderedEvents.GroupBy(item => item.Timestamp.ToLocalTime().Date).Select(group => group.Count()).DefaultIfEmpty().Max();
        var weekendDays = reviewDates.Count(date => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        var earlyReviews = orderedEvents.Count(item => item.Timestamp.ToLocalTime().Hour < 8);
        var nightReviews = orderedEvents.Count(item => item.Timestamp.ToLocalTime().Hour >= 22);
        var distinctLists = orderedEvents.Select(item => item.WordListId).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var laterResolved = CountLaterResolved(orderedEvents);
        var bestSessionLength = state.Sessions.Select(session => session.Actions.Count).DefaultIfEmpty().Max();
        var hasPerfectSession = state.Sessions.Any(session => session.Actions.Count >= 10 && session.Actions.All(action => action != ReviewAction.Skipped));
        var hasSpeedSession = state.Sessions.Any(session => session.Actions.Count >= 20 && session.CompletedAt - session.StartedAt <= TimeSpan.FromMinutes(5));
        var hasComeback = HasComeback(reviewDates);
        var bestListCompletion = GetBestListCompletion(wordLists, progress);
        var enabledWords = wordLists.Where(list => list.IsEnabled).SelectMany(list => list.Words).ToList();
        var masteredWords = enabledWords.Count(word => progress.Entries.TryGetValue(word.Id, out var entry) && entry.TimesKnown >= 5);
        var masteryPercent = enabledWords.Count == 0 ? 0 : masteredWords * 100.0 / enabledWords.Count;

        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["first-review"] = orderedEvents.Count,
            ["words-10"] = uniqueReviewed,
            ["words-50"] = uniqueReviewed,
            ["words-100"] = uniqueReviewed,
            ["words-250"] = uniqueReviewed,
            ["words-550"] = uniqueReviewed,
            ["first-known"] = uniqueKnown,
            ["known-words"] = uniqueKnown,
            ["perfect-session"] = hasPerfectSession ? 1 : 0,
            ["daily-goal"] = dailyBest,
            ["streak-3"] = longestStreak,
            ["streak-7"] = longestStreak,
            ["streak-30"] = longestStreak,
            ["streak-100"] = longestStreak,
            ["early-review"] = earlyReviews,
            ["night-review"] = nightReviews,
            ["weekend-review"] = weekendDays,
            ["speed-review"] = hasSpeedSession ? 1 : 0,
            ["marathon-session"] = bestSessionLength,
            ["review-comeback"] = hasComeback ? 1 : 0,
            ["list-complete"] = bestListCompletion,
            ["list-explorer"] = distinctLists,
            ["later-resolved"] = laterResolved,
            ["pronunciation-pro"] = state.PronouncedWordIds.Count,
            ["complete-mastery"] = masteryPercent
        };

        var snapshots = new List<AchievementSnapshot>();
        var newlyUnlocked = new List<AchievementSnapshot>();

        foreach (var definition in Definitions)
        {
            var current = values.GetValueOrDefault(definition.Id);
            var reached = current >= definition.TargetValue;
            if (reached && !state.Unlocks.ContainsKey(definition.Id))
            {
                state.Unlocks[definition.Id] = new AchievementUnlockRecord
                {
                    AchievementId = definition.Id,
                    UnlockedAt = now
                };
            }

            state.Unlocks.TryGetValue(definition.Id, out var unlock);
            var snapshot = CreateSnapshot(definition, current, unlock);
            snapshots.Add(snapshot);
            if (reached && unlock?.UnlockedAt == now)
            {
                newlyUnlocked.Add(snapshot);
            }
        }

        return new AchievementEvaluation(snapshots, newlyUnlocked);
    }

    private static AchievementSnapshot CreateSnapshot(AchievementDefinition definition, double current, AchievementUnlockRecord? unlock)
    {
        var percent = Math.Clamp(current * 100.0 / definition.TargetValue, 0, 100);
        var currentDisplay = definition.Unit == "%" ? Math.Round(Math.Min(current, 100)) : Math.Floor(current);
        var targetDisplay = Math.Round(definition.TargetValue);
        var progressLabel = definition.TargetValue == 1
            ? (current >= 1 ? "Complete" : "Not yet unlocked")
            : $"{currentDisplay:N0} of {targetDisplay:N0} {definition.Unit}";

        var tier = definition.Id == "known-words"
            ? current >= 500 ? "Gold" : current >= 100 ? "Silver" : current >= 25 ? "Bronze" : "First tier at 25"
            : "";

        return new AchievementSnapshot(
            definition,
            current,
            percent,
            unlock is not null,
            unlock?.UnlockedAt,
            progressLabel,
            unlock is null ? "In progress" : $"Unlocked {unlock.UnlockedAt.ToLocalTime():MMM d, yyyy}",
            tier);
    }

    private static int GetLongestStreak(IReadOnlyList<DateTime> dates)
    {
        var longest = 0;
        var current = 0;
        DateTime? previous = null;
        foreach (var date in dates)
        {
            current = previous is not null && date == previous.Value.AddDays(1) ? current + 1 : 1;
            longest = Math.Max(longest, current);
            previous = date;
        }

        return longest;
    }

    private static int CountLaterResolved(IReadOnlyList<ReviewEvent> events)
    {
        return events
            .GroupBy(item => item.WordId, StringComparer.OrdinalIgnoreCase)
            .Count(group =>
            {
                var laterSeen = false;
                foreach (var item in group.OrderBy(entry => entry.Timestamp))
                {
                    laterSeen |= item.Action == ReviewAction.Later;
                    if (laterSeen && item.Action == ReviewAction.Known)
                    {
                        return true;
                    }
                }

                return false;
            });
    }

    private static bool HasComeback(IReadOnlyList<DateTime> dates)
    {
        for (var index = 1; index < dates.Count; index++)
        {
            if ((dates[index] - dates[index - 1]).TotalDays >= 14)
            {
                return true;
            }
        }

        return false;
    }

    private static double GetBestListCompletion(IReadOnlyList<WordList> wordLists, ReviewProgress progress)
    {
        return wordLists
            .Where(list => list.Words.Count > 0)
            .Select(list => list.Words.Count(word => progress.Entries.TryGetValue(word.Id, out var entry) && entry.TimesKnown > 0) * 100.0 / list.Words.Count)
            .DefaultIfEmpty()
            .Max();
    }
}

public sealed class AchievementUnlockedEventArgs(AchievementSnapshot achievement) : EventArgs
{
    public AchievementSnapshot Achievement { get; } = achievement;
}
