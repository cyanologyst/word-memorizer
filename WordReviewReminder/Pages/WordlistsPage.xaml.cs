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
    private List<WordEntry> _visibleWords = [];
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

        CompactToolbarRow.Height = compact ? GridLength.Auto : new GridLength(0);
        SortColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(170);
        FilterColumn.Width = compact ? new GridLength(1, GridUnitType.Star) : new GridLength(150);
        Grid.SetColumnSpan(SearchBox, compact ? 3 : 1);
        Grid.SetRow(SortBox, compact ? 1 : 0);
        Grid.SetColumn(SortBox, compact ? 0 : 1);
        Grid.SetRow(PartOfSpeechFilter, compact ? 1 : 0);
        Grid.SetColumn(PartOfSpeechFilter, compact ? 1 : 2);
        Grid.SetColumnSpan(PartOfSpeechFilter, compact ? 2 : 1);

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
        var listItems = App.Data.WordLists.Select(CreateLibraryItem).ToList();
        WordListView.ItemsSource = listItems;
        if (_selectedList is null && App.Data.WordLists.Count > 0)
        {
            WordListView.SelectedIndex = 0;
        }
        else if (_selectedList is not null)
        {
            WordListView.SelectedItem = listItems.FirstOrDefault(item => item.WordList.Id == _selectedList.Id);
            _selectedList = (WordListView.SelectedItem as LibraryListItem)?.WordList;
        }

        _loading = false;
        RenderSelectedList();
        NoWordlistsPanel.Visibility = App.Data.WordLists.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ContentGrid.IsHitTestVisible = App.Data.WordLists.Count > 0;
        UpdateListActions();
    }

    private void WordListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedList = (WordListView.SelectedItem as LibraryListItem)?.WordList;
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
            ResultCountText.Text = "0 words";
            UpdateSelectionActions();
            UpdateListActions();
            return;
        }

        SelectedListTitle.Text = _selectedList.Title;
        var reviewed = _selectedList.Words.Count(word => App.Data.Progress.Entries.ContainsKey(word.Id));
        var modified = GetLastModified(_selectedList.Id);
        SelectedListMeta.Text = $"{_selectedList.Words.Count:N0} words  ·  {reviewed:N0} reviewed  ·  Updated {modified:MMM d, yyyy}  ·  {_selectedList.Source ?? "Local wordlist"}";
        SchemaStatusText.Text = $"Schema v{_selectedList.SchemaVersion}";
        ReviewProgressText.Text = _selectedList.Words.Count == 0
            ? "No review data"
            : $"{reviewed * 100.0 / _selectedList.Words.Count:0}% reviewed";
        EnableSelectedToggle.IsEnabled = true;
        EnableSelectedToggle.IsOn = _selectedList.IsEnabled;
        UpdatePartOfSpeechFilters();
        RenderWords();
        UpdateListActions();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!IsLoaded)
        {
            return;
        }

        RenderWords();
    }

    private void WordsView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = WordsView.SelectedItems.Cast<WordEntry>().ToList();
        RenderPreview(selected.Count == 1 ? selected[0] : WordsView.SelectedItem as WordEntry);
        UpdateSelectionActions();
    }

    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RenderWords();
        }
    }

    private void PartOfSpeechFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            RenderWords();
        }
    }

    private void RenderWords()
    {
        if (_selectedList is null)
        {
            WordsView.ItemsSource = null;
            _visibleWords = [];
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

        if (PartOfSpeechFilter.SelectedItem is string partOfSpeech && partOfSpeech != "All types")
        {
            words = words.Where(word => string.Equals(
                string.IsNullOrWhiteSpace(word.PartOfSpeech) ? "Unspecified" : word.PartOfSpeech.Trim(),
                partOfSpeech,
                StringComparison.OrdinalIgnoreCase));
        }

        words = SortBox.SelectedIndex switch
        {
            1 => words.OrderBy(word => word.Term, StringComparer.CurrentCultureIgnoreCase),
            2 => words.OrderBy(word => word.PartOfSpeech ?? "", StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(word => word.Term, StringComparer.CurrentCultureIgnoreCase),
            3 => words.OrderByDescending(word => App.Data.Progress.Entries.GetValueOrDefault(word.Id)?.LastReviewedAt)
                .ThenBy(word => word.Term, StringComparer.CurrentCultureIgnoreCase),
            _ => words.OrderBy(word => word.Order ?? int.MaxValue)
        };

        _visibleWords = words.ToList();
        WordsView.ItemsSource = _visibleWords;
        WordsView.Visibility = _visibleWords.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyWordsPanel.Visibility = _visibleWords.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultCountText.Text = _visibleWords.Count == _selectedList.Words.Count
            ? $"{_visibleWords.Count:N0} words"
            : $"{_visibleWords.Count:N0} of {_selectedList.Words.Count:N0} words";
        EmptyWordsTitle.Text = _selectedList.Words.Count == 0 ? "No words in this list" : "No matching words";
        EmptyWordsMessage.Text = _selectedList.Words.Count == 0
            ? "Add a word or import a populated wordlist."
            : "Clear the search or try a broader term.";
        if (WordsView.SelectedItem is not WordEntry selected || !_visibleWords.Any(word => word.Id == selected.Id))
        {
            WordsView.SelectedIndex = _visibleWords.Count > 0 ? 0 : -1;
            RenderPreview(WordsView.SelectedItem as WordEntry);
        }

        UpdateSelectionActions();
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
            PreviewReviewText.Text = "Select a row to inspect it before editing or deleting.";
            return;
        }

        PreviewTermText.Text = word.Term;
        PreviewMetaText.Text = $"{word.PartOfSpeech}  {word.Pronunciation}".Trim();
        PreviewMeaningText.Text = word.ShortMeaning ?? "";
        PreviewChapterText.Text = word.Chapter is null ? "No chapter" : $"Chapter {word.Chapter}";
        PreviewTagsText.Text = word.Tags is null || word.Tags.Count == 0 ? "No tags" : string.Join(", ", word.Tags.Take(3));
        PreviewReviewText.Text = App.Data.Progress.Entries.TryGetValue(word.Id, out var progress)
            ? $"Reviewed {progress.TimesSeen:N0} times · {progress.TimesKnown:N0} known · Last reviewed {progress.LastReviewedAt?.ToLocalTime():MMM d, yyyy}"
            : "Not reviewed yet";
    }

    private async void OpenWordDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSingleSelectedWord() is not WordEntry word)
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
            SetBusy(true, "Validating import...");
            var preparedList = await ConfirmImportAsync(file.Path);
            if (preparedList is null)
            {
                return;
            }

            SetBusy(true, "Importing wordlist...");
            await App.Data.ImportWordListAsync(preparedList);
            _selectedList = preparedList;
            StatusText.Text = $"Imported {preparedList.Words.Count:N0} words";
            await RefreshAsync();
            App.Feedback.Success("Wordlist imported", $"{file.Name} is ready to review.");
        }
        catch (Exception ex)
        {
            App.Feedback.Error("Import failed", ex.Message);
            await ShowMessageAsync("Import failed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<WordList?> ConfirmImportAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var list = await JsonSerializer.DeserializeAsync<WordList>(stream, JsonOptions.Default);
        var initialPlan = WordListImportPlanner.Create(list, App.Data.WordLists, DuplicateImportPolicy.Keep);

        if (!initialPlan.Validation.IsValid || list is null)
        {
            await ShowMessageAsync("This wordlist cannot be imported", string.Join(Environment.NewLine, initialPlan.Validation.Errors));
            return null;
        }

        var summary = new StackPanel { Spacing = 8 };
        summary.Children.Add(new TextBlock { Text = list.Title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        summary.Children.Add(new TextBlock { Text = $"{list.Words.Count:N0} words · Schema v{list.SchemaVersion} · {list.Source ?? "No source supplied"}", TextWrapping = TextWrapping.Wrap });
        var skipDuplicates = new CheckBox
        {
            Content = "Skip duplicate terms already in this library or file",
            IsChecked = initialPlan.Validation.Warnings.Count > 0
        };
        summary.Children.Add(skipDuplicates);
        if (initialPlan.Validation.Warnings.Count > 0)
        {
            summary.Children.Add(new TextBlock { Text = $"Validation found {initialPlan.Validation.Warnings.Count:N0} duplicate warning(s)", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            summary.Children.Add(new TextBlock
            {
                Text = string.Join(Environment.NewLine, initialPlan.Validation.Warnings.Take(6)) + (initialPlan.Validation.Warnings.Count > 6 ? $"{Environment.NewLine}+ {initialPlan.Validation.Warnings.Count - 6} more" : ""),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Import preview",
            Content = new ScrollViewer { Content = summary, MaxHeight = 430 },
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var policy = skipDuplicates.IsChecked == true ? DuplicateImportPolicy.Skip : DuplicateImportPolicy.Keep;
        var plan = WordListImportPlanner.Create(list, App.Data.WordLists, policy);
        if (!plan.CanImport)
        {
            await ShowMessageAsync("Nothing to import", string.Join(Environment.NewLine, plan.Validation.Errors));
            return null;
        }

        if (plan.SkippedDuplicateCount > 0)
        {
            StatusText.Text = $"Skipped {plan.SkippedDuplicateCount:N0} duplicate words";
        }

        return plan.PreparedList;
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
        if (_selectedList is null || GetSingleSelectedWord() is not WordEntry selectedWord)
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
        if (_selectedList is null)
        {
            return;
        }

        var selectedWords = WordsView.SelectedItems.Cast<WordEntry>().ToList();
        if (selectedWords.Count == 0 && WordsView.SelectedItem is WordEntry selectedWord)
        {
            selectedWords.Add(selectedWord);
        }

        if (selectedWords.Count == 0 || !await ConfirmDeleteWordsAsync(selectedWords))
        {
            return;
        }

        var listId = _selectedList.Id;
        var removedWords = selectedWords
            .Select(word => (Index: _selectedList.Words.FindIndex(candidate => candidate.Id == word.Id), Word: word))
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index)
            .ToList();
        var removedIds = removedWords.Select(item => item.Word.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedList.Words.RemoveAll(word => removedIds.Contains(word.Id));
        await App.Data.SaveWordListAsync(_selectedList);
        StatusText.Text = removedWords.Count == 1 ? "Word deleted" : $"{removedWords.Count:N0} words deleted";
        await RefreshAsync();
        App.Feedback.Success(
            removedWords.Count == 1 ? "Word deleted" : "Words deleted",
            removedWords.Count == 1
                ? $"{removedWords[0].Word.Term} was removed. Review history was kept."
                : $"{removedWords.Count:N0} words were removed. Review history was kept.",
            "Undo",
            async () =>
            {
                var list = App.Data.WordLists.FirstOrDefault(candidate => candidate.Id == listId);
                if (list is null)
                {
                    return;
                }

                foreach (var removed in removedWords)
                {
                    if (!list.Words.Any(word => word.Id == removed.Word.Id))
                    {
                        list.Words.Insert(Math.Clamp(removed.Index, 0, list.Words.Count), removed.Word);
                    }
                }

                await App.Data.SaveWordListAsync(list);
                if (IsLoaded)
                {
                    _selectedList = list;
                    await RefreshAsync();
                }

                App.Feedback.Success(
                    removedWords.Count == 1 ? "Word restored" : "Words restored",
                    removedWords.Count == 1
                        ? $"{removedWords[0].Word.Term} is back in {list.Title}."
                        : $"{removedWords.Count:N0} words are back in {list.Title}.");
            });
    }

    private async void ExportListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedList is null)
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedFileName = $"{LocalDataStore.SanitizeFileName(_selectedList.Id)}.wordlist"
        };
        picker.FileTypeChoices.Add("Word Review wordlist", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            SetBusy(true, "Exporting wordlist...");
            await using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            await JsonSerializer.SerializeAsync(stream, _selectedList, JsonOptions.Default);
            StatusText.Text = $"Exported to {file.Name}";
            App.Feedback.Success("Wordlist exported", $"Saved {_selectedList.Words.Count:N0} words to {file.Name}.");
        }
        catch (Exception ex)
        {
            App.Feedback.Error("Export failed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedList is null)
        {
            return;
        }

        if (App.Data.IsBuiltInWordList(_selectedList.Id))
        {
            App.Feedback.Show("Built-in wordlist", "Disable this list to exclude it from reviews. Built-in data cannot be deleted.");
            return;
        }

        var deletedList = _selectedList;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Delete {deletedList.Title}?",
            Content = $"This removes {deletedList.Words.Count:N0} words from your library. Existing review history is kept, and you can undo this action.",
            PrimaryButtonText = "Delete list",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await App.Data.DeleteWordListAsync(deletedList.Id);
        _selectedList = null;
        await RefreshAsync();
        App.Feedback.Success(
            "Wordlist deleted",
            $"{deletedList.Title} was removed. Review history was kept.",
            "Undo",
            async () =>
            {
                await App.Data.SaveWordListAsync(deletedList);
                if (IsLoaded)
                {
                    _selectedList = deletedList;
                    await RefreshAsync();
                }

                App.Feedback.Success("Wordlist restored", $"{deletedList.Title} is back in your library.");
            });
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Refreshing library...");
            await RefreshAsync();
            StatusText.Text = $"Library refreshed · {App.Data.WordLists.Sum(list => list.Words.Count):N0} words";
        }
        catch (Exception ex)
        {
            App.Feedback.Error("Refresh failed", ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
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

    private void UpdatePartOfSpeechFilters()
    {
        var previous = PartOfSpeechFilter.SelectedItem as string ?? "All types";
        var options = new[] { "All types" }
            .Concat((_selectedList?.Words ?? [])
                .Select(word => string.IsNullOrWhiteSpace(word.PartOfSpeech) ? "Unspecified" : word.PartOfSpeech.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase))
            .ToList();
        PartOfSpeechFilter.ItemsSource = options;
        PartOfSpeechFilter.SelectedItem = options.Contains(previous, StringComparer.OrdinalIgnoreCase)
            ? options.First(value => string.Equals(value, previous, StringComparison.OrdinalIgnoreCase))
            : "All types";
    }

    private void UpdateSelectionActions()
    {
        var count = WordsView.SelectedItems.Count;
        EditSelectedButton.IsEnabled = count == 1;
        OpenSelectedButton.IsEnabled = count == 1;
        DeleteSelectedButton.IsEnabled = count > 0;
        DeleteSelectedButton.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (count > 1)
        {
            ResultCountText.Text = $"{count:N0} selected · {_visibleWords.Count:N0} shown";
        }
    }

    private void UpdateListActions()
    {
        ExportListButton.IsEnabled = _selectedList is not null;
        var isBuiltIn = _selectedList is not null && App.Data.IsBuiltInWordList(_selectedList.Id);
        DeleteListButton.IsEnabled = _selectedList is not null && !isBuiltIn;
        ToolTipService.SetToolTip(
            DeleteListButton,
            isBuiltIn
                ? "Built-in wordlists can be disabled, but not deleted"
                : "Delete the selected wordlist");
    }

    private WordEntry? GetSingleSelectedWord()
    {
        return WordsView.SelectedItems.Count == 1 ? WordsView.SelectedItems[0] as WordEntry : null;
    }

    private async Task<bool> ConfirmDeleteWordsAsync(IReadOnlyCollection<WordEntry> words)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = words.Count == 1 ? $"Delete {words.First().Term}?" : $"Delete {words.Count:N0} words?",
            Content = "The words will be removed from this list. Review history is kept, and you can undo this action.",
            PrimaryButtonText = words.Count == 1 ? "Delete word" : "Delete words",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        OperationProgress.IsActive = busy;
        OperationProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        PageCommandBar.IsEnabled = !busy;
        ContentGrid.IsHitTestVisible = !busy && App.Data.WordLists.Count > 0;
        ContentGrid.Opacity = busy ? 0.72 : 1;
        if (!string.IsNullOrWhiteSpace(message))
        {
            StatusText.Text = message;
        }
    }

    private LibraryListItem CreateLibraryItem(WordList list)
    {
        var reviewed = list.Words.Count(word => App.Data.Progress.Entries.ContainsKey(word.Id));
        return new LibraryListItem(
            list,
            list.Title,
            list.Source ?? "Local wordlist",
            $"{list.Words.Count:N0} words",
            list.Words.Count == 0 ? "No progress" : $"{reviewed * 100.0 / list.Words.Count:0}% reviewed");
    }

    private static DateTimeOffset GetLastModified(string id)
    {
        var path = Path.Combine(App.Data.Store.WordListsPath, $"{LocalDataStore.SanitizeFileName(id)}.wordlist.json");
        return File.Exists(path) ? File.GetLastWriteTime(path) : DateTimeOffset.Now;
    }

    private sealed record LibraryListItem(
        WordList WordList,
        string Title,
        string Source,
        string WordCountLabel,
        string ReviewProgressLabel);
}
