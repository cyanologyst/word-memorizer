using System.IO.Compression;

namespace WordReviewReminder.Core;

public sealed class BackupService
{
    private readonly LocalDataStore _store;

    public BackupService(LocalDataStore store)
    {
        _store = store;
    }

    public Task CreateAsync(string destinationPath)
    {
        _store.EnsureCreated();
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        ZipFile.CreateFromDirectory(_store.RootPath, destinationPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        return Task.CompletedTask;
    }

    public async Task RestoreAsync(string archivePath)
    {
        var staging = Path.Combine(Path.GetTempPath(), "WordReviewReminderRestore", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    var stagingRoot = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The backup contains an unsafe path.");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            var settingsPath = Path.Combine(staging, "settings.json");
            var wordlistsPath = Path.Combine(staging, "wordlists");
            if (!File.Exists(settingsPath) && !Directory.Exists(wordlistsPath))
            {
                throw new InvalidDataException("This is not a Word Review Reminder backup.");
            }

            _store.EnsureCreated();
            foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, source);
                var destination = Path.Combine(_store.RootPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = File.OpenRead(source);
                await using var output = File.Create(destination);
                await input.CopyToAsync(output);
            }
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }
}
