namespace WordReviewReminder.Core;

public sealed record ReviewSessionPlan(
    ReviewSessionOptions Options,
    int EligibleCount,
    int DueCount,
    int NewCount,
    int DifficultCount,
    int EstimatedMinutes,
    string Reason)
{
    public bool HasEligibleWords => EligibleCount > 0 && Options.Goal > 0;
}

public static class ReviewSessionPlanner
{
    public static ReviewSessionPlan Create(
        IReadOnlyList<WordList> wordLists,
        ReviewProgress progress,
        DateTimeOffset now,
        int requestedGoal,
        string? wordListId = null,
        bool difficultOnly = false)
    {
        var words = wordLists
            .Where(list => list.IsEnabled &&
                           (string.IsNullOrWhiteSpace(wordListId) ||
                            string.Equals(list.Id, wordListId, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(list => list.Words)
            .Where(word => !string.IsNullOrWhiteSpace(word.Term))
            .GroupBy(word => word.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var newCount = words.Count(word =>
            !progress.Entries.TryGetValue(word.Id, out var entry) || entry.TimesSeen == 0);
        var dueCount = words.Count(word =>
            progress.Entries.TryGetValue(word.Id, out var entry) &&
            entry.TimesSeen > 0 &&
            entry.DueAt <= now);
        var difficultWords = words.Where(word => IsDifficult(word, progress)).ToList();
        var eligibleCount = difficultOnly ? difficultWords.Count : words.Count;
        var goal = eligibleCount == 0
            ? 0
            : Math.Min(Math.Clamp(requestedGoal, 1, 100), eligibleCount);
        var estimatedMinutes = goal == 0 ? 0 : Math.Max(1, (int)Math.Ceiling(goal * 18d / 60d));

        var reason = eligibleCount == 0
            ? difficultOnly
                ? "No words currently meet the difficult-word criteria."
                : "No enabled words match this session."
            : dueCount > 0
                ? $"Starts with {Math.Min(dueCount, goal)} due words, then fills the session with useful practice."
                : newCount > 0
                    ? "Introduces new words while keeping the session short enough to finish."
                    : "Keeps familiar words active with a focused recall session.";

        return new ReviewSessionPlan(
            new ReviewSessionOptions
            {
                Goal = goal,
                WordListId = wordListId,
                DifficultOnly = difficultOnly,
                FocusMode = true
            },
            eligibleCount,
            dueCount,
            newCount,
            difficultWords.Count,
            estimatedMinutes,
            reason);
    }

    private static bool IsDifficult(WordEntry word, ReviewProgress progress)
    {
        return progress.Entries.TryGetValue(word.Id, out var entry) &&
               (entry.Lapses > 0 ||
                entry.TimesLater + entry.TimesSkipped >= 2 ||
                entry.MemoryDifficulty >= 6);
    }
}
