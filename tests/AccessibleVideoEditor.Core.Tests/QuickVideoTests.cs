using System.Globalization;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>The three-step Quick Video: real files in, a finished project out, checked in a render.</summary>
[Collection(MediaCollection.Name)]
public sealed class QuickVideoTests(MediaFixture media)
{
    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private async Task<MediaInfo> Photo(string name, string color) =>
        await media.Probe(await media.Generate(name, "-f", "lavfi", "-i", $"color=c={color}:size=640x480", "-frames:v", "1"));

    private async Task<MediaInfo> Drums120()
    {
        const string p = "0.5";
        var kick = $"0.9*sin(2*PI*(50+90*exp(-30*mod(t,{p})))*mod(t,{p}))*exp(-14*mod(t,{p}))";
        var hat = $"0.25*(2*random(0)-1)*exp(-90*mod(t+{p}/2,{p}))";
        return await media.Probe(await media.Generate("quick drums.wav", "-f", "lavfi", "-i", $"aevalsrc='{kick}+{hat}':s=44100:d=12", "-ac", "2"));
    }

    /// <summary>A 6 s talking clip: speech bursts at 0.5-2 and 4-5.5 s, with a 2 s pause.</summary>
    private async Task<MediaInfo> TalkingClip() => await media.Probe(await media.Generate("talking.mp4",
        "-f", "lavfi", "-i", "testsrc=size=320x240:rate=30:duration=6",
        "-f", "lavfi", "-i", "aevalsrc='0.4*sin(2*PI*700*t)*(between(t,0.5,2)+between(t,4,5.5))+0.001*(2*random(0)-1)':s=48000:d=6",
        "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest"));

