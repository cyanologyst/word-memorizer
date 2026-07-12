namespace WordReviewReminder.Core;

public static class WordListValidator
{
    public static WordListValidationResult Validate(WordList? list, IEnumerable<WordList>? existingLists = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (list is null)
        {
            return WordListValidationResult.Failure("The JSON file does not match the wordlist format.");
        }

        if (list.SchemaVersion != 1)
        {
            errors.Add("schemaVersion must be 1.");
        }

        if (string.IsNullOrWhiteSpace(list.Id))
        {
            errors.Add("id is required.");
        }

        if (string.IsNullOrWhiteSpace(list.Title))
        {
            errors.Add("title is required.");
        }

        if (list.Words.Count == 0)
        {
            errors.Add("words must contain at least one word.");
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var terms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in list.Words)
        {
            if (string.IsNullOrWhiteSpace(word.Id))
            {
                errors.Add("Every word must have an id.");
            }
            else if (!ids.Add(word.Id))
            {
                errors.Add($"Duplicate word id: {word.Id}");
            }

            if (string.IsNullOrWhiteSpace(word.Term))
            {
                errors.Add($"Word '{word.Id}' has an empty term.");
                continue;
            }

            var normalized = WordNormalizer.NormalizeTerm(word.Term);
            if (terms.TryGetValue(normalized, out var duplicateTerm))
            {
                warnings.Add($"Duplicate term in this list: {word.Term} matches {duplicateTerm}.");
            }
            else
            {
                terms[normalized] = word.Term;
            }
        }

        if (existingLists is not null)
        {
            if (existingLists.Any(existing =>
                    string.Equals(existing.Id, list.Id, StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"A wordlist with id '{list.Id}' already exists. Change the imported id before trying again.");
            }

            var existingTerms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var existingWord in existingLists.SelectMany(existing => existing.Words))
            {
                var normalized = WordNormalizer.NormalizeTerm(existingWord.Term);
                existingTerms.TryAdd(normalized, existingWord.Term);
            }

            foreach (var word in list.Words)
            {
                var normalized = WordNormalizer.NormalizeTerm(word.Term);
                if (existingTerms.TryGetValue(normalized, out var existingTerm))
                {
                    warnings.Add($"Already in another list: {word.Term} matches {existingTerm}.");
                }
            }
        }

        return new WordListValidationResult(errors.Count == 0, errors, warnings.Distinct().ToList());
    }
}
