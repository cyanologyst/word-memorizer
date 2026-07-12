namespace WordReviewReminder.Core;

public sealed record MistakeQualification(
    bool Qualifies,
    int Misses,
    int Lapses,
    string Urgency,
    string Reason);

public static class MistakeQualifier
{
    public static MistakeQualification Evaluate(ReviewProgressEntry entry)
    {
        var misses = entry.TimesLater + entry.TimesSkipped;
        var qualifies = entry.Lapses > 0 || misses > 0;
        var urgency = entry.Lapses >= 3 || entry.MemoryDifficulty >= 8
            ? "High"
            : misses >= 2 || entry.Lapses >= 2
                ? "Medium"
                : "Low";
        var reason = entry.Lapses >= 3
            ? "Repeated recall lapses"
            : entry.MemoryDifficulty >= 8
                ? "High memory difficulty"
                : entry.TimesSkipped >= 2
                    ? "Skipped repeatedly"
                    : entry.TimesLater >= 2
                        ? "Deferred repeatedly"
                        : entry.Lapses > 0
                            ? "Recall lapse recorded"
                            : misses > 0
                                ? "Marked for another review"
                                : "No difficulty signal";

        return new MistakeQualification(qualifies, misses, entry.Lapses, urgency, reason);
    }
}