    [Fact]
    public async Task A_reel_is_vertical_cut_to_the_beat_with_whooshes_title_and_music_to_the_end()
    {
        var photos = new List<MediaInfo>
        {
            await Photo("photo 10.png", "red"), await Photo("photo 2.png", "green"), await Photo("photo 1.png", "blue"),
        };
        var whoosh = await media.Generate("whoosh.wav", "-f", "lavfi", "-i", "anoisesrc=c=pink:a=0.3:d=0.6", "-af", "afade=t=in:d=0.3,afade=t=out:st=0.3:d=0.3");
        var request = QuickVideo.Analyse(media.Tools, new QuickVideoRequest
        {
            Files = photos, Style = QuickStyle.Reel, Title = "My trip", Music = await Drums120(), WhooshPath = whoosh,
        });
        Assert.True(request.MusicBeats!.HasSteadyBeat);

        var (project, summary) = QuickVideo.Build(request);
        Assert.Equal((1080, 1920), (project.Width, project.Height));
        var main = project.Layers.Single(l => l.Name == "Video");
        Assert.Equal(["photo 1.png", "photo 2.png", "photo 10.png"], main.Items.Select(i => Path.GetFileName(i.Path)));
        Assert.All(main.Items, i => Assert.True(i.Placement.Fill));

        // Every picture ends on a beat, two beats (1 s at 120 BPM) each.
        var beats = BeatEditing.BeatTimes(project);
        foreach (var span in Timeline.Spans(main))
        {
            Assert.True(beats.Any(b => Math.Abs(b - span.End) < 1e-6),
                $"{span.Start:0.000}-{span.End:0.000} not on a beat; spans {string.Join(" ", Timeline.Spans(main).Select(x => $"{x.Start:0.000}-{x.End:0.000}"))}; beats {string.Join(" ", beats.Take(12).Select(b => b.ToString("0.000")))}");
            Assert.InRange(span.Duration, 0.95, 1.05);
        }

        var length = Timeline.LayerEnd(main);
        var music = project.Layers.Single(l => l.Name == "Music");
        Assert.Equal(length, Timeline.LayerEnd(music), 6);
        Assert.True(music.Items[^1].FadeOut > 0);
        Assert.Equal(2, project.Layers.Single(l => l.Name == "Effects").Items.Count);
        Assert.Equal("My trip", project.Layers.Single(l => l.Name == "Title").Items[0].Text.Content);
        Assert.Contains("cut to the beat at 120 beats per minute", summary);
        Assert.Contains("a whoosh on every cut", summary);

        // It renders, at the -14 LUFS that social sites play at (measured first, then levelled).
        var gain = await Loudness.MasterGainAsync(media.Tools, project, RenderScope.AllLayers);
        Assert.NotNull(gain);
        var (output, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("reel.mp4"), CanvasOverride = (180, 320),
            MasterGainDb = gain,
        });
        var info = await media.Probe(output);
        Assert.InRange(info.Duration, length - 0.1, length + 0.1);
        Assert.InRange(await media.LoudnessLufs(output), -15, -13);
    }

    [Fact]
    public async Task A_talking_video_shortens_pauses_ducks_the_music_and_adds_captions()
    {
        var clip = await TalkingClip();
        var transcript = new Transcript
        {
            MediaPath = clip.Path, Language = "en",
            Words = [new() { Text = "Hello", Start = 0.6, End = 1.2 }, new() { Text = "there.", Start = 1.3, End = 1.9 },
                     new() { Text = "Welcome", Start = 4.1, End = 4.8 }, new() { Text = "back.", Start = 4.9, End = 5.4 }],
        };
        var request = QuickVideo.Analyse(media.Tools, new QuickVideoRequest
        {
            Files = [clip], Style = QuickStyle.Talking, Title = "Hello", Music = await Drums120(), Transcripts = [transcript],
        });
        var (project, summary) = QuickVideo.Build(request);

        var main = project.Layers.Single(l => l.Name == "Video");
        var length = Timeline.LayerEnd(main);
        Assert.InRange(length, 4.0, 4.7); // 6 s minus most of the 2 s pause
        Assert.Contains("removed", summary);
        Assert.Contains("music lowered under speech", summary);
        Assert.Contains("captions", summary);

        var music = project.Layers.Single(l => l.Name == "Music");
        Assert.NotEmpty(music.Items.SelectMany(i => i.VolumeDips));
        Assert.Equal(0.35, music.Items[0].Volume);
        var captions = project.Layers.Single(l => l.Name == Captions.LayerName);
        Assert.Equal(["Hello there.", "Welcome back."], captions.Items.Select(i => i.Text.Content));
        // The second caption follows the cut: it starts when "Welcome" now plays.
        var welcome = Captions.WordsOnTimeline(project, main).First(w => w.Text == "Welcome").Start;
        Assert.Equal(welcome, Timeline.Spans(captions)[1].Start, 6);
        Assert.InRange(welcome, 2.3, 2.9); // 4.1 s in the file, minus the 1.5 s taken out of the pause

        var (output, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("talking.mp4"), CanvasOverride = (320, 180),
        });
        Assert.InRange((await media.Probe(output)).Duration, length - 0.1, length + 0.1);

        // Listen to the voice alone: no pause longer than 0.6 s is left.
        project.Layers.Single(l => l.Name == "Music").Muted = true;
        var (voice, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.Wav, OutputPath = media.OutPath("talking voice.wav") });
        var pauses = AccessibleVideoEditor.Core.Audio.SpeechDetector.Analyse(media.Tools, voice, 0, 20, minimumPause: 0.25).Pauses;
        Assert.All(pauses, p => Assert.True(p.Duration < 0.6, $"Pause {p.Start:0.00}-{p.End:0.00} is still {p.Duration:0.00} s"));
    }

    [Fact]
    public void Each_style_explains_itself()
    {
        foreach (var style in Enum.GetValues<QuickStyle>())
        {
            Assert.False(string.IsNullOrWhiteSpace(QuickStyleRules.For(style).Spoken));
        }

        var (_, nothing) = QuickVideo.Build(new QuickVideoRequest { Files = [] });
        Assert.Contains("nothing to make", nothing);
    }
}
