using Microsoft.UI.Xaml;

namespace WordReviewReminder.Services;

public sealed class ClipboardMonitorService
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _lastText = "";

    public event EventHandler<string>? WordDetected;

    public ClipboardMonitorService()
    {
        _timer.Tick += Timer_Tick;
    }

    public void Update(bool enabled)
    {
        if (enabled)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Timer_Tick(object? sender, object e)
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsText())
            {
                return;
            }

            var text = System.Windows.Clipboard.GetText().Trim();
            if (text.Equals(_lastText, StringComparison.Ordinal) || text.Length is < 2 or > 40 || text.Any(char.IsWhiteSpace) || !text.All(character => char.IsLetter(character) || character is '-' or '\''))
            {
                return;
            }

            _lastText = text;
            WordDetected?.Invoke(this, text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard can be temporarily locked by another application.
        }
    }
}
