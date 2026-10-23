using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class BeatEditingTests
{
    private static Item Picture(string name, double duration) => new()
    {
        Kind = ItemKind.Image, Name = name, Path = name, Duration = duration, HasVideo = true,
    };

    /// <summary>A project with a 120 BPM song that starts 10 s into its file and plays for 20 s.</summary>
    private static (Project Project, Layer Pictures, Layer Music) Setup()
    {
        var project = new Project();
        var pictures = ProjectEditor.AddLayer(project, LayerKind.Video);
        var music = ProjectEditor.AddLayer(project, LayerKind.Audio);
        var song = Make.Clip("song.mp3", 20, sourceDuration: 60);
        song.Kind = ItemKind.Audio;
        song.SourceIn = 10;
        music.Items.Add(song);
        var beats = Enumerable.Range(0, 120).Select(i => 0.2 + i * 0.5).ToList();
        Assert.True(BeatEditing.SetBeats(music, 0, new BeatAnalysis(120, beats, 0.9)).Ok);
        return (project, pictures, music);
    }

    [Fact]
    public void Beats_are_kept_in_file_time_and_shown_where_the_song_plays()
    {
        var (project, _, music) = Setup();
        var times = BeatEditing.BeatsOf(music, 0);
        Assert.Equal(40, times.Count);
        Assert.Equal(0.2, times[0], 6); // file beat at 10.2 s plays at 0.2 s
        Assert.Equal(19.7, times[^1], 6);

        // Trimming the start of the song moves nothing: the beats stay on the music.
        music.Items[0].SourceIn = 10.1;
        music.Items[0].Duration = 19.9;
        Assert.Equal(0.1, BeatEditing.BeatsOf(music, 0)[0], 6);
        Assert.Equal(0.1, BeatEditing.NeighbourBeat(project, 0, 1)!.Value, 6);
        Assert.Null(BeatEditing.NeighbourBeat(project, 0.05, -1));
    }

    [Fact]
    public void Pictures_are_cut_to_whole_beats()
    {
        var (project, pictures, _) = Setup();
        pictures.Items.Add(Picture("a.jpg", 3));
        pictures.Items.Add(Picture("b.jpg", 3));
        pictures.Items.Add(Picture("c.jpg", 3));
        var result = BeatEditing.CutToBeats(project, pictures, beatsPerItem: 2);
        Assert.True(result.Ok, result.Message);
        var spans = Timeline.Spans(pictures);
        var beats = BeatEditing.BeatTimes(project);
        foreach (var span in spans)
        {
            Assert.Contains(beats, b => Math.Abs(b - span.End) < 1e-6);
        }

        // From the first beat a picture spans an even number of beats.
        Assert.Equal(3.2, spans[0].End, 6);
        Assert.Equal(6.2, spans[1].End, 6);
        Assert.Contains("now end on a beat", result.Message);
    }

    [Fact]
    public void Clips_never_get_longer_than_their_file()
    {
        var (project, pictures, _) = Setup();
        pictures.Items.Add(Make.Clip("short.mp4", 1.9, sourceDuration: 1.9));
        BeatEditing.CutToBeats(project, pictures);
        Assert.Equal(1.7, pictures.Items[0].Duration, 6);
    }

    [Fact]
    public void Music_without_a_beat_is_reported()
    {
        var (_, _, music) = Setup();
        var result = BeatEditing.SetBeats(music, 0, new BeatAnalysis(0, [], 0));
        Assert.False(result.Ok);
        Assert.Empty(music.Items[0].Beats);
        Assert.Contains("No steady beat", result.Message);
    }

    [Fact]
    public void Cutting_without_beats_explains_what_to_do()
    {
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(Picture("a.jpg", 3));
        Assert.Contains("Find beats", BeatEditing.CutToBeats(project, layer).Message);
    }

    [Fact]
    public void Beats_survive_save_split_and_clone()
    {
        var (project, _, music) = Setup();
        var copy = ProjectSerializer.Clone(project);
        Assert.Equal(120, copy.Layers[1].Items[0].Beats.Count);
        ProjectEditor.SplitItem(music, 0, 5);
        Assert.Equal(120, music.Items[1].Beats.Count);
        music.Items[1].Beats.Clear();
        Assert.Equal(120, music.Items[0].Beats.Count); // split halves don't share the list
        Assert.Equal(10, BeatEditing.BeatsOf(music, 0).Count);
    }
}
