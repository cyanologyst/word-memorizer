using System.Text.Json;

namespace WordReviewReminder.Core;

public sealed class LocalDataStore
{
    public LocalDataStore(string rootPath)
    {
        RootPath = rootPath;
        WordListsPath = Path.Combine(rootPath, "wordlists");
        SettingsPath = Path.Combine(rootPath, "settings.json");
        ProgressPath = Path.Combine(rootPath, "progress.json");
        AchievementsPath = Path.Combine(rootPath, "achievements.json");
        LogsPath = Path.Combine(rootPath, "review-log.jsonl");
    }

    public string RootPath { get; }
    public string WordListsPath { get; }
    public string SettingsPath { get; }
    public string ProgressPath { get; }
    public string AchievementsPath { get; }
    public string LogsPath { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(WordListsPath);
    }

    public async Task<IReadOnlyList<WordList>> LoadWordListsAsync(CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        var lists = new List<WordList>();

        foreach (var file in Directory.EnumerateFiles(WordListsPath, "*.wordlist.json").OrderBy(path => path))
        {
            await using var stream = File.OpenRead(file);
            var list = await JsonSerializer.DeserializeAsync<WordList>(stream, JsonOptions.Default, cancellationToken);
            if (WordListValidator.Validate(list).IsValid && list is not null)
            {
                lists.Add(list);
            }
        }

        return lists;
    }

    public async Task ImportWordListAsync(string sourceFile, IReadOnlyList<WordList> existingLists, CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        await using var stream = File.OpenRead(sourceFile);
        var list = await JsonSerializer.DeserializeAsync<WordList>(stream, JsonOptions.Default, cancellationToken);
        var validation = WordListValidator.Validate(list, existingLists);

        if (!validation.IsValid || list is null)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, validation.Errors));
        }

        var destination = Path.Combine(WordListsPath, $"{SanitizeFileName(list.Id)}.wordlist.json");
        await SaveWordListAsync(list, destination, cancellationToken);
    }

    public async Task SaveWordListAsync(WordList list, string? destination = null, CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        destination ??= Path.Combine(WordListsPath, $"{SanitizeFileName(list.Id)}.wordlist.json");
        await using var stream = File.Create(destination);
        await JsonSerializer.SerializeAsync(stream, list, JsonOptions.Default, cancellationToken);
    }

    public async Task<UserSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        if (!File.Exists(SettingsPath))
        {
            return new UserSettings();
        }

        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<UserSettings>(stream, JsonOptions.Default, cancellationToken)
               ?? new UserSettings();
    }

    public async Task SaveSettingsAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        await using var stream = File.Create(SettingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions.Default, cancellationToken);
    }

    public async Task<ReviewProgress> LoadProgressAsync(CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        if (!File.Exists(ProgressPath))
        {
            return new ReviewProgress();
        }

        await using var stream = File.OpenRead(ProgressPath);
        return await JsonSerializer.DeserializeAsync<ReviewProgress>(stream, JsonOptions.Default, cancellationToken)
               ?? new ReviewProgress();
    }

    public async Task SaveProgressAsync(ReviewProgress progress, CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        await using var stream = File.Create(ProgressPath);
        await JsonSerializer.SerializeAsync(stream, progress, JsonOptions.Default, cancellationToken);
    }

    public async Task<AchievementState> LoadAchievementStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        if (!File.Exists(AchievementsPath))
        {
            return new AchievementState();
        }

        await using var stream = File.OpenRead(AchievementsPath);
        return await JsonSerializer.DeserializeAsync<AchievementState>(stream, JsonOptions.Default, cancellationToken)
               ?? new AchievementState();
    }

    public async Task SaveAchievementStateAsync(AchievementState state, CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        await using var stream = File.Create(AchievementsPath);
        await JsonSerializer.SerializeAsync(stream, state, JsonOptions.Default, cancellationToken);
    }

    public async Task SeedWordListsAsync(string seedDirectory, CancellationToken cancellationToken = default)
    {
        EnsureCreated();
        if (!Directory.Exists(seedDirectory))
        {
            return;
        }

        foreach (var source in Directory.EnumerateFiles(seedDirectory, "*.wordlist.json"))
        {
            var destination = Path.Combine(WordListsPath, Path.GetFileName(source));
            if (!File.Exists(destination))
            {
                await using var sourceStream = File.OpenRead(source);
                await using var destinationStream = File.Create(destination);
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
            }
        }
    }

    public static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray());
    }
}
