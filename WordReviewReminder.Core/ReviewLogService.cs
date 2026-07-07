using System.Text.Json;

namespace WordReviewReminder.Core;

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
        foreach (var line in await File.ReadAllLinesAsync(_logPath, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var reviewEvent = JsonSerializer.Deserialize<ReviewEvent>(line, JsonOptions.Default);
            if (reviewEvent is not null)
            {
                events.Add(reviewEvent);
            }
        }

        return events;
    }
}
