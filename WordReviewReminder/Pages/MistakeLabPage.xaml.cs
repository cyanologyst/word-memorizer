using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Render();
    }

    private void UrgencyFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            Render();
        }
    }

    private void Render()
    {
        var filter = (UrgencyFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
        MistakesView.ItemsSource = filter is null or "All" ? _items : _items.Where(item => item.Urgency == filter).ToList();
    }

    private void StartPracticeButton_Click(object sender, RoutedEventArgs e)
    {
        (App.MainWindow as MainWindow)?.StartReviewSession(new ReviewSessionOptions
        {
            Goal = Math.Min(20, Math.Max(5, _items.Count)),
            DifficultOnly = true,
            FocusMode = true
        });
    }
}
