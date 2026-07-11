namespace WordReviewReminder.Core;

public sealed class ReviewScheduler
{
    private readonly Random _random;

    public ReviewScheduler(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
    }

    public WordEntry? PickNextWord(IEnumerable<WordList> lists, ReviewProgress progress, UserSettings settings, DateTimeOffset now)
    {
        if (IsQuietTime(settings, now))
        {
            return null;
        }

        var words = lists
            .Where(list => list.IsEnabled)
            .SelectMany(list => list.Words)
            .Where(word => !string.IsNullOrWhiteSpace(word.Term))
            .ToList();

        if (words.Count == 0)
        {
            return null;
        }

        if (settings.SelectionMode == ReviewSelectionMode.Random)
        {
            return words[_random.Next(words.Count)];
        }

        var due = words
            .Select(word => new
            {
                Word = word,
                Progress = progress.Entries.GetValueOrDefault(word.Id)
            })
            .Where(item => item.Progress is null || item.Progress.DueAt <= now)
            .OrderBy(item => item.Progress?.DueAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.Progress?.Lapses ?? 0)
            .ThenByDescending(item => item.Progress?.MemoryDifficulty ?? 5)
            .ThenBy(item => item.Progress?.TimesSeen ?? 0)
            .FirstOrDefault();

        return due?.Word ?? words[_random.Next(words.Count)];
    }

    public DateTimeOffset? GetNextReminderAt(UserSettings settings, DateTimeOffset now)
    {
        if (!IsQuietTime(settings, now))
        {
            return now.AddMinutes(settings.ReminderIntervalMinutes);
        }

        var end = new DateTimeOffset(now.Year, now.Month, now.Day, settings.QuietHoursEnd.Hour, settings.QuietHoursEnd.Minute, 0, now.Offset);
        if (settings.QuietHoursStart > settings.QuietHoursEnd && now.TimeOfDay >= settings.QuietHoursStart.ToTimeSpan())
        {
            end = end.AddDays(1);
        }

        return end;
    }

    public bool IsQuietTime(UserSettings settings, DateTimeOffset now)
    {
        if (!settings.QuietHoursEnabled)
        {
            return false;
        }

        var current = TimeOnly.FromTimeSpan(now.TimeOfDay);
        var start = settings.QuietHoursStart;
        var end = settings.QuietHoursEnd;

        if (start == end)
        {
            return true;
        }

        return start < end
            ? current >= start && current < end
            : current >= start || current < end;
    }

    public void RecordReview(ReviewProgress progress, WordEntry word, ReviewAction action, DateTimeOffset now, double responseSeconds = 0)
    {
        if (!progress.Entries.TryGetValue(word.Id, out var entry))
        {
            entry = new ReviewProgressEntry { WordId = word.Id };
            progress.Entries[word.Id] = entry;
        }

        entry.TimesSeen++;
        entry.LastReviewedAt = now;
        entry.LastResponseSeconds = Math.Max(0, responseSeconds);

        switch (action)
        {
            case ReviewAction.Known:
                entry.TimesKnown++;
                entry.ConsecutiveCorrect++;
                entry.MemoryDifficulty = Math.Clamp(entry.MemoryDifficulty - (responseSeconds is > 0 and < 4 ? 0.35 : 0.18), 1, 10);
                var growth = 1.65 + (10 - entry.MemoryDifficulty) * 0.08 + Math.Min(0.5, entry.ConsecutiveCorrect * 0.05);
                entry.StabilityDays = Math.Clamp(entry.StabilityDays * growth, 1, 365);
                entry.DueAt = now.AddDays(Math.Max(1, Math.Round(entry.StabilityDays, 1)));
                break;
            case ReviewAction.Later:
                entry.TimesLater++;
                entry.ConsecutiveCorrect = 0;
                entry.MemoryDifficulty = Math.Clamp(entry.MemoryDifficulty + 0.45, 1, 10);
                entry.StabilityDays = Math.Max(0.5, entry.StabilityDays * 0.72);
                entry.DueAt = now.AddMinutes(10);
                break;
            case ReviewAction.Skipped:
                entry.TimesSkipped++;
                entry.Lapses++;
                entry.ConsecutiveCorrect = 0;
                entry.MemoryDifficulty = Math.Clamp(entry.MemoryDifficulty + 0.7, 1, 10);
                entry.StabilityDays = Math.Max(0.25, entry.StabilityDays * 0.45);
                entry.DueAt = now.AddMinutes(5);
                break;
        }
    }
}
