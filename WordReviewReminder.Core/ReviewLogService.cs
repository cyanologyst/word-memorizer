using System.Text.Json;

namespace WordReviewReminder.Core;

public enum ReviewLogSort
{
    Newest,
    Oldest,
    Term
}

public sealed record ReviewLogQuery
{
    public string? Search { get; init; }
    public ReviewAction? Action { get; init; }
    public string? WordListId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public ReviewLogSort Sort { get; init; } = ReviewLogSort.Newest;
    public int Page { get; init; }
    public int PageSize { get; init; } = 100;
}

public sealed record ReviewLogPage(
    IReadOnlyList<ReviewEvent> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 0;
    public bool HasNext => Page + 1 < PageCount;
}

public sealed class ReviewLogService
{
    private readonly string _logPath;

    public ReviewLogService(string logPath)
    {
        _logPath = logPath;
    }

    public async Task AppendAsync(ReviewEvent reviewEvent, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        var json = JsonSerializer.Serialize(reviewEvent, JsonOptions.Compact);
        await File.AppendAllTextAsync(_logPath, json + Environment.NewLine, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewEvent>> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_logPath))
        {
            return [];
        }

        var events = new List<ReviewEvent>();
        await foreach (var line in File.ReadLinesAsync(_logPath, cancellationToken))
        {
            var reviewEvent = TryDeserialize(line);
            if (reviewEvent is not null)
            {
                events.Add(reviewEvent);
            }
        }

        return events;
    }

    public async Task<ReviewLogPage> QueryAsync(ReviewLogQuery query, CancellationToken cancellationToken = default)
    {
        if (query.Page < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query.Page));
        }

        if (query.PageSize is < 1 or > 5000)
        {
            throw new ArgumentOutOfRangeException(nameof(query.PageSize));
        }

        if (!File.Exists(_logPath))
        {
            return new ReviewLogPage([], 0, query.Page, query.PageSize);
        }

        var matched = new List<ReviewEvent>();
        var total = 0;
        var capacity = checked((query.Page + 1) * query.PageSize);
        await foreach (var line in File.ReadLinesAsync(_logPath, cancellationToken))
        {
            var reviewEvent = TryDeserialize(line);
            if (reviewEvent is null || !Matches(reviewEvent, query))
            {
                continue;
            }

            total++;
            matched.Add(reviewEvent);
            if (matched.Count > capacity)
            {
                switch (query.Sort)
                {
                    case ReviewLogSort.Newest:
                        matched.RemoveAt(0);
                        break;
                    case ReviewLogSort.Oldest:
                        matched.RemoveAt(matched.Count - 1);
                        break;
                    case ReviewLogSort.Term:
                        matched.Sort(CompareByTerm);
                        matched.RemoveAt(matched.Count - 1);
                        break;
                }
            }
        }

        IEnumerable<ReviewEvent> ordered = query.Sort switch
        {
            ReviewLogSort.Oldest => matched.OrderBy(item => item.Timestamp),
            ReviewLogSort.Term => matched.OrderBy(item => item.Term, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(item => item.Timestamp),
            _ => matched.OrderByDescending(item => item.Timestamp)
        };
        var pageItems = ordered
            .Skip(query.Page * query.PageSize)
            .Take(query.PageSize)
            .ToList();
        return new ReviewLogPage(pageItems, total, query.Page, query.PageSize);
    }

    public async Task<int> ExportAsync(
        ReviewLogQuery query,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var destination = File.CreateText(destinationPath);
        if (!File.Exists(_logPath))
        {
            return 0;
        }

        var count = 0;
        await foreach (var line in File.ReadLinesAsync(_logPath, cancellationToken))
        {
            var reviewEvent = TryDeserialize(line);
            if (reviewEvent is null || !Matches(reviewEvent, query))
            {
                continue;
            }

            await destination.WriteLineAsync(JsonSerializer.Serialize(reviewEvent, JsonOptions.Compact));
            count++;
        }

        return count;
    }

    private static ReviewEvent? TryDeserialize(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReviewEvent>(line, JsonOptions.Default);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Matches(ReviewEvent reviewEvent, ReviewLogQuery query)
    {
        if (query.Action is not null && reviewEvent.Action != query.Action)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.WordListId) &&
            !string.Equals(reviewEvent.WordListId, query.WordListId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.From is not null && reviewEvent.Timestamp < query.From)
        {
            return false;
        }

        if (query.To is not null && reviewEvent.Timestamp > query.To)
        {
            return false;
        }

        var search = query.Search?.Trim();
        return string.IsNullOrWhiteSpace(search) ||
               reviewEvent.Term.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               reviewEvent.WordId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               reviewEvent.WordListId.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareByTerm(ReviewEvent left, ReviewEvent right)
    {
        var termComparison = StringComparer.CurrentCultureIgnoreCase.Compare(left.Term, right.Term);
        return termComparison != 0 ? termComparison : right.Timestamp.CompareTo(left.Timestamp);
    }
}
