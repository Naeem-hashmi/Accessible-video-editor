using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Transcripts, captions and editing by transcript, without running Whisper.</summary>
public sealed class TranscriptTests
{
    private static TranscriptWord W(string text, double start, double end) => new() { Text = text, Start = start, End = end };

    /// <summary>
    /// A 10 s clip saying "Hello and welcome. Um today we cook rice. Thanks for watching." with clear pauses.
    /// </summary>
    private static (Project Project, Layer Layer) Spoken()
    {
        var project = new Project { FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(Make.Clip("talk.mp4", 10));
        project.Transcripts.Add(new Transcript
        {
            MediaPath = "talk.mp4",
            Language = "en",
            Words =
            [
                W("Hello", 0.5, 0.9), W("and", 0.95, 1.1), W("welcome.", 1.15, 1.7),
                W("Um", 2.5, 2.9),
                W("today", 3.6, 4.0), W("we", 4.05, 4.2), W("cook", 4.25, 4.6), W("rice.", 4.65, 5.1),
                W("Thanks", 7.0, 7.4), W("for", 7.45, 7.6), W("watching.", 7.65, 8.3),
            ],
        });
        return (project, layer);
    }

    [Fact]
    public void Whisper_json_is_read_word_by_word_with_punctuation_joined()
    {
        var json = """
            {"result":{"language":"ur"},"transcription":[
            {"offsets":{"from":0,"to":100},"text":""},
            {"offsets":{"from":100,"to":480},"text":" وصلام"},
            {"offsets":{"from":480,"to":950},"text":" علیکم"},
            {"offsets":{"from":950,"to":1000},"text":"۔"},
            {"offsets":{"from":1000,"to":1320},"text":" [_TT_50]"},
            {"offsets":{"from":1320,"to":1700},"text":" Hello"}]}
            """;
        var transcript = WhisperRunner.Parse(json, offset: 10);
        Assert.Equal("ur", transcript.Language);
        Assert.Equal(["وصلام", "علیکم۔", "Hello"], transcript.Words.Select(w => w.Text));
        Assert.Equal(10.1, transcript.Words[0].Start, 6);
        Assert.Equal(11.7, transcript.Words[2].End, 6);
    }

    [Fact]
    public void Words_with_no_length_share_time_and_edges_move_out_of_pauses()
    {
        var words = new List<TranscriptWord> { W("show", 3.9, 4.72), W("you", 4.72, 4.72), W("how", 4.72, 5.07), W("channel.", 1.44, 2.76) };
        TranscriptTiming.ShareZeroLengthWords(words);
        Assert.True(words[1].End - words[1].Start > 0.1);
        Assert.Equal(words[0].End, words[1].Start, 6);

        var sentence = new List<TranscriptWord> { W("my", 1.29, 1.44), W("channel.", 1.44, 2.76), W("Today", 2.76, 3.34) };
        var analysis = new SpeechAnalysis([new(0, 1.62), new(2.82, 4)], [new TimeStretch(1.62, 2.82)], 4, -40, -70);
        TranscriptTiming.Refine(sentence, analysis);
        Assert.Equal(1.62, sentence[1].End, 6);   // "channel." no longer runs through the pause
        Assert.Equal(2.82, sentence[2].Start, 6); // "Today" starts when the sound does
    }

    [Fact]
    public void The_transcript_reads_as_one_sentence_per_line()
    {
        var (project, layer) = Spoken();
        var document = TranscriptEditing.Build(project, layer);
        Assert.Equal("Hello and welcome.\nUm today we cook rice.\nThanks for watching.", document.Text);
        Assert.Equal(11, document.Entries.Count);
    }

    [Fact]
    public void Deleting_a_sentence_cuts_it_out_of_the_clip_with_a_little_pause_kept()
    {
        var (project, layer) = Spoken();
        var document = TranscriptEditing.Build(project, layer);
        var result = TranscriptEditing.Apply(project, layer, document, "Hello and welcome.\nThanks for watching.", TranscriptMode.Cut);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(2, layer.Items.Count);
        // Cut from 0.12 s after "welcome." to 0.12 s before "Thanks": 1.82 to 6.88, snapped to frames.
        Assert.InRange(layer.Items[0].SourceIn + layer.Items[0].Duration, 1.78, 1.86);
        Assert.Equal(6.88, layer.Items[1].SourceIn, 6);
        Assert.StartsWith("Cut 5 words from Video 1, 5", result.Message);

        // The transcript of the edited layer has only the kept words.
        Assert.Equal("Hello and welcome.\nThanks for watching.", TranscriptEditing.Build(project, layer).Text);
    }

    [Fact]
    public void Deleting_one_filler_word_keeps_the_words_around_it()
    {
        var (project, layer) = Spoken();
        var document = TranscriptEditing.Build(project, layer);
        var result = TranscriptEditing.Apply(project, layer, document, document.Text.Replace("Um ", ""), TranscriptMode.Cut);
        Assert.True(result.Ok);
        Assert.Equal("Hello and welcome.\ntoday we cook rice.\nThanks for watching.", TranscriptEditing.Build(project, layer).Text);
        var total = layer.Items.Sum(i => i.Duration);
        // 0.8 s before "Um" and 0.7 s after it become 0.12 s each: 1.66 s plus frame snapping.
        Assert.InRange(10 - total, 1.6, 1.72);
    }

    [Fact]
    public void Deleting_the_first_and_last_words_trims_the_clip_ends()
    {
        var (project, layer) = Spoken();
        var document = TranscriptEditing.Build(project, layer);
        var result = TranscriptEditing.Apply(project, layer, document, "Um today we cook rice.", TranscriptMode.Cut);
        Assert.True(result.Ok, result.Message);
        Assert.Single(layer.Items);
        Assert.InRange(layer.Items[0].SourceIn, 2.0, 2.4);
        Assert.InRange(layer.Items[0].SourceIn + layer.Items[0].Duration, 5.2, 5.3);
    }

    [Fact]
    public void Cut_mode_ignores_spelling_changes_and_says_so()
    {
        var (project, layer) = Spoken();
        var document = TranscriptEditing.Build(project, layer);
        var only = TranscriptEditing.Apply(project, layer, document, document.Text.Replace("rice", "rise"), TranscriptMode.Cut);
        Assert.False(only.Ok);
        Assert.Contains("Fix text mode", only.Message);
        Assert.Single(layer.Items);
    }

    [Fact]
    public void Fix_text_mode_corrects_words_and_keeps_the_video()
    {
        var (project, layer) = Spoken();
        var document = TranscriptEditing.Build(project, layer);
        var edited = "Hello and welcome!\ntoday we cook basmati rice.\nThanks for watching.";
        var result = TranscriptEditing.Apply(project, layer, document, edited, TranscriptMode.FixText);
        Assert.True(result.Ok, result.Message);
        Assert.Single(layer.Items);
        Assert.Equal(10, layer.Items[0].Duration);
        Assert.Equal("Hello and welcome!\ntoday we cook basmati rice.\nThanks for watching.", TranscriptEditing.Build(project, layer).Text);
        Assert.DoesNotContain(project.Transcripts[0].Words, w => w.Text == "Um");
    }

    [Fact]
    public void Urdu_and_hindi_words_compare_with_their_vowel_marks()
    {
        Assert.Equal("نمستے", TranscriptEditing.Normalize("نمستے،"));
        Assert.Equal("नमस्ते", TranscriptEditing.Normalize("नमस्ते।"));
        Assert.NotEqual(TranscriptEditing.Normalize("कि"), TranscriptEditing.Normalize("क"));
    }

    [Fact]
    public void Long_transcripts_compare_quickly()
    {
        var words = Enumerable.Range(0, 20000).Select(i => "w" + i).ToList();
        var edited = words.Where((_, i) => i is < 9000 or > 9010).ToList();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var pairs = TranscriptEditing.Lcs(words, edited);
        Assert.Equal(edited.Count, pairs.Count);
        Assert.True(clock.Elapsed.TotalSeconds < 2);
    }

    [Fact]
    public void Captions_break_at_sentences_pauses_and_length()
    {
        var (project, layer) = Spoken();
        var lines = Captions.Lines(Captions.WordsOnTimeline(project, layer));
        Assert.Equal(["Hello and welcome.", "Um", "today we cook rice.", "Thanks for watching."], lines.Select(l => l.Text));
        Assert.Equal(0.5, lines[0].Start, 6);
        Assert.Equal(2.0, lines[0].End, 6);   // held 0.3 s
        Assert.Equal(3.3, lines[1].End, 6);   // short line shown 0.8 s
        var longLine = Captions.Lines(Enumerable.Range(0, 20).Select(i => new TimedWord("word" + i, i * 0.3, i * 0.3 + 0.25)).ToList());
        Assert.All(longLine, l => Assert.True(l.Text.Length <= Captions.MaxCharacters));
    }

    [Fact]
    public void Captions_go_on_their_own_top_layer_at_the_right_times_and_follow_cuts()
    {
        var (project, layer) = Spoken();
        var result = Captions.Create(project, layer);
        Assert.True(result.Ok, result.Message);
        var captions = project.Layers[^1];
        Assert.Equal(Captions.LayerName, captions.Name);
        Assert.All(captions.Items, i => Assert.Equal(ItemKind.Text, i.Kind));
        var spans = Timeline.Spans(captions);
        Assert.Equal(0.5, spans[0].Start, 6);
        Assert.Equal(7.0, spans[^1].Start, 6);
        Assert.Equal("Segoe UI", captions.Items[0].Text.Font);

        // After cutting the middle sentence, making captions again replaces them.
        var document = TranscriptEditing.Build(project, layer);
        TranscriptEditing.Apply(project, layer, document, "Hello and welcome.\nThanks for watching.", TranscriptMode.Cut);
        Captions.Create(project, layer);
        Assert.Single(project.Layers, l => l.Name == Captions.LayerName);
        Assert.Equal(["Hello and welcome.", "Thanks for watching."], project.Layers[^1].Items.Select(i => i.Text.Content));
        Assert.InRange(Timeline.Spans(project.Layers[^1])[1].Start, 1.9, 2.1);
    }

    [Fact]
    public void Urdu_captions_use_an_urdu_font_and_hindi_a_devanagari_one()
    {
        Assert.Equal("Noto Naskh Arabic", Captions.DefaultStyle("ur").Font);
        Assert.Equal("Nirmala UI", Captions.DefaultStyle("hi").Font);
    }

    [Fact]
    public void Transcripts_are_saved_with_the_project()
    {
        var (project, _) = Spoken();
        var copy = ProjectSerializer.Clone(project);
        Assert.Equal(11, copy.TranscriptFor("TALK.mp4")!.Words.Count);
    }

    [Fact]
    public void Models_are_described_with_their_size()
    {
        Assert.Equal("Accurate (recommended), 574 MB. Best for Urdu, Hindi and noisy recordings. Slower; works on any modern computer.", WhisperModel.Recommended.Spoken);
        Assert.StartsWith("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/", WhisperModel.Recommended.Url);
        Assert.Equal("नमस्ते। यह हिंदी में है।", WhisperRunner.InitialPrompt("hi"));
        Assert.Null(WhisperRunner.InitialPrompt("ur"));
    }
}
