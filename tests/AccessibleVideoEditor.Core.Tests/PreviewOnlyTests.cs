using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Live preview and export of projects with no video clip: only sound, only pictures, only text.</summary>
[Collection(MediaCollection.Name)]
public sealed class PreviewOnlyTests(MediaFixture media)
{
    private static long ReadAll(FfmpegStream stream)
    {
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = stream.Output.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
        }

        return total;
    }

    private (double Sound, long Frames, string Errors) Stream(Project project, RenderScope? scope = null, double start = 0)
    {
        scope ??= RenderScope.AllLayers;
        var sound = new GraphBuilder(media.Tools.HasEncoder, media.Tools.Text).Build(new RenderRequest
        {
            Project = project, Scope = scope, Start = start, Profile = OutputProfile.StreamAudio, OutputPath = "pipe:1",
        });
        using var s = FfmpegStream.Start(media.Tools, sound);
        var seconds = ReadAll(s) / (48000.0 * 4);
        long frames = 0;
        if (GraphBuilder.ScopeHasVideo(project, scope))
        {
            var picture = new GraphBuilder(media.Tools.HasEncoder, media.Tools.Text).Build(new RenderRequest
            {
                Project = project, Scope = scope, Start = start, Profile = OutputProfile.StreamVideo, OutputPath = "pipe:1", CanvasOverride = (160, 90),
            });
            using var p = FfmpegStream.Start(media.Tools, picture);
            frames = ReadAll(p) / (160 * 90 * 4);
        }

        return (seconds, frames, s.Errors);
    }

    [Fact]
    public async Task Only_an_audio_layer_plays()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        layer.Items.Add(await media.ItemFor(media.Voice, LayerKind.Audio));
        var (sound, _, errors) = Stream(project);
        Assert.True(sound > 3.8, $"{sound:0.00} s of sound. {errors}");
    }

    [Fact]
    public async Task An_mp3_with_cover_art_is_sound_and_plays_in_full()
    {
        var mp3 = await media.Generate("song with cover.mp3", "-f", "lavfi", "-i", "sine=frequency=550:duration=5", "-f", "lavfi", "-i",
            "testsrc=size=300x300:rate=1:duration=1", "-map", "0:a", "-map", "1:v", "-c:v", "mjpeg", "-disposition:v", "attached_pic", "-c:a", "libmp3lame");
        var info = await media.Probe(mp3);
        Assert.Equal(ItemKind.Audio, info.Kind); // The cover picture is not a video.
        Assert.InRange(info.Duration, 4.9, 5.2);
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        ProjectEditor.AddLayer(project, LayerKind.Audio).Items.Add(await media.ItemFor(mp3, LayerKind.Audio));
        var (sound, _, errors) = Stream(project);
        Assert.True(sound > 4.8, $"{sound:0.00} s of sound. {errors}");
    }

    [Fact]
    public async Task An_empty_video_layer_beside_audio_still_plays()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        ProjectEditor.AddLayer(project, LayerKind.Video);
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        layer.Items.Add(await media.ItemFor(media.Voice, LayerKind.Audio));
        var (sound, _, errors) = Stream(project);
        Assert.True(sound > 3.8, $"{sound:0.00} s of sound. {errors}");
    }

    [Fact]
    public async Task Only_pictures_play_as_a_slideshow_with_silence()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var red = await media.ItemFor(media.RedPng, LayerKind.Video);
        red.Duration = 2;
        var blue = await media.ItemFor(media.BlueJpg, LayerKind.Video);
        blue.Duration = 3;
        layer.Items.Add(red);
        layer.Items.Add(blue);
        var (sound, frames, errors) = Stream(project);
        Assert.True(sound > 4.8, $"{sound:0.00} s of sound (silence). {errors}");
        Assert.InRange(frames, 73, 77);
    }

    [Fact]
    public async Task Only_sound_exports_as_a_video_with_a_black_picture()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        ProjectEditor.AddLayer(project, LayerKind.Audio).Items.Add(await media.ItemFor(media.Voice, LayerKind.Audio));
        var (file, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("sound only.mp4") });
        var info = await media.Probe(file);
        Assert.True(info.HasAudio && info.Width == 320, $"{info.Width}x{info.Height}, sound {info.HasAudio}");
        Assert.InRange(info.Duration, 3.8, 4.2);
    }

    [Fact]
    public async Task Only_pictures_export_with_their_places_and_lengths()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var red = await media.ItemFor(media.RedPng, LayerKind.Video);
        red.Duration = 2;
        var blue = await media.ItemFor(media.BlueJpg, LayerKind.Video);
        blue.Duration = 3;
        blue.Placement.Position = PositionPreset.TopRight;
        blue.Placement.SizePercent = 40;
        layer.Items.Add(red);
        layer.Items.Add(blue);
        var (file, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("pictures only.mp4") });
        var info = await media.Probe(file);
        Assert.InRange(info.Duration, 4.8, 5.2);
        Assert.True(info.HasAudio, "A silent sound track is included, so every player accepts the file");
        var (r, _, _) = await media.PixelAt(file, 1, 160, 90);
        Assert.True(r > 200, "The red picture fills the frame first");
        var (_, _, b) = await media.PixelAt(file, 3.5, 300, 20);
        var (r2, g2, b2) = await media.PixelAt(file, 3.5, 20, 160);
        Assert.True(b > 150 && r2 < 40 && g2 < 40 && b2 < 40, "Then the blue photo sits top right, small, on black");
    }
}
