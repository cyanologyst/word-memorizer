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
    List<string>? Tags)
{
    public List<string> ExampleSentences { get; init; } = [];
    public List<string> Synonyms { get; init; } = [];
    public List<string> Antonyms { get; init; } = [];
    public string? Notes { get; init; }
    public int Difficulty { get; init; } = 3;
    public string? EnrichmentSource { get; init; }
}

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
    public bool GlobalHotkeyEnabled { get; set; } = true;
    public bool ClipboardQuickAddEnabled { get; set; } = false;
    public bool DictionaryLookupEnabled { get; set; } = true;
    public bool SoundEnabled { get; set; } = false;
    public bool CompactNotificationsWhenFullscreen { get; set; } = true;
    public string? VoiceName { get; set; }
    public double SpeechRate { get; set; } = 1.0;
    public int DefaultSessionSize { get; set; } = 20;
    public string LastPageTag { get; set; } = "home";
    public double? PopupLeft { get; set; }
    public double? PopupTop { get; set; }
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
    public double StabilityDays { get; set; } = 1;
    public double MemoryDifficulty { get; set; } = 5;
    public int Lapses { get; set; }
    public int ConsecutiveCorrect { get; set; }
    public double LastResponseSeconds { get; set; }
}

public sealed record ReviewEvent
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string WordListId { get; set; } = "";
    public string WordId { get; set; } = "";
    public string Term { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ReviewAction Action { get; set; }
    public double ResponseSeconds { get; set; }
}

public sealed record ReviewSessionOptions
{
    public int Goal { get; init; } = 20;
    public string? WordListId { get; init; }
    public bool DifficultOnly { get; init; }
    public bool Timed { get; init; }
    public bool FocusMode { get; init; } = true;
}

public sealed record SessionSummary(
    int Total,
    int Known,
    int Later,
    int Skipped,
    TimeSpan Duration)
{
    public double Accuracy => Total == 0 ? 0 : Known * 100.0 / Total;
}

public sealed record WordEnrichment
{
    public string Term { get; init; } = "";
    public string? PartOfSpeech { get; init; }
    public string? Pronunciation { get; init; }
    public string? Definition { get; init; }
    public List<string> Examples { get; init; } = [];
    public List<string> Synonyms { get; init; } = [];
    public List<string> Antonyms { get; init; } = [];
    public string Source { get; init; } = "Offline";
    public DateTimeOffset CachedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record WordListValidationResult(bool IsValid, List<string> Errors, List<string> Warnings)
{
    public static WordListValidationResult Success(List<string>? warnings = null) => new(true, [], warnings ?? []);
    public static WordListValidationResult Failure(params string[] errors) => new(false, [.. errors], []);
}
