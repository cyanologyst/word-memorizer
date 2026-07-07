using System.Text.Json.Serialization;

namespace WordReviewReminder.Core;

public enum NotificationMode
{
    Popup,
    Toast,
    Both
}

public enum ReviewAction
{
    Known,
    Later,
    Skipped
}

public enum ReviewSelectionMode
{
    DueFirst,
    Random
}

public sealed record WordList(
    int SchemaVersion,
    string Id,
    string Title,
    string? Language,
    string? Source,
    List<WordEntry> Words)
{
    public bool IsEnabled { get; set; } = true;
}

public sealed record WordEntry(
    string Id,
    string Term,
    string? PartOfSpeech,
    string? Pronunciation,
    string? ShortMeaning,
    int? Chapter,
    int? Order,
    List<string>? Tags);

public sealed record UserSettings
{
    public int ReminderIntervalMinutes { get; set; } = 5;
    public NotificationMode NotificationMode { get; set; } = NotificationMode.Both;
    public int PopupDurationSeconds { get; set; } = 18;
    public bool QuietHoursEnabled { get; set; } = false;
    public TimeOnly QuietHoursStart { get; set; } = new(22, 0);
    public TimeOnly QuietHoursEnd { get; set; } = new(7, 0);
    public bool StartWithWindows { get; set; } = false;
    public ReviewSelectionMode SelectionMode { get; set; } = ReviewSelectionMode.DueFirst;
}

public sealed record ReviewProgress
{
    public Dictionary<string, ReviewProgressEntry> Entries { get; set; } = [];
}

public sealed record ReviewProgressEntry
{
    public string WordId { get; set; } = "";
    public int TimesSeen { get; set; }
    public int TimesKnown { get; set; }
    public int TimesLater { get; set; }
    public int TimesSkipped { get; set; }
    public DateTimeOffset? LastReviewedAt { get; set; }
    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.MinValue;
}

public sealed record ReviewEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string WordListId { get; set; } = "";
    public string WordId { get; set; } = "";
    public string Term { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReviewAction Action { get; set; }
}

public sealed record WordListValidationResult(bool IsValid, List<string> Errors, List<string> Warnings)
{
    public static WordListValidationResult Success(List<string>? warnings = null) => new(true, [], warnings ?? []);
    public static WordListValidationResult Failure(params string[] errors) => new(false, [.. errors], []);
}
