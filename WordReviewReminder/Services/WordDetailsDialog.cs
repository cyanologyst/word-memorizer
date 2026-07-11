using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WordReviewReminder.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WordReviewReminder.Services;

public static class WordDetailsDialog
{
    public static async Task<WordEntry> ShowAsync(Page owner, WordEntry word)
    {
        var enriched = await App.Data.EnrichWordAsync(word);
        var notesBox = new TextBox
        {
            Header = "Personal notes",
            Text = enriched.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 84
        };
        var content = new StackPanel { Spacing = 14, Width = 520 };
        content.Children.Add(new TextBlock
        {
            Text = enriched.Term,
            FontSize = 34,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = $"{enriched.PartOfSpeech}  {enriched.Pronunciation}".Trim(),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 216, 203, 207))
        });
        AddSection(content, "Meaning", enriched.ShortMeaning);
        AddSection(content, "Examples", string.Join(Environment.NewLine, enriched.ExampleSentences.Select(example => $"• {example}")));
        AddSection(content, "Synonyms", string.Join(", ", enriched.Synonyms));
        AddSection(content, "Antonyms", string.Join(", ", enriched.Antonyms));
        content.Children.Add(notesBox);
        content.Children.Add(new TextBlock
        {
            Text = $"Source: {enriched.EnrichmentSource ?? "Wordlist"}",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 174, 162, 166))
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
        }

        return enriched;
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
