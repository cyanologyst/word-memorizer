using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WordReviewReminder.Core;
using WordReviewReminder.Services;

namespace WordReviewReminder.Pages;

public sealed partial class WordlistsPage : Page
{
    private WordList? _selectedList;
    private bool _loading;

    public WordlistsPage()
    {
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < UiLayout.CompactPageWidth;
        var showPreview = e.NewSize.Width >= UiLayout.WidePageWidth;
        PreviewPane.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        PreviewColumn.Width = showPreview ? new GridLength(280) : new GridLength(0);

        HeaderActionColumn.Width = compact ? new GridLength(0) : GridLength.Auto;
        Grid.SetRow(PageCommandBar, compact ? 1 : 0);
        Grid.SetColumn(PageCommandBar, compact ? 0 : 1);
        PageCommandBar.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        ListsColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(240);
        WordsColumn.Width = compact ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ListsRow.Height = compact ? new GridLength(116) : new GridLength(1, GridUnitType.Star);
        WordsRow.Height = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(WordListView, 0);
        Grid.SetColumn(WordListView, 0);
        Grid.SetRow(WordsPane, compact ? 1 : 0);
        Grid.SetColumn(WordsPane, compact ? 0 : 1);
        Grid.SetColumnSpan(WordsPane, compact ? 2 : 1);
    }

    private async Task RefreshAsync()
    {
        _loading = true;
        await App.Data.RefreshAsync();
        WordListView.ItemsSource = App.Data.WordLists;
        if (_selectedList is null && App.Data.WordLists.Count > 0)
        {
            WordListView.SelectedIndex = 0;
        }
        else if (_selectedList is not null)
        {
            WordListView.SelectedItem = App.Data.WordLists.FirstOrDefault(list => list.Id == _selectedList.Id);
        }

        _loading = false;
        RenderSelectedList();
        NoWordlistsPanel.Visibility = App.Data.WordLists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentGrid.IsHitTestVisible = App.Data.WordLists.Count > 0;
    }

