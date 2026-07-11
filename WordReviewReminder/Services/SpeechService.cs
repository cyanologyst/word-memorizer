using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace WordReviewReminder.Services;

public sealed class SpeechService
{
    private readonly MediaPlayer _player = new();

    public async Task SpeakAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        using var synthesizer = new SpeechSynthesizer();
        var settings = App.Data.Settings;
        if (!string.IsNullOrWhiteSpace(settings.VoiceName))
        {
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(candidate =>
                string.Equals(candidate.DisplayName, settings.VoiceName, StringComparison.OrdinalIgnoreCase));
            if (voice is not null)
            {
                synthesizer.Voice = voice;
            }
        }

        synthesizer.Options.SpeakingRate = Math.Clamp(settings.SpeechRate, 0.5, 2.0);
        var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
        _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        _player.Play();
    }
}
