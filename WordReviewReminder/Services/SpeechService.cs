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
        var stream = await synthesizer.SynthesizeTextToStreamAsync(text);
        _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
        _player.Play();
    }
}