    private void WordListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedList = WordListView.SelectedItem as WordList;
        RenderSelectedList();
    }

    private void RenderSelectedList()
    {
        if (_selectedList is null)
        {
            SelectedListTitle.Text = "No wordlist selected";
            SelectedListMeta.Text = "";
            EnableSelectedToggle.IsEnabled = false;
            WordsView.ItemsSource = null;
            RenderPreview(null);
            return;
        }

        SelectedListTitle.Text = _selectedList.Title;
        SelectedListMeta.Text = $"{_selectedList.Words.Count:N0} words - {_selectedList.Source}";
        EnableSelectedToggle.IsEnabled = true;
        EnableSelectedToggle.IsOn = _selectedList.IsEnabled;
        RenderWords();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        RenderWords();
    }

    private void WordsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderPreview(WordsView.SelectedItem as WordEntry);
    }

    private void RenderWords()
    {
        if (_selectedList is null)
        {
            WordsView.ItemsSource = null;
            return;
        }

        var query = SearchBox.Text?.Trim() ?? "";
        var words = _selectedList.Words.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            words = words.Where(word =>
                Contains(word.Term, query) ||
                Contains(word.PartOfSpeech, query) ||
                Contains(word.ShortMeaning, query) ||
                Contains(word.Pronunciation, query));
        }

        var orderedWords = words.OrderBy(word => word.Order ?? int.MaxValue).ToList();
        WordsView.ItemsSource = orderedWords;
        WordsView.Visibility = orderedWords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyWordsPanel.Visibility = orderedWords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyWordsTitle.Text = _selectedList.Words.Count == 0 ? "No words in this list" : "No matching words";
        EmptyWordsMessage.Text = _selectedList.Words.Count == 0
            ? "Add a word or import a populated wordlist."
            : "Clear the search or try a broader term.";
        if (WordsView.SelectedItem is not WordEntry selected || !orderedWords.Any(word => word.Id == selected.Id))
        {
            WordsView.SelectedIndex = orderedWords.Count > 0 ? 0 : -1;
            RenderPreview(WordsView.SelectedItem as WordEntry);
        }
    }

    private void RenderPreview(WordEntry? word)
    {
        if (word is null)
        {
            PreviewTermText.Text = "Select a word";
            PreviewMetaText.Text = "";
            PreviewMeaningText.Text = "Preview meaning, chapter, tags, and pronunciation before editing.";
            PreviewChapterText.Text = "No chapter";
            PreviewTagsText.Text = "No tags";
            return;
        }

        PreviewTermText.Text = word.Term;
        PreviewMetaText.Text = $"{word.PartOfSpeech}  {word.Pronunciation}".Trim();
        PreviewMeaningText.Text = word.ShortMeaning ?? "";
        PreviewChapterText.Text = word.Chapter is null ? "No chapter" : $"Chapter {word.Chapter}";
        PreviewTagsText.Text = word.Tags is null || word.Tags.Count == 0 ? "No tags" : string.Join(", ", word.Tags.Take(3));
    }

    private async void OpenWordDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (WordsView.SelectedItem is not WordEntry word)
        {
            return;
        }

        await WordDetailsDialog.ShowAsync(this, word);
        await RefreshAsync();
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));

        var file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            if (!await ConfirmImportAsync(file.Path))
            {
                return;
            }

            await App.Data.ImportWordListAsync(file.Path);
            StatusText.Text = "Imported";
            await RefreshAsync();
            App.Feedback.Success("Wordlist imported", $"{file.Name} is ready to review.");
        }
        catch (Exception ex)
        {
            App.Feedback.Error("Import failed", ex.Message);
            await ShowMessageAsync("Import failed", ex.Message);
        }
    }

    private async Task<bool> ConfirmImportAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var list = await JsonSerializer.DeserializeAsync<WordList>(stream, JsonOptions.Default);
        var validation = WordListValidator.Validate(list, App.Data.WordLists);

        if (!validation.IsValid || list is null)
        {
            await ShowMessageAsync("Import failed", string.Join(Environment.NewLine, validation.Errors));
            return false;
        }

        var summary = new StackPanel { Spacing = 8 };
        summary.Children.Add(new TextBlock { Text = list.Title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        summary.Children.Add(new TextBlock { Text = $"{list.Words.Count:N0} words - {list.Source}", TextWrapping = TextWrapping.Wrap });
        if (validation.Warnings.Count > 0)
        {
            summary.Children.Add(new TextBlock { Text = "Warnings", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            summary.Children.Add(new TextBlock
            {
                Text = string.Join(Environment.NewLine, validation.Warnings.Take(8)) + (validation.Warnings.Count > 8 ? $"{Environment.NewLine}+ {validation.Warnings.Count - 8} more" : ""),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Import preview",
            Content = summary,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void AddWordButton_Click(object sender, RoutedEventArgs e)
    {
        var word = await ShowWordDialogAsync(null);
        if (word is null)
        {
            return;
        }

        var list = await GetOrCreatePersonalListAsync();
        list.Words.Add(word);
        await App.Data.SaveWordListAsync(list);
        _selectedList = list;
        StatusText.Text = "Word added";
        await RefreshAsync();
        App.Feedback.Success("Word added", $"{word.Term} was added to Personal Words.");
    }

    private async void EditWordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedList is null || WordsView.SelectedItem is not WordEntry selectedWord)
        {
            return;
        }

        var edited = await ShowWordDialogAsync(selectedWord);
        if (edited is null)
        {
            return;
        }

        var index = _selectedList.Words.FindIndex(word => word.Id == selectedWord.Id);
        if (index >= 0)
        {
            _selectedList.Words[index] = edited;
            await App.Data.SaveWordListAsync(_selectedList);
            StatusText.Text = "Word saved";
            await RefreshAsync();
            App.Feedback.Success("Word updated", $"Changes to {edited.Term} were saved.");
        }
    }

    private async void DeleteWordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedList is null || WordsView.SelectedItem is not WordEntry selectedWord)
        {
            return;
        }

        var listId = _selectedList.Id;
        var originalIndex = _selectedList.Words.FindIndex(word => word.Id == selectedWord.Id);
        _selectedList.Words.RemoveAll(word => word.Id == selectedWord.Id);
        await App.Data.SaveWordListAsync(_selectedList);
        StatusText.Text = "Word deleted";
        await RefreshAsync();
        App.Feedback.Success(
            "Word deleted",
            $"{selectedWord.Term} was removed. Review history was kept.",
            "Undo",
            async () =>
            {
                var list = App.Data.WordLists.FirstOrDefault(candidate => candidate.Id == listId);
                if (list is null || list.Words.Any(word => word.Id == selectedWord.Id))
                {
                    return;
                }

                list.Words.Insert(Math.Clamp(originalIndex, 0, list.Words.Count), selectedWord);
                await App.Data.SaveWordListAsync(list);
                if (IsLoaded)
                {
                    _selectedList = list;
                    await RefreshAsync();
                }

                App.Feedback.Success("Word restored", $"{selectedWord.Term} is back in {list.Title}.");
            });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private async void EnableSelectedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _selectedList is null)
        {
            return;
        }

        _selectedList.IsEnabled = EnableSelectedToggle.IsOn;
        await App.Data.SaveWordListAsync(_selectedList);
        StatusText.Text = EnableSelectedToggle.IsOn ? "List enabled" : "List disabled";
        App.Feedback.Show(
            EnableSelectedToggle.IsOn ? "Wordlist enabled" : "Wordlist disabled",
            $"{_selectedList.Title} is {(EnableSelectedToggle.IsOn ? "included in" : "excluded from")} review sessions.");
    }

    private async Task<WordList> GetOrCreatePersonalListAsync()
    {
        const string id = "personal-words";
        var list = App.Data.WordLists.FirstOrDefault(candidate => candidate.Id == id);
        if (list is not null)
        {
            return list;
        }

        list = new WordList(1, id, "Personal Words", "en", "Added inside the app", []);
        await App.Data.SaveWordListAsync(list);
        return list;
    }

    private async Task<WordEntry?> ShowWordDialogAsync(WordEntry? existing)
    {
        var termBox = new TextBox { Header = "Word", Text = existing?.Term ?? "" };
        var partBox = new TextBox { Header = "Part of speech", Text = existing?.PartOfSpeech ?? "" };
        var pronunciationBox = new TextBox { Header = "Pronunciation", Text = existing?.Pronunciation ?? "" };
        var meaningBox = new TextBox { Header = "Short meaning", Text = existing?.ShortMeaning ?? "", TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 90 };
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(termBox);
        stack.Children.Add(partBox);
        stack.Children.Add(pronunciationBox);
        stack.Children.Add(meaningBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = existing is null ? "Add word" : "Edit word",
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = stack
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(termBox.Text))
        {
            return null;
        }

        var id = existing?.Id ?? $"personal-words-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        return new WordEntry(
            id,
            termBox.Text.Trim(),
            EmptyToNull(partBox.Text),
            EmptyToNull(pronunciationBox.Text),
            EmptyToNull(meaningBox.Text),
            existing?.Chapter,
            existing?.Order,
            existing?.Tags ?? ["custom"]);
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK"
        };
        await dialog.ShowAsync();
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool Contains(string? value, string query)
    {
        return value?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;
    }
}
