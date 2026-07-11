using Windows.Media.Core;
using Windows.Media.Playback;

namespace WordReviewReminder.Services;

public static class SoundService
{
    private static readonly MediaPlayer Player = new();

    public static void Play(string cue)
    {
        if (!App.Data.Settings.SoundEnabled)
        {
            return;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", $"{cue}.wav");
        if (!File.Exists(path))
        {
            return;
        }

        Player.Source = MediaSource.CreateFromUri(new Uri(path));
        Player.Play();
    }
}
