namespace WordReviewReminder.Services;

public enum AppFeedbackSeverity
{
    Informational,
    Success,
    Warning,
    Error
}

public sealed record AppFeedbackMessage(
    string Title,
    string Message,
    AppFeedbackSeverity Severity,
    string? ActionLabel = null,
    Func<Task>? Action = null,
    TimeSpan? Duration = null);

public sealed class AppFeedbackService
{
    public event EventHandler<AppFeedbackMessage>? MessageRequested;

    public void Show(
        string title,
        string message,
        AppFeedbackSeverity severity = AppFeedbackSeverity.Informational,
        string? actionLabel = null,
        Func<Task>? action = null,
        TimeSpan? duration = null)
    {
        MessageRequested?.Invoke(
            this,
            new AppFeedbackMessage(title, message, severity, actionLabel, action, duration));
    }

    public void Success(string title, string message, string? actionLabel = null, Func<Task>? action = null)
        => Show(title, message, AppFeedbackSeverity.Success, actionLabel, action);

    public void Error(string title, string message)
        => Show(title, message, AppFeedbackSeverity.Error, duration: TimeSpan.FromSeconds(8));
}
