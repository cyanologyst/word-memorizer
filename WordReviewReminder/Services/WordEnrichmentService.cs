using System.Net.Http.Json;
using System.Text.Json.Serialization;
using WordReviewReminder.Core;

namespace WordReviewReminder.Services;

public sealed class WordEnrichmentService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly LocalDataStore _store;
    private Dictionary<string, WordEnrichment>? _cache;

    public WordEnrichmentService(LocalDataStore store)
    {
        _store = store;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WordReviewReminder/1.0");
    }

    public async Task<WordEnrichment> GetAsync(WordEntry word, bool allowOnline, CancellationToken cancellationToken = default)
    {
        var offline = FromWord(word);
        if (HasUsefulDetails(offline))
        {
            return offline;
        }

        _cache ??= await _store.LoadEnrichmentCacheAsync(cancellationToken);
        var key = WordNormalizer.NormalizeTerm(word.Term);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!allowOnline)
        {
            return offline;
        }

        try
        {
            var endpoint = $"https://api.dictionaryapi.dev/api/v2/entries/en/{Uri.EscapeDataString(word.Term)}";
            var entries = await _httpClient.GetFromJsonAsync<List<DictionaryEntry>>(endpoint, cancellationToken);
            var entry = entries?.FirstOrDefault();
            if (entry is null)
            {
                return offline;
            }

            var preferredMeaning = entry.Meanings.FirstOrDefault(item =>
                                       string.Equals(item.PartOfSpeech, word.PartOfSpeech, StringComparison.OrdinalIgnoreCase))
                                   ?? entry.Meanings.FirstOrDefault();
            var definition = preferredMeaning?.Definitions.FirstOrDefault();
            var result = new WordEnrichment
            {
                Term = word.Term,
                PartOfSpeech = preferredMeaning?.PartOfSpeech ?? word.PartOfSpeech,
                Pronunciation = entry.Phonetic ?? entry.Phonetics.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Text))?.Text ?? word.Pronunciation,
                Definition = definition?.DefinitionText ?? word.ShortMeaning,
                Examples = preferredMeaning?.Definitions.Select(item => item.Example).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct().Take(3).ToList() ?? [],
                Synonyms = preferredMeaning?.Synonyms.Concat(preferredMeaning.Definitions.SelectMany(item => item.Synonyms)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList() ?? [],
                Antonyms = preferredMeaning?.Antonyms.Concat(preferredMeaning.Definitions.SelectMany(item => item.Antonyms)).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList() ?? [],
                Source = "Dictionary API",
                CachedAt = DateTimeOffset.UtcNow
            };
            _cache[key] = result;
            await _store.SaveEnrichmentCacheAsync(_cache, cancellationToken);
            return result;
        }
        catch (HttpRequestException)
        {
            return offline;
        }
        catch (TaskCanceledException)
        {
            return offline;
        }
    }

    private static WordEnrichment FromWord(WordEntry word)
    {
        return new WordEnrichment
        {
            Term = word.Term,
            PartOfSpeech = word.PartOfSpeech,
            Pronunciation = word.Pronunciation,
            Definition = word.ShortMeaning,
            Examples = [.. word.ExampleSentences],
            Synonyms = [.. word.Synonyms],
            Antonyms = [.. word.Antonyms],
            Source = word.EnrichmentSource ?? "Wordlist"
        };
    }

    private static bool HasUsefulDetails(WordEnrichment details)
    {
        return details.Examples.Count > 0 || details.Synonyms.Count > 0 || details.Antonyms.Count > 0;
    }

    private sealed record DictionaryEntry(
        string? Phonetic,
        List<DictionaryPhonetic> Phonetics,
        List<DictionaryMeaning> Meanings);

    private sealed record DictionaryPhonetic(string? Text);

    private sealed record DictionaryMeaning(
        string? PartOfSpeech,
        List<DictionaryDefinition> Definitions,
        List<string> Synonyms,
        List<string> Antonyms);

    private sealed record DictionaryDefinition(
        [property: JsonPropertyName("definition")] string? DefinitionText,
        string? Example,
        List<string> Synonyms,
        List<string> Antonyms);
}
