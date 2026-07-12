namespace WordReviewReminder.Core;

public enum DuplicateImportPolicy
{
    Keep,
    Skip
}

public sealed record WordListImportPlan(
    WordList? PreparedList,
    WordListValidationResult Validation,
    int OriginalWordCount,
    int ImportWordCount,
    int SkippedDuplicateCount)
{
    public bool CanImport => Validation.IsValid && PreparedList is not null && ImportWordCount > 0;
}

public static class WordListImportPlanner
{
    public static WordListImportPlan Create(
        WordList? candidate,
        IReadOnlyList<WordList> existingLists,
        DuplicateImportPolicy duplicatePolicy)
    {
        var validation = WordListValidator.Validate(candidate, existingLists);
        if (candidate is null || !validation.IsValid)
        {
            return new WordListImportPlan(candidate, validation, candidate?.Words.Count ?? 0, 0, 0);
        }

        if (duplicatePolicy == DuplicateImportPolicy.Keep)
        {
            return new WordListImportPlan(candidate, validation, candidate.Words.Count, candidate.Words.Count, 0);
        }

        var normalizedTerms = existingLists
            .SelectMany(list => list.Words)
            .Select(word => WordNormalizer.NormalizeTerm(word.Term))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredWords = new List<WordEntry>();

        foreach (var word in candidate.Words)
        {
            if (normalizedTerms.Add(WordNormalizer.NormalizeTerm(word.Term)))
            {
                filteredWords.Add(word);
            }
        }

        var prepared = candidate with { Words = filteredWords };
        var errors = validation.Errors.ToList();
        if (filteredWords.Count == 0)
        {
            errors.Add("Every word in this file already exists in your library.");
        }

        var preparedValidation = new WordListValidationResult(
            errors.Count == 0,
            errors,
            validation.Warnings);
        return new WordListImportPlan(
            prepared,
            preparedValidation,
            candidate.Words.Count,
            filteredWords.Count,
            candidate.Words.Count - filteredWords.Count);
    }
}
