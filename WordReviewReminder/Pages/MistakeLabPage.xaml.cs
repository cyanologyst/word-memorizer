using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class MistakeLabPage : Page
{
    private IReadOnlyList<MistakeWordItem> _items = [];

    public MistakeLabPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAsync();
        _items = App.Data.GetMistakeCandidates();
        QueueCountText.Text = _items.Count.ToString("N0");
        HighUrgencyText.Text = _items.Count(item => item.Urgency == "High").ToString("N0");
        UrgencyFilter.SelectedIndex = 0;
        SortBox.SelectedIndex = 0;
        Render();
    }

    private void UrgencyFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Render();
        }
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Render();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (IsLoaded)
        {
            Render();
        }
    }

    private void Render()
    {
        var filter = (UrgencyFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        var query = SearchBox.Text?.Trim() ?? "";
        var visibleItems = (filter is null or "All" ? _items : _items.Where(item => item.Urgency == filter))
            .Where(item => query.Length == 0 ||
                           item.Term.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.Meaning.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           item.Reason.Contains(query, StringComparison.OrdinalIgnoreCase));
        visibleItems = SortBox.SelectedIndex switch
        {
            1 => visibleItems.OrderByDescending(item => item.Misses).ThenBy(item => item.Term),
            2 => visibleItems.OrderByDescending(item => item.Lapses).ThenBy(item => item.Term),
            3 => visibleItems.OrderByDescending(item => item.LastReviewedAt ?? DateTimeOffset.MinValue).ThenBy(item => item.Term),
            4 => visibleItems.OrderBy(item => item.Term),
            _ => visibleItems.OrderBy(item => UrgencyRank(item.Urgency)).ThenByDescending(item => item.Lapses).ThenByDescending(item => item.Misses)
        };
        var renderedItems = visibleItems.ToList();
        MistakesView.ItemsSource = renderedItems;
        MistakesView.Visibility = renderedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyMistakesPanel.Visibility = renderedItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyMistakesTitle.Text = _items.Count == 0 ? "No difficult words here" : $"No {filter?.ToLowerInvariant()} urgency words";
        EmptyMistakesMessage.Text = _items.Count == 0
            ? "Words appear after they are skipped or marked for later review."
            : "Try another urgency filter to see the rest of your practice queue.";
    }

    private async void MistakesView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (MistakesView.SelectedItem is MistakeWordItem item)
        {
            await WordDetailsDialog.ShowAsync(this, item.Word);
        }
    }

    private static int UrgencyRank(string urgency) => urgency switch
    {
        "High" => 0,
        "Medium" => 1,
        _ => 2
    };

    private void StartPracticeButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.StartReviewSession(new ReviewSessionOptions
        {
            Goal = Math.Min(20, Math.Max(1, _items.Count)),
            DifficultOnly = true,
            FocusMode = true
        });
    }
}
