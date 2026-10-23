using System.Globalization;
using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Pause detection, cutting pauses out, and lowering music under speech, checked in real renders.</summary>
[Collection(MediaCollection.Name)]
public sealed class PauseAndDuckingTests(MediaFixture media)
{
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// "Speech": 1 kHz bursts at 0-1, 2.5-3.5 (after a 1.5 s pause), 3.8-4.8 (after a 0.3 s pause), and
    /// 7-8 s (after a 2.2 s pause), over quiet background noise, 9 s long.
    /// </summary>
    private Task<string> Speech(string name, double noise = 0.001) => media.Generate(name, "-f", "lavfi", "-i",
        $"aevalsrc='0.5*sin(2*PI*1000*t)*(between(t,0,1)+between(t,2.5,3.5)+between(t,3.8,4.8)+between(t,7,8))+{N(noise)}*(2*random(0)-1)':s=48000:d=9",
        "-ac", "1");

    [Fact]
    public async Task Pauses_are_found_and_short_gaps_are_not()
    {
        var file = await Speech("speech pauses.wav");
        var result = SpeechDetector.Analyse(media.Tools, file, 0, 9, minimumPause: 0.5);
        Assert.Equal(3, result.Pauses.Count);
        Assert.InRange(result.Pauses[0].Start, 1.0, 1.15);
        Assert.InRange(result.Pauses[0].End, 2.35, 2.5);
        Assert.InRange(result.Pauses[1].Start, 4.8, 4.95);
        Assert.InRange(result.Pauses[1].End, 6.85, 7.0);
        Assert.InRange(result.Pauses[2].Start, 8.0, 8.15);
        Assert.Equal(9, result.Pauses[2].End, 2);
        Assert.False(result.IsNoisy);
    }

    [Fact]
    public async Task Pauses_are_found_in_a_noisy_recording_too()
    {
        var file = await Speech("noisy speech.wav", noise: 0.05);
        var result = SpeechDetector.Analyse(media.Tools, file, 0, 9, minimumPause: 0.5);
        Assert.Equal(3, result.Pauses.Count);
        Assert.InRange(result.Pauses[1].Duration, 1.8, 2.1);
    }

    [Fact]
    public async Task Analysing_part_of_a_file_uses_times_within_that_part()
    {
        var file = await Speech("speech part.wav");
        var result = SpeechDetector.Analyse(media.Tools, file, 2, 4, minimumPause: 0.5).Shift(2);
        Assert.Single(result.Pauses, p => p.Start > 4.7 && p.End >= 5.99);
    }

    [Fact]
    public async Task Shortening_pauses_cuts_them_to_the_kept_length_in_the_render()
    {
        var file = await Speech("speech cut.wav");
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        layer.Items.Add(await media.ItemFor(file, LayerKind.Audio));
        var analysis = SpeechDetector.Analyse(media.Tools, file, 0, 9, minimumPause: 0.5);
        var cuts = CutEditing.PauseCuts(analysis, keep: 0.3, longerThan: 0.7);
        Assert.Equal(3, cuts.Count);

        var result = CutEditing.RemoveSourceRanges(layer, 0, cuts, project.FrameRate, "pauses");
        Assert.True(result.Ok, result.Message);
        Assert.StartsWith("Removed 3 pauses", result.Message);
        Assert.Equal(3, layer.Items.Count);
        Assert.All(layer.Items, i => Assert.Equal(CutEditing.JoinFade, i.SoundJoinFade));

        // 4.3 s of sound, 80 ms of margin on each side of every burst, and three pauses kept at 0.3 s.
        var expected = layer.Items.Sum(i => i.Duration);
        Assert.InRange(expected, 5.4, 5.8);
        var (output, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.Wav, OutputPath = media.OutPath("cut.wav") });
        var info = await media.Probe(output);
        Assert.InRange(info.Duration, expected - 0.03, expected + 0.03);

