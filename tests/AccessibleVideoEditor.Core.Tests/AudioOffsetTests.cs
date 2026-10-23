using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>
/// Files whose sound starts later (or earlier) than their picture, as some phones and cameras write them.
/// A white flash and a beep happen together when the file is played; after editing they must still match.
/// </summary>
[Collection(MediaCollection.Name)]
public sealed class AudioOffsetTests(MediaFixture media)
{
    /// <summary>
    /// Flash and beep at 2.0 s of playback; the stored sound starts <paramref name="audioDelay"/> seconds after
    /// the picture (negative: before). Picture and sound are encoded separately and then joined without
    /// re-encoding, so each keeps its real start time, as in a camera file.
    /// </summary>
    private async Task<string> FlashAndBeep(string name, double audioDelay)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string N(double v) => v.ToString("0.###", inv);
        var videoDelay = Math.Max(0, -audioDelay);
        var soundDelay = Math.Max(0, audioDelay);
        // Each stream has its event at 2.0 s minus its own start delay.
        var video = await media.Generate(name + ".v.mp4", "-f", "lavfi", "-i",
            $"testsrc2=size=160x120:rate=25:duration=5,drawbox=enable='between(t,{N(2 - videoDelay)},{N(2 - videoDelay)}+0.12)':color=white:t=fill",
            "-c:v", "libx264", "-pix_fmt", "yuv420p");
        var audio = await media.Generate(name + ".a.m4a", "-f", "lavfi", "-i",
            $"aevalsrc='0.6*sin(2*PI*1000*t)*between(t,{N(2 - soundDelay)},{N(2 - soundDelay)}+0.12)':s=48000:d=5", "-c:a", "aac");
        return await media.Generate(name, "-itsoffset", N(videoDelay), "-i", video, "-itsoffset", N(soundDelay), "-i", audio,
            "-map", "0:v", "-map", "1:a", "-c", "copy");
    }

    private async Task<(double Flash, double Beep)> Measure(string file)
    {
        var info = await media.Probe(file);
        // Flash: the first frame that is mostly white.
        var flash = -1.0;
        for (var t = 0.0; t < info.Duration && flash < 0; t += 0.04)
        {
            var (r, g, b) = await media.PixelAt(file, t, 10, 10);
            if (r > 220 && g > 220 && b > 220)
            {
                flash = t;
            }
        }

        var samples = FfmpegStream.DecodeMono(media.Tools, file, 16000);
        var first = Array.FindIndex(samples, v => Math.Abs(v) > 0.2);
        return (flash, first < 0 ? -1 : first / 16000.0);
    }

    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(-0.4, 0.0)]
    [InlineData(-0.4, 1.0)]
    public async Task Sound_stays_with_the_picture_when_it_is_stored_later_or_earlier(double audioDelay, double sourceIn)
    {
        var file = await FlashAndBeep($"offset {audioDelay} {sourceIn}.mkv", audioDelay);
        var project = new Project { Width = 160, Height = 120, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var item = await media.ItemFor(file, LayerKind.Video);
        item.SourceIn = sourceIn;
        item.Duration = 3;
        layer.Items.Add(item);
        var (output, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath($"offset out {audioDelay} {sourceIn}.mp4") });
        var (flash, beep) = await Measure(output);
        Assert.True(flash >= 0 && beep >= 0, $"flash {flash}, beep {beep}");
        Assert.True(Math.Abs(flash - beep) < 0.06, $"Flash at {flash:0.000} s but beep at {beep:0.000} s (sound stored {audioDelay} s late, clip starts at {sourceIn} s)");
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(0.5)]
    [InlineData(0.25)]
    public async Task A_faster_or_slower_clip_keeps_sound_with_picture_and_has_the_right_length(double speed)
    {
        var file = await FlashAndBeep("speed source.mkv", 0);
        var project = new Project { Width = 160, Height = 120, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var item = await media.ItemFor(file, LayerKind.Video);
        item.SourceIn = 1;
        item.Duration = 2;
        layer.Items.Add(item);
        Assert.True(StretchEditing.SetSpeed(layer, 0, speed, ripple: true).Ok);
        var (output, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath($"speed {speed}.mp4") });
        var length = (await media.Probe(output)).Duration;
        Assert.InRange(length, 2 / speed - 0.1, 2 / speed + 0.1);
        // The flash and beep are 1 s into the clip's part of the file, so (1 / speed) s into the result.
        var (flash, beep) = await Measure(output);
        Assert.InRange(flash, 1 / speed - 0.08, 1 / speed + 0.08);
        Assert.True(Math.Abs(flash - beep) < 0.06, $"Flash at {flash:0.000} s but beep at {beep:0.000} s at speed {speed}");
    }

    [Fact]
    public async Task The_offset_is_read_when_the_file_is_added()
    {
        var late = await media.Probe(await FlashAndBeep("probe late.mkv", 0.5));
        Assert.InRange(late.AudioOffset, 0.45, 0.55);
        var early = await media.Probe(await FlashAndBeep("probe early.mkv", -0.4));
        Assert.InRange(early.AudioOffset, -0.45, -0.35);
        Assert.Equal(0, (await media.Probe(media.VideoA)).AudioOffset, 2);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(-0.4)]
    public async Task Speech_and_pause_detection_use_picture_time(double audioDelay)
    {
        var file = await FlashAndBeep($"analyse {audioDelay}.mkv", audioDelay);
        var info = await media.Probe(file);
        var analysis = AccessibleVideoEditor.Core.Audio.SpeechDetector.Analyse(media.Tools, file, 0, 5, minimumPause: 0.5, audioOffset: info.AudioOffset);
        Assert.Single(analysis.Sound);
        // The beep is at 2.0 s from the start of the file, which is 2.0 s of picture time when the sound is
        // late and 1.6 s when the picture starts 0.4 s after the sound (the sound has an 80 ms margin each side).
        var expected = 2.0 - Math.Max(0, -audioDelay);
        Assert.InRange(analysis.Sound[0].Start, expected - 0.15, expected - 0.03);
    }
}
