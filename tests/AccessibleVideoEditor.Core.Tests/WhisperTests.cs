using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Real offline speech recognition with whisper.cpp. Needs WHISPER_CLI (the whisper-cli program),
/// WHISPER_MODELS (a folder with ggml-base.bin, and ggml-large-v3-turbo-q5_0.bin for Urdu and Hindi) and
/// AVE_SPEECH (a folder with english.wav, hindi.wav and urdu.wav). CI sets AVE_REQUIRE_WHISPER so missing
/// tools fail the build instead of skipping.
/// </summary>
[Collection(MediaCollection.Name)]
public sealed class WhisperTests(MediaFixture media)
{
    private static (WhisperRunner Runner, string Speech)? Setup(WhisperModel model)
    {
        var models = Environment.GetEnvironmentVariable("WHISPER_MODELS");
        var speech = Environment.GetEnvironmentVariable("AVE_SPEECH");
        var runner = models is null ? null : WhisperRunner.Locate(models);
        var ready = runner is not null && speech is not null && runner.HasModel(model);
        if (!ready && Environment.GetEnvironmentVariable("AVE_REQUIRE_WHISPER") == "1")
        {
            Assert.Fail($"Whisper is required: WHISPER_CLI, WHISPER_MODELS with {model.FileName}, and AVE_SPEECH must be set.");
        }

        return ready ? (runner!, speech!) : null;
    }

    [Fact]
    public async Task English_speech_is_transcribed_offline_with_word_times()
    {
        if (Setup(WhisperModel.All[0]) is not var (runner, speech))
        {
            return;
        }

        var file = Path.Combine(speech, "english.wav");
        var progress = new List<double>();
        var transcript = await runner.TranscribeAsync(media.Tools, WhisperModel.All[0], file, "en", progress: new SyncProgress(progress.Add));
        var text = string.Join(" ", transcript.Words.Select(w => w.Text)).ToLowerInvariant();
        foreach (var expected in new[] { "hello", "welcome", "channel", "today", "cook", "rice", "wash", "cold", "water" })
        {
            Assert.Contains(expected, text);
        }

        Assert.Equal("en", transcript.Language);
        Assert.Equal(file, transcript.MediaPath);
        Assert.All(transcript.Words.Zip(transcript.Words.Skip(1)), p => Assert.True(p.First.End <= p.Second.Start + 1e-6));
        Assert.All(transcript.Words, w => Assert.True(w.End > w.Start));
        var today = transcript.Words.First(w => w.Text.StartsWith("Today", StringComparison.OrdinalIgnoreCase));
        Assert.InRange(today.Start, 2.6, 3.2);
        Assert.NotEmpty(progress);
    }

    [Fact]
    public async Task Deleting_a_sentence_from_the_transcript_removes_it_from_the_export()
    {
        if (Setup(WhisperModel.All[0]) is not var (runner, speech))
        {
            return;
        }

        var file = Path.Combine(speech, "english.wav");
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        layer.Items.Add(await media.ItemFor(file, LayerKind.Audio));
        project.Transcripts.Add(await runner.TranscribeAsync(media.Tools, WhisperModel.All[0], file, "en"));

        var document = TranscriptEditing.Build(project, layer);
        var lines = document.Text.Split('\n');
        Assert.Equal(3, lines.Length);
        var edited = lines[0] + "\n" + lines[2];
        var result = TranscriptEditing.Apply(project, layer, document, edited, TranscriptMode.Cut);
        Assert.True(result.Ok, result.Message);

        var (output, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.Wav, OutputPath = media.OutPath("transcript cut.wav") });
        var before = (await media.Probe(file)).Duration;
        var after = (await media.Probe(output)).Duration;
        // The sentence (about 3.4 s) and most of the 1.2 s pause before it go; 0.12 s of pause is kept.
        Assert.InRange(before - after, 3.4, 4.8);

        // Listen to the result with Whisper: the middle sentence is gone, the others are whole.
        var heard = await runner.TranscribeAsync(media.Tools, WhisperModel.All[0], output, "en");
        var text = string.Join(" ", heard.Words.Select(w => w.Text)).ToLowerInvariant();
        Assert.DoesNotContain("cook", text);
        Assert.Contains("hello", text);
        Assert.Contains("channel", text);
        Assert.Contains("wash", text);
        Assert.Contains("water", text);
    }

    [Fact]
    public async Task Hindi_is_written_in_devanagari_and_urdu_in_urdu_script()
    {
        if (Setup(WhisperModel.Recommended) is not var (runner, speech))
        {
            return;
        }

        var hindi = await runner.TranscribeAsync(media.Tools, WhisperModel.Recommended, Path.Combine(speech, "hindi.wav"), "hi");
        var hindiText = string.Join(" ", hindi.Words.Select(w => w.Text));
        Assert.Contains("नमस्ते", hindiText);
        Assert.Contains("चावल", hindiText);
        Assert.DoesNotContain(hindiText, c => c is >= '؀' and <= 'ۿ');

        var urdu = await runner.TranscribeAsync(media.Tools, WhisperModel.Recommended, Path.Combine(speech, "urdu.wav"), "ur");
        var urduText = string.Join(" ", urdu.Words.Select(w => w.Text));
        // Urdu recognition is good but not perfect (in CI one run heard "چنم" for "چینل"), so require most
        // of the key words rather than every one.
        var urduWords = new[] { "السلام", "علیکم", "چینل", "خوش", "چاول", "سیکھیں" };
        var heard = urduWords.Count(w => urduText.Contains(w, StringComparison.Ordinal));
        Assert.True(heard >= 4, $"Heard {heard} of {urduWords.Length} key words: {urduText}");
        Assert.DoesNotContain(urduText, c => c is >= 'ऀ' and <= 'ॿ');
        Assert.Equal("Noto Naskh Arabic", Captions.DefaultStyle(urdu.Language).Font);
    }

    /// <summary>Reports progress on the calling thread (Progress&lt;T&gt; posts later, which tests can miss).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
