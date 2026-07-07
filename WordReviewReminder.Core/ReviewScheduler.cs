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

    public void RecordReview(ReviewProgress progress, WordEntry word, ReviewAction action, DateTimeOffset now)
    {
        if (!progress.Entries.TryGetValue(word.Id, out var entry))
        {
            entry = new ReviewProgressEntry { WordId = word.Id };
            progress.Entries[word.Id] = entry;
        }

        entry.TimesSeen++;
        entry.LastReviewedAt = now;

        switch (action)
        {
            case ReviewAction.Known:
                entry.TimesKnown++;
                entry.DueAt = now.AddDays(Math.Min(30, Math.Max(1, entry.TimesKnown * 2)));
                break;
            case ReviewAction.Later:
                entry.TimesLater++;
                entry.DueAt = now.AddMinutes(10);
                break;
            case ReviewAction.Skipped:
                entry.TimesSkipped++;
                entry.DueAt = now.AddMinutes(5);
                break;
        }
    }
}
