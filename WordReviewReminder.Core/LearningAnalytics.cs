namespace WordReviewReminder.Core;

public sealed record AnalyticsDay(
    DateTime Date,
    int Reviews,
    int Known,
    int Later,
    int Skipped)
{
    public double RecallRate => Reviews == 0 ? 0 : Known * 100.0 / Reviews;
    public int DifficultResponses => Later + Skipped;
}

public sealed record AnalyticsTrendPoint(
    DateTime StartDate,
    DateTime EndDate,
    int Reviews,
    int Known,
    int DifficultResponses)
{
    public double RecallRate => Reviews == 0 ? 0 : Known * 100.0 / Reviews;
    public string Label => StartDate == EndDate
        ? StartDate.ToString("MMM d")
        : $"{StartDate:MMM d}-{EndDate:MMM d}";
}

public sealed record WordListAnalytics(
    string WordListId,
    string Title,
    int Reviews,
    int Known,
    int DifficultResponses)
{
    public double RecallRate => Reviews == 0 ? 0 : Known * 100.0 / Reviews;
}

public sealed record DifficultWordAnalytics(string WordId, string Term, int Responses);

public sealed record AnalyticsMasteryState(int New, int Learning, int Familiar, int Mastered);

public sealed record LearningAnalyticsSnapshot(
    DateTime StartDate,
    DateTime EndDate,
    int TotalReviews,
    int Known,
    int Later,
    int Skipped,
    int UniqueWords,
    int FirstReviews,
    int ActiveDays,
    AnalyticsDay? BestDay,
    IReadOnlyList<AnalyticsDay> Days,
    IReadOnlyList<AnalyticsTrendPoint> Trend,
    IReadOnlyList<WordListAnalytics> WordLists,
    IReadOnlyList<DifficultWordAnalytics> DifficultWords,
    AnalyticsMasteryState MasteryAtStart,
    AnalyticsMasteryState MasteryAtEnd)
{
    public double RecallRate => TotalReviews == 0 ? 0 : Known * 100.0 / TotalReviews;
    public int DifficultResponses => Later + Skipped;
    public int RepeatReviews => Math.Max(0, TotalReviews - FirstReviews);
    public int RangeDays => (EndDate - StartDate).Days + 1;
    public double Consistency => RangeDays == 0 ? 0 : ActiveDays * 100.0 / RangeDays;
}

public static class LearningAnalytics
{
    public static LearningAnalyticsSnapshot Build(
        IReadOnlyList<ReviewEvent> allEvents,
        IReadOnlyList<WordList> wordLists,
        int rangeDays,
        DateTimeOffset now,
        TimeZoneInfo? timeZone = null)
    {
        if (rangeDays < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rangeDays));
        }

        timeZone ??= TimeZoneInfo.Local;
        var endDate = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        var startDate = endDate.AddDays(-rangeDays + 1);
        var datedEvents = allEvents
            .Select(review => new DatedReview(review, TimeZoneInfo.ConvertTime(review.Timestamp, timeZone).Date))
            .ToList();
        var rangeEvents = datedEvents
            .Where(item => item.Date >= startDate && item.Date <= endDate)
            .ToList();

        var firstReviewByWord = datedEvents
            .GroupBy(item => item.Event.WordId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Min(item => item.Event.Timestamp),
                StringComparer.OrdinalIgnoreCase);
        var firstReviews = firstReviewByWord.Values.Count(timestamp =>
        {
            var date = TimeZoneInfo.ConvertTime(timestamp, timeZone).Date;
            return date >= startDate && date <= endDate;
        });

        var groupedDays = rangeEvents
            .GroupBy(item => item.Date)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Event).ToList());
        var days = Enumerable.Range(0, rangeDays)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => CreateDay(date, groupedDays.GetValueOrDefault(date) ?? []))
            .ToList();
        var activeDays = days.Where(day => day.Reviews > 0).ToList();
        var bestDay = activeDays
            .OrderByDescending(day => day.Reviews)
            .ThenByDescending(day => day.RecallRate)
            .ThenByDescending(day => day.Date)
            .FirstOrDefault();

        var bucketSize = Math.Max(1, (int)Math.Ceiling(rangeDays / 12.0));
        var trend = days
            .Select((day, index) => (day, index))
            .GroupBy(item => item.index / bucketSize)
            .Select(group => new AnalyticsTrendPoint(
                group.First().day.Date,
                group.Last().day.Date,
                group.Sum(item => item.day.Reviews),
                group.Sum(item => item.day.Known),
                group.Sum(item => item.day.DifficultResponses)))
            .ToList();

        var titles = wordLists.ToDictionary(list => list.Id, list => list.Title, StringComparer.OrdinalIgnoreCase);
        var listAnalytics = rangeEvents
            .GroupBy(item => item.Event.WordListId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new WordListAnalytics(
                group.Key,
                titles.GetValueOrDefault(group.Key) ?? group.Key,
                group.Count(),
                group.Count(item => item.Event.Action == ReviewAction.Known),
                group.Count(item => item.Event.Action is ReviewAction.Later or ReviewAction.Skipped)))
            .OrderByDescending(item => item.Reviews)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var difficultWords = rangeEvents
            .Where(item => item.Event.Action is ReviewAction.Later or ReviewAction.Skipped)
            .GroupBy(item => item.Event.WordId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DifficultWordAnalytics(
                group.Key,
                group.Last().Event.Term,
                group.Count()))
            .OrderByDescending(item => item.Responses)
            .ThenBy(item => item.Term, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var wordIds = wordLists
            .SelectMany(list => list.Words)
            .Select(word => word.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var masteryAtStart = BuildMasteryState(datedEvents, wordIds, startDate.AddDays(-1));
        var masteryAtEnd = BuildMasteryState(datedEvents, wordIds, endDate);

        return new LearningAnalyticsSnapshot(
            startDate,
            endDate,
            rangeEvents.Count,
            rangeEvents.Count(item => item.Event.Action == ReviewAction.Known),
            rangeEvents.Count(item => item.Event.Action == ReviewAction.Later),
            rangeEvents.Count(item => item.Event.Action == ReviewAction.Skipped),
            rangeEvents.Select(item => item.Event.WordId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            firstReviews,
            activeDays.Count,
            bestDay,
            days,
            trend,
            listAnalytics,
            difficultWords,
            masteryAtStart,
            masteryAtEnd);
    }

    private static AnalyticsDay CreateDay(DateTime date, IReadOnlyList<ReviewEvent> events)
    {
        return new AnalyticsDay(
            date,
            events.Count,
            events.Count(item => item.Action == ReviewAction.Known),
            events.Count(item => item.Action == ReviewAction.Later),
            events.Count(item => item.Action == ReviewAction.Skipped));
    }

    private static AnalyticsMasteryState BuildMasteryState(
        IReadOnlyList<DatedReview> events,
        IReadOnlySet<string> wordIds,
        DateTime throughDate)
    {
        var reviewed = events
            .Where(item => item.Date <= throughDate && wordIds.Contains(item.Event.WordId))
            .GroupBy(item => item.Event.WordId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(item => item.Event.Action == ReviewAction.Known),
                StringComparer.OrdinalIgnoreCase);
        return new AnalyticsMasteryState(
            wordIds.Count - reviewed.Count,
            reviewed.Count(item => item.Value < 2),
            reviewed.Count(item => item.Value is >= 2 and < 5),
            reviewed.Count(item => item.Value >= 5));
    }

    private sealed record DatedReview(ReviewEvent Event, DateTime Date);
}
