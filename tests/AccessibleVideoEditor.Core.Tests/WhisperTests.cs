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

    [Fact]
    public async Task Skipping_silence_hears_speech_after_a_long_gap_at_the_right_time()
    {
        if (Setup(WhisperModel.All[0]) is not var (runner, speech))
        {
            return;
        }

        Assert.True(runner.VadModelPath is not null || Environment.GetEnvironmentVariable("AVE_REQUIRE_WHISPER") != "1",
            "The Silero voice activity model is required (WHISPER_VAD_MODEL or next to whisper-cli).");
        if (runner.VadModelPath is null)
        {
            return;
        }

        // The English sample, 25 seconds of silence, then the sample again.
        var english = Path.Combine(speech, "english.wav");
        var gap = await media.Generate("speech with gap.wav", "-i", english, "-f", "lavfi", "-t", "25", "-i", "anullsrc=r=22050:cl=mono", "-i", english,
            "-filter_complex", "[0][1][2]concat=n=3:v=0:a=1", "-ac", "1");
        var firstLength = (await media.Probe(english)).Duration;
        var transcript = await runner.TranscribeAsync(media.Tools, WhisperModel.All[0], gap, "en",
            options: new WhisperOptions(SkipSilence: true));
        var hellos = transcript.Words.Where(w => w.Text.StartsWith("Hello", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, hellos.Count);
        var expected = firstLength + 25 + (hellos[0].Start - 0);
        Assert.InRange(hellos[1].Start, expected - 0.3, expected + 0.3);
        var water = transcript.Words.First(w => w.Text.StartsWith("water", StringComparison.OrdinalIgnoreCase));
        Assert.True(water.End < firstLength + 0.5, $"\"water\" ends at {water.End:0.00}, inside the silence");
    }

    [Fact]
    public async Task Only_the_used_part_is_transcribed_and_a_new_clip_adds_just_its_own_part()
    {
        if (Setup(WhisperModel.All[0]) is not var (runner, speech))
        {
            return;
        }

        // The English sample twice with 15 seconds of silence between. Only the second copy is on the timeline.
        var english = Path.Combine(speech, "english.wav");
        var file = await media.Generate("used parts.wav", "-i", english, "-f", "lavfi", "-t", "15", "-i", "anullsrc=r=22050:cl=mono", "-i", english,
            "-filter_complex", "[0][1][2]concat=n=3:v=0:a=1", "-ac", "1");
        var first = (await media.Probe(english)).Duration;
        var total = (await media.Probe(file)).Duration;
        var second = first + 15;
        var project = new Project();
        project.Layers.Add(new Layer { Kind = LayerKind.Audio, Name = "Voice" });
        project.Layers[0].Items.Add(new Item { Kind = ItemKind.Audio, Path = file, HasAudio = true, SourceIn = second, Duration = first, SourceDuration = total });

        var used = TranscriptCoverage.Used(project, file);
        Assert.True(TranscriptCoverage.Seconds(used) < total * 0.6, "only about half the file is transcribed");
        var transcript = await runner.TranscribePartsAsync(media.Tools, WhisperModel.All[0], file, "en", TranscriptCoverage.Missing(null, used));
        var hellos = transcript.Words.Where(w => w.Text.StartsWith("Hello", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(hellos);
        Assert.InRange(hellos[0].Start, second - 0.2, second + 1.0);
        Assert.All(transcript.Words, w => Assert.True(w.Start >= second - TranscriptCoverage.Margin - 0.01, $"{w.Text} at {w.Start:0.00}"));
        Assert.Contains(transcript.Words, w => w.Text.StartsWith("water", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(TranscriptCoverage.Missing(transcript, used));

        // Adding the first copy to the timeline transcribes just that part, keeping the words already there.
        project.Layers[0].Items.Insert(0, new Item { Kind = ItemKind.Audio, Path = file, HasAudio = true, SourceIn = 0, Duration = first, SourceDuration = total });
        var missing = TranscriptCoverage.Missing(transcript, TranscriptCoverage.Used(project, file));
        Assert.Single(missing);
        Assert.InRange(missing[0].End, first, second);
        var both = await runner.TranscribePartsAsync(media.Tools, WhisperModel.All[0], file, "en", missing, transcript);
        Assert.Equal(2, both.Words.Count(w => w.Text.StartsWith("Hello", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2, both.Words.Count(w => w.Text.StartsWith("water", StringComparison.OrdinalIgnoreCase)));
        Assert.All(both.Words.Zip(both.Words.Skip(1)), p => Assert.True(p.First.Start <= p.Second.Start));
        Assert.Empty(TranscriptCoverage.Missing(both, TranscriptCoverage.Used(project, file)));
    }

    [Fact]
    public async Task The_speed_test_times_each_setting_and_picks_the_fastest()
    {
        if (Setup(WhisperModel.All[0]) is not var (runner, speech))
        {
            return;
        }

        var result = await WhisperSpeedTest.RunAsync(runner, media.Tools, WhisperModel.All[0], Path.Combine(speech, "english.wav"), 0, 8, "en",
            [1, 2], tryGpu: false);
        Assert.Equal(2, result.Runs.Count);
        Assert.All(result.Runs, r => Assert.True(r.Seconds > 0 && r.Words > 10));
        Assert.Equal(result.Runs.Min(r => r.Seconds), result.Best.Seconds);
        Assert.Contains("times faster than real time. A 25-minute video would take about", result.Spoken);
        Assert.Equal("processor", result.Best.Device);
    }

    /// <summary>
    /// The graphics-chip path, run on Mesa's software Vulkan device in CI (AVE_VULKAN_CLI is a Vulkan build of
    /// whisper-cli; the test sets GGML_VK_VISIBLE_DEVICES=0 so it uses the software device). Real GPUs are much faster.
    /// </summary>
    [Fact]
    public async Task The_graphics_chip_path_gives_the_same_words_and_says_where_it_ran()
    {
        var vulkan = Environment.GetEnvironmentVariable("AVE_VULKAN_CLI");
        if (Setup(WhisperModel.All[0]) is not var (cpuRunner, speech) || vulkan is null || !File.Exists(vulkan))
        {
            return;
        }

        var runner = new WhisperRunner(vulkan, cpuRunner.ModelsFolder);
        var file = Path.Combine(speech, "english.wav");
        // Only this test may use the slow software device; whisper-cli inherits the setting.
        var before = Environment.GetEnvironmentVariable("GGML_VK_VISIBLE_DEVICES");
        Environment.SetEnvironmentVariable("GGML_VK_VISIBLE_DEVICES", "0");
        Transcript onGpu;
        try
        {
            onGpu = await runner.TranscribeAsync(media.Tools, WhisperModel.All[0], file, "en", options: new WhisperOptions(UseGpu: true, SkipSilence: false));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GGML_VK_VISIBLE_DEVICES", before);
        }

        Assert.NotEqual("processor", runner.LastDevice);
        var gpuText = string.Join(" ", onGpu.Words.Select(w => w.Text)).ToLowerInvariant();
        var onCpu = await runner.TranscribeAsync(media.Tools, WhisperModel.All[0], file, "en", options: new WhisperOptions(UseGpu: false, SkipSilence: false));
        Assert.Equal("processor", runner.LastDevice);
        foreach (var word in new[] { "hello", "welcome", "rice", "water" })
        {
            Assert.Contains(word, gpuText);
        }

        Assert.InRange(onGpu.Words.Count, onCpu.Words.Count - 2, onCpu.Words.Count + 2);
    }

    /// <summary>Reports progress on the calling thread (Progress&lt;T&gt; posts later, which tests can miss).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
