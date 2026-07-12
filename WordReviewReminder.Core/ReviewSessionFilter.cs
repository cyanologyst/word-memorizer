namespace WordReviewReminder.Core;

public static class ReviewSessionFilter
{
    public static IReadOnlyList<WordList> Apply(
        IReadOnlyList<WordList> wordLists,
        ReviewProgress progress,
        ReviewSessionOptions options,
        IReadOnlySet<string>? excludedWordIds = null)
    {
        var includedWordIds = options.IncludedWordIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wordLists
            .Where(list => string.IsNullOrWhiteSpace(options.WordListId) ||
                           string.Equals(list.Id, options.WordListId, StringComparison.OrdinalIgnoreCase))
            .Select(list => list with
            {
                Words = list.Words
                    .Where(word => excludedWordIds is null || !excludedWordIds.Contains(word.Id))
                    .Where(word => includedWordIds is null || includedWordIds.Contains(word.Id))
                    .Where(word => !options.DifficultOnly ||
                        progress.Entries.TryGetValue(word.Id, out var entry) &&
                        (entry.Lapses > 0 || entry.TimesLater + entry.TimesSkipped >= 2 || entry.MemoryDifficulty >= 6))
                    .ToList()
            })
            .Where(list => list.Words.Count > 0)
            .ToList();
    }
}
