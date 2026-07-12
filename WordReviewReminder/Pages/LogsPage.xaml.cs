using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class LogsPage : Page
{
    private readonly CollectionViewSource _groupedLogs = new() { IsSourceGrouped = true };
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private int _page;
    private int _refreshVersion;
    private bool _loaded;

    public LogsPage()
    {
        InitializeComponent();
        _searchTimer.Tick += SearchTimer_Tick;
        Unloaded += (_, _) => _searchTimer.Stop();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAsync();
        WordListFilterBox.ItemsSource = new[] { new WordListFilterItem(null, "All wordlists") }
            .Concat(App.Data.WordLists.Select(list => new WordListFilterItem(list.Id, list.Title)))
            .ToList();
        WordListFilterBox.SelectedIndex = 0;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        SetBusy(true);
        try
        {
            var query = BuildQuery();
            var result = await App.Data.LogService.QueryAsync(query);
            if (version != _refreshVersion)
            {
                return;
            }

            if (_page >= result.PageCount && _page > 0)
            {
                _page = result.PageCount - 1;
                await RefreshAsync();
                return;
            }

            var titles = App.Data.WordLists.ToDictionary(list => list.Id, list => list.Title, StringComparer.OrdinalIgnoreCase);
            var rows = result.Items.Select(review => CreateRow(review, titles)).ToList();
            var groups = rows
                .GroupBy(row => row.DateKey)
                .Select(group => new LogGroup(group.First().DateLabel, group))
                .ToList();
            _groupedLogs.Source = groups;
            LogsView.ItemsSource = _groupedLogs.View;
            LogsView.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyLogsPanel.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyLogsTitle.Text = result.TotalCount == 0 && !HasActiveFilters()
                ? "No review history yet"
                : "No matching review events";
            EmptyLogsMessage.Text = result.TotalCount == 0 && !HasActiveFilters()
                ? "Complete a review and the result will appear here."
                : "Clear one or more filters to broaden the results.";
            StatusText.Text = result.TotalCount == 0
                ? "No events"
                : $"{result.TotalCount:N0} matching events · showing {rows.Count:N0}";
            PageText.Text = $"Page {result.Page + 1:N0} of {result.PageCount:N0}";
            PreviousPageButton.IsEnabled = result.HasPrevious;
            NextPageButton.IsEnabled = result.HasNext;
            ExportButton.IsEnabled = result.TotalCount > 0;
        }
        catch (Exception exception)
        {
            App.Feedback.Error("History could not be loaded", exception.Message);
            StatusText.Text = "History unavailable";
        }
        finally
        {
            if (version == _refreshVersion)
            {
                SetBusy(false);
            }
        }
    }

    private ReviewLogQuery BuildQuery()
    {
        DateTimeOffset? from = FromDatePicker.Date is DateTimeOffset fromDate
            ? StartOfLocalDay(fromDate.Date)
            : null;
        DateTimeOffset? to = ToDatePicker.Date is DateTimeOffset toDate
            ? StartOfLocalDay(toDate.Date).AddDays(1).AddTicks(-1)
            : null;
        return new ReviewLogQuery
        {
            Search = SearchBox.Text,
            Action = ActionFilterBox.SelectedIndex switch
            {
                1 => ReviewAction.Known,
                2 => ReviewAction.Later,
                3 => ReviewAction.Skipped,
                _ => null
            },
            WordListId = (WordListFilterBox.SelectedItem as WordListFilterItem)?.Id,
            From = from,
            To = to,
            Sort = SortBox.SelectedIndex switch
            {
                1 => ReviewLogSort.Oldest,
                2 => ReviewLogSort.Term,
                _ => ReviewLogSort.Newest
            },
            Page = _page,
            PageSize = 100
        };
    }

    private static DateTimeOffset StartOfLocalDay(DateTime date)
    {
        return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_loaded)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void SearchTimer_Tick(object? sender, object e)
    {
        _searchTimer.Stop();
        _page = 0;
        await RefreshAsync();
    }

    private async void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
        {
            _page = 0;
            await RefreshAsync();
        }
    }

    private async void DateFilter_Changed(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_loaded)
        {
            _page = 0;
            await RefreshAsync();
        }
    }

    private async void ClearDatesButton_Click(object sender, RoutedEventArgs e)
    {
        _loaded = false;
        FromDatePicker.Date = null;
        ToDatePicker.Date = null;
        _loaded = true;
        _page = 0;
        await RefreshAsync();
    }

    private async void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page > 0)
        {
            _page--;
            await RefreshAsync();
        }
    }

    private async void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        _page++;
        await RefreshAsync();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"word-review-history-{DateTimeOffset.Now:yyyyMMdd-HHmm}"
        };
        picker.FileTypeChoices.Add("JSON Lines", [".jsonl"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            SetBusy(true);
            var count = await App.Data.LogService.ExportAsync(BuildQuery(), file.Path);
            StatusText.Text = $"Exported {count:N0} events to {file.Name}";
            App.Feedback.Success("History exported", $"Saved {count:N0} matching events to {file.Name}.");
        }
        catch (Exception exception)
        {
            App.Feedback.Error("Export failed", exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await App.Data.RefreshAsync();
        await RefreshAsync();
    }

    private void LogsView_ItemClick(object sender, ItemClickEventArgs e)
    {
        LogsView.SelectedItem = e.ClickedItem;
    }

    private async void LogsView_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e) =>
        await OpenSelectedDetailsAsync();

    private async void OpenDetailsMenuItem_Click(object sender, RoutedEventArgs e) =>
        await OpenSelectedDetailsAsync();

    private async Task OpenSelectedDetailsAsync()
    {
        if (LogsView.SelectedItem is not LogRow row)
        {
            return;
        }

        var word = App.Data.WordLists.SelectMany(list => list.Words)
            .FirstOrDefault(item => string.Equals(item.Id, row.WordId, StringComparison.OrdinalIgnoreCase));
        if (word is not null)
        {
            await WordDetailsDialog.ShowAsync(this, word);
            return;
        }

        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = row.Term,
            Content = $"{row.DateLabel} at {row.Time}\n{row.ActionLabel} · {row.WordListTitle}\nResponse time: {row.ResponseTime}",
            CloseButtonText = "Close"
        }.ShowAsync();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < UiLayout.MediumPageWidth;
        HeaderActionColumn.Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(PageCommandBar, compact ? 1 : 0);
        Grid.SetColumn(PageCommandBar, compact ? 0 : 1);
        PageCommandBar.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        CompactFilterRow.Height = compact ? GridLength.Auto : new GridLength(0);
        DateFilterRow.Height = GridLength.Auto;
        Grid.SetColumnSpan(SearchBox, compact ? 4 : 1);
        Grid.SetRow(ActionFilterBox, compact ? 1 : 0);
        Grid.SetColumn(ActionFilterBox, compact ? 0 : 1);
        Grid.SetRow(WordListFilterBox, compact ? 1 : 0);
        Grid.SetColumn(WordListFilterBox, compact ? 1 : 2);
        Grid.SetColumnSpan(WordListFilterBox, compact ? 2 : 1);
        Grid.SetRow(SortBox, compact ? 1 : 0);
        Grid.SetColumn(SortBox, compact ? 3 : 3);
    }

    private bool HasActiveFilters()
    {
        return !string.IsNullOrWhiteSpace(SearchBox.Text) ||
               ActionFilterBox.SelectedIndex > 0 ||
               WordListFilterBox.SelectedIndex > 0 ||
               FromDatePicker.Date is not null ||
               ToDatePicker.Date is not null;
    }

    private void SetBusy(bool busy)
    {
        LogsProgress.IsActive = busy;
        LogsProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        FilterGrid.IsHitTestVisible = !busy;
        PageCommandBar.IsEnabled = !busy;
        PreviousPageButton.IsEnabled = !busy && PreviousPageButton.IsEnabled;
        NextPageButton.IsEnabled = !busy && NextPageButton.IsEnabled;
    }

    private static LogRow CreateRow(ReviewEvent review, IReadOnlyDictionary<string, string> titles)
    {
        var local = review.Timestamp.ToLocalTime();
        var (label, glyph, brushKey) = review.Action switch
        {
            ReviewAction.Known => ("Known", "\uE73E", "PremiumSuccessBrush"),
            ReviewAction.Later => ("Review Later", "\uE916", "PremiumWarningBrush"),
            _ => ("Skipped", "\uE711", "PremiumDangerBrush")
        };
        return new LogRow(
            review.WordId,
            review.Term,
            local.Date,
            local.ToString("dddd, MMMM d, yyyy"),
            local.ToString("HH:mm"),
            review.ResponseSeconds > 0 ? $"{review.ResponseSeconds:0.0}s response" : "Response not recorded",
            titles.GetValueOrDefault(review.WordListId) ?? review.WordListId,
            label,
            glyph,
            (Brush)Application.Current.Resources[brushKey]);
    }

    private sealed record WordListFilterItem(string? Id, string Title);

    private sealed record LogRow(
        string WordId,
        string Term,
        DateTime DateKey,
        string DateLabel,
        string Time,
        string ResponseTime,
        string WordListTitle,
        string ActionLabel,
        string ActionGlyph,
        Brush ActionBrush);

    private sealed class LogGroup : List<LogRow>
    {
        public LogGroup(string key, IEnumerable<LogRow> items) : base(items) => Key = key;
        public string Key { get; }
    }
}
