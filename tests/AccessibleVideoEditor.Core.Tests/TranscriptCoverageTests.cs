using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class TranscriptCoverageTests
{
    private static Item Clip(string path, double sourceIn, double duration, double sourceDuration = 600) => new()
    {
        Kind = ItemKind.Video, Path = path, HasAudio = true, HasVideo = true, SourceIn = sourceIn, Duration = duration, SourceDuration = sourceDuration,
    };

    [Fact]
    public void Only_the_parts_clips_play_are_wanted_on_every_layer_with_a_margin()
    {
        var project = new Project();
        project.Layers.Add(Make.Layer(LayerKind.Video, Clip("a.mp4", 100, 20), Clip("a.mp4", 300, 10), Clip("b.mp4", 0, 5)));
        project.Layers.Add(Make.Layer(LayerKind.Audio, Clip("a.mp4", 122, 5), Clip("a.mp4", 599, 30)));
        var used = TranscriptCoverage.Used(project, "A.MP4");
        // 99-121 and 121-128 overlap; 300 stands alone; the last is clamped to the file's end.
        Assert.Equal([new TimeStretch(99, 128), new TimeStretch(299, 311), new TimeStretch(598, 600)], used);
        Assert.Equal(29 + 12 + 2, TranscriptCoverage.Seconds(used), 6);
    }

    [Fact]
    public void Clips_a_few_seconds_apart_are_transcribed_together_and_the_start_never_goes_below_zero()
    {
        var project = new Project();
        project.Layers.Add(Make.Layer(LayerKind.Video, Clip("a.mp4", 0.5, 5), Clip("a.mp4", 9, 5)));
        Assert.Equal([new TimeStretch(0, 15)], TranscriptCoverage.Used(project, "a.mp4"));
    }

    [Fact]
    public void Parts_already_transcribed_are_not_done_again()
    {
        var existing = new Transcript { Covered = [new TimeStretch(10, 20), new TimeStretch(30, 40)] };
        var missing = TranscriptCoverage.Missing(existing, [new TimeStretch(5, 35), new TimeStretch(38, 50)]);
        Assert.Equal([new TimeStretch(5, 10), new TimeStretch(20, 30), new TimeStretch(40, 50)], missing);
        Assert.Empty(TranscriptCoverage.Missing(existing, [new TimeStretch(12, 18), new TimeStretch(39.9, 40.1)]));
        Assert.Equal([new TimeStretch(5, 35)], TranscriptCoverage.Missing(null, [new TimeStretch(5, 35)]));
        // A transcript of the whole file (and one from an older project) needs nothing more.
        Assert.Empty(TranscriptCoverage.Missing(new Transcript(), [new TimeStretch(0, 1000)]));
    }

    [Fact]
    public void Merging_replaces_words_inside_the_part_and_drops_words_only_heard_as_context()
    {
        static TranscriptWord W(string t, double s) => new() { Text = t, Start = s, End = s + 0.4 };
        var target = new Transcript { Words = [W("old", 1), W("keep", 25), W("stale", 12)], Covered = [new TimeStretch(0, 5), new TimeStretch(20, 30)] };
        var heard = new Transcript { Model = "base", Language = "en", Words = [W("context", 9), W("new", 11), W("words", 13), W("after", 20.5)] };
        TranscriptCoverage.Merge(target, heard, new TimeStretch(10, 20));
        Assert.Equal(["old", "new", "words", "keep"], target.Words.Select(w => w.Text));
        Assert.Equal([new TimeStretch(0, 5), new TimeStretch(10, 30)], target.Covered);
        Assert.Equal("base", target.Model);
    }

    [Fact]
    public void Covered_parts_are_saved_with_the_project()
    {
        var project = new Project();
        project.Transcripts.Add(new Transcript { MediaPath = "a.mp4", Covered = [new TimeStretch(1.5, 9)] });
        var again = Editing.ProjectSerializer.FromJson(Editing.ProjectSerializer.ToJson(project));
        Assert.Equal([new TimeStretch(1.5, 9)], again.Transcripts[0].Covered);
        Assert.Equal([new TimeStretch(1.5, 9)], project.Transcripts[0].Clone().Covered);
    }

    [Fact]
    public void A_draft_counts_as_unchanged_only_when_no_word_or_time_was_corrected()
    {
        var draft = new Transcript { Words = [new() { Text = "hello", Start = 1, End = 1.4 }, new() { Text = "wold", Start = 1.5, End = 2 }] };
        var copy = draft.Clone();
        Assert.True(copy.SameWords(draft));
        copy.Words[1].Text = "world";
        Assert.False(copy.SameWords(draft));
        copy = draft.Clone();
        copy.Words[0].End = 1.3;
        Assert.False(copy.SameWords(draft));
        copy = draft.Clone();
        copy.Words.RemoveAt(1);
        Assert.False(copy.SameWords(draft));
    }
}