        var after = SpeechDetector.Analyse(media.Tools, output, 0, 20, minimumPause: 0.5);
        Assert.Empty(after.Pauses);
        Assert.Single(after.Sound);
    }

    [Fact]
    public void Removing_everything_or_nothing_is_refused()
    {
        var layer = Make.Layer(LayerKind.Audio, Make.Clip("a.wav", 4));
        layer.Items[0].Kind = ItemKind.Audio;
        Assert.False(CutEditing.RemoveSourceRanges(layer, 0, [new TimeStretch(0, 4)], 25).Ok);
        Assert.False(CutEditing.RemoveSourceRanges(layer, 0, [new TimeStretch(5, 6)], 25).Ok);
        Assert.Single(layer.Items);
        var image = Make.Layer(LayerKind.Video, new Item { Kind = ItemKind.Image, Name = "p.jpg", Duration = 3 });
        Assert.False(CutEditing.RemoveSourceRanges(image, 0, [new TimeStretch(1, 2)], 25).Ok);
    }

    [Fact]
    public void Cut_pieces_keep_the_clip_settings_and_only_the_first_keeps_its_gap_and_transition()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a.mp4", 2), Make.Clip("b.mp4", 6, gap: 1).WithTransition("fade", 0.5));
        layer.Items[1].Volume = 0.5;
        layer.Items[1].FadeOut = 1;
        var result = CutEditing.RemoveSourceRanges(layer, 1, [new TimeStretch(2, 3), new TimeStretch(2.6, 3.6)], 25);
        Assert.True(result.Ok);
        Assert.Equal(3, layer.Items.Count);
        Assert.Equal(1, layer.Items[1].GapBefore);
        Assert.Equal(0, layer.Items[2].GapBefore);
        Assert.Equal(TransitionSpec.None, layer.Items[2].TransitionIn.Video);
        Assert.Equal(0, layer.Items[1].FadeOut);
        Assert.Equal(1, layer.Items[2].FadeOut);
        Assert.All(layer.Items.Skip(1), i => Assert.Equal(0.5, i.Volume));
        Assert.Equal(2, layer.Items[1].Duration, 6);
        Assert.Equal(3.6, layer.Items[2].SourceIn, 6);
        Assert.Contains("Removed 1 part, 1.6 seconds", result.Message);
    }

    [Fact]
    public async Task Music_is_lowered_where_someone_speaks_and_back_up_after()
    {
        var speech = await Speech("speech duck.wav");
        var music = await media.Generate("duck music.wav", "-f", "lavfi", "-i", "sine=frequency=300:sample_rate=48000:duration=9", "-ac", "2");
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var voice = ProjectEditor.AddLayer(project, LayerKind.Audio);
        voice.Items.Add(await media.ItemFor(speech, LayerKind.Audio));
        var bed = ProjectEditor.AddLayer(project, LayerKind.Audio);
        bed.Name = "Music";
        bed.Items.Add(await media.ItemFor(music, LayerKind.Audio));

        var found = Ducking.FindSpeech(media.Tools, project, bed);
        var result = Ducking.Apply(bed, found, level: 0.25, ramp: 0.2);
        Assert.True(result.Ok);
        // 2.5-3.5 and 3.8-4.8 are bridged (0.3 s apart), so three dips: 0-1, 2.5-4.8, 7-8.
        Assert.Equal(3, bed.Items[0].VolumeDips.Count);
        Assert.Equal("Music lowered by 12 decibels in 3 places where someone speaks.", result.Message);

        // Hear only the music to measure it.
        voice.Muted = true;
        var (output, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.Wav, OutputPath = media.OutPath("ducked.wav") });
        var full = await media.PeakDb(output, 5.5, 6.5);
        var underSpeech = await media.PeakDb(output, 3.0, 4.3);
        var bridged = await media.PeakDb(output, 3.55, 3.75);
        Assert.InRange(full - underSpeech, 11, 13);
        Assert.InRange(full - bridged, 11, 13);
        Assert.InRange(await media.PeakDb(output, 0.1, 0.8), full - 13, full - 11);
    }

    [Fact]
    public void Dips_follow_the_music_when_it_is_trimmed_and_render_only_where_they_fall()
    {
        var item = new Item { Kind = ItemKind.Audio, SourceIn = 10, Duration = 5, HasAudio = true };
        item.VolumeDips.Add(new VolumeDip { Start = 11, End = 12, Level = 0.5, Ramp = 0.25 });
        item.VolumeDips.Add(new VolumeDip { Start = 30, End = 31 });
        var expression = GraphBuilder.DipExpression(item, 5)!;
        Assert.Equal("(1-0.5*clip((t-0.75)/0.25,0,1)*clip((2.25-t)/0.25,0,1))", expression);
        Assert.Null(GraphBuilder.DipExpression(new Item { SourceIn = 40, Duration = 5, VolumeDips = { new VolumeDip { Start = 1, End = 2 } } }, 5));
    }

    [Fact]
    public void Ducking_can_be_cleared()
    {
        var layer = Make.Layer(LayerKind.Audio, Make.Clip("m.mp3", 10));
        Ducking.Apply(layer, [new TimeStretch(1, 2)]);
        Assert.Single(layer.Items[0].VolumeDips);
        Assert.True(Ducking.Clear(layer).Ok);
        Assert.Empty(layer.Items[0].VolumeDips);
        Assert.False(Ducking.Clear(layer).Ok);
    }
}
