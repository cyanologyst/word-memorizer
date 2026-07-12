using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;

namespace WordReviewReminder.Services;

public static class WordDetailsDialog
{
    public static async Task<WordEntry> ShowAsync(Page owner, WordEntry word)
    {
        var enriched = await App.Data.EnrichWordAsync(word);
        var speech = new SpeechService();
        var sourceList = App.Data.FindListForWord(enriched);
        App.Data.Progress.Entries.TryGetValue(enriched.Id, out var progress);
        var notesBox = new TextBox
        {
            Header = "Personal notes",
            Text = enriched.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84
        };
        var content = new StackPanel
        {
            Spacing = 14,
            Width = Math.Clamp(owner.ActualWidth - 96, 320, 520)
        };
        content.Children.Add(new TextBlock
        {
            Text = enriched.Term,
            FontSize = 34,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{enriched.PartOfSpeech}  {enriched.Pronunciation}".Trim(),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var listenButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon { Glyph = "\uE767" },
                    new TextBlock { Text = "Listen" }
                }
            }
        };
        AutomationProperties.SetName(listenButton, $"Listen to {enriched.Term}");
        listenButton.Click += async (_, _) =>
        {
            try
            {
                await speech.SpeakAsync(enriched.Term);
                await App.Data.RecordPronunciationAsync(enriched);
            }
            catch (Exception exception)
            {
                App.Feedback.Error("Pronunciation is unavailable", exception.Message);
            }
        };
        content.Children.Add(listenButton);

        AddSection(content, "Meaning", enriched.ShortMeaning);
        AddSection(content, "Examples", string.Join(Environment.NewLine, enriched.ExampleSentences.Select(example => $"- {example}")));
        AddSection(content, "Synonyms", string.Join(", ", enriched.Synonyms));
        AddSection(content, "Antonyms", string.Join(", ", enriched.Antonyms));
        AddSection(content, "Tags", enriched.Tags is null ? null : string.Join(", ", enriched.Tags));
        AddSection(content, "Review history", FormatReviewHistory(progress));
        content.Children.Add(notesBox);
        content.Children.Add(new TextBlock
        {
            Text = sourceList is null
                ? $"Details source: {enriched.EnrichmentSource ?? "Local wordlist"}"
                : $"Wordlist: {sourceList.Title} · {sourceList.Source ?? "Local"} · Details: {enriched.EnrichmentSource ?? "Wordlist"}",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            TextWrapping = TextWrapping.WrapWholeWords
        });

        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            Title = "Word details",
            Content = new ScrollViewer { Content = content, MaxHeight = 520 },
            PrimaryButtonText = "Save notes",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await App.Data.SaveWordNotesAsync(enriched, notesBox.Text);
            enriched = enriched with { Notes = notesBox.Text };
            App.Feedback.Success("Notes saved", $"Your notes for {enriched.Term} were updated.");
        }

        return enriched;
    }

    private static string FormatReviewHistory(ReviewProgressEntry? progress)
    {
        if (progress is null)
        {
            return "Not reviewed yet.";
        }

        var lastReviewed = progress.LastReviewedAt?.ToLocalTime().ToString("MMM d, yyyy 'at' HH:mm") ?? "Not recorded";
        var due = progress.DueAt <= DateTimeOffset.UtcNow
            ? "Due now"
            : $"Due {progress.DueAt.ToLocalTime():MMM d, yyyy}";
        return $"{progress.TimesSeen:N0} seen · {progress.TimesKnown:N0} known · {progress.TimesLater:N0} later · {progress.TimesSkipped:N0} skipped\nLast reviewed: {lastReviewed} · {due}";
    }

    private static void AddSection(Panel parent, string title, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parent.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        parent.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.WrapWholeWords });
    }
}
