using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Live preview streams: FFmpeg writes raw sound and frames to a pipe as it renders.</summary>
[Collection(MediaCollection.Name)]
public sealed class StreamingTests(MediaFixture media)
{
    private async Task<Project> TwoClips()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(await media.ItemFor(media.VideoA, LayerKind.Video));
        layer.Items.Add(await media.ItemFor(media.VideoB, LayerKind.Video));
        return project;
    }

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

    [Theory]
    [InlineData(0, 1, 6.0)]
    [InlineData(2, 1, 4.0)]
    [InlineData(0, 2, 3.0)]
    [InlineData(0, 4, 1.5)]
    public async Task Sound_streams_as_raw_pcm_for_the_right_length(double start, double speed, double expectedSeconds)
    {
        var plan = new GraphBuilder(media.Tools.HasEncoder, media.Tools.Text).Build(new RenderRequest
        {
            Project = await TwoClips(), Start = start, Speed = speed, Profile = OutputProfile.StreamAudio, OutputPath = "pipe:1",
        });
        using var stream = FfmpegStream.Start(media.Tools, plan);
        var bytes = ReadAll(stream);
        var seconds = bytes / (48000.0 * 4);
        Assert.InRange(seconds, expectedSeconds - 0.1, expectedSeconds + 0.1);
    }

    [Fact]
    public async Task Picture_streams_as_whole_bgra_frames_at_15_per_second()
    {
        var plan = new GraphBuilder(media.Tools.HasEncoder, media.Tools.Text).Build(new RenderRequest
        {
            Project = await TwoClips(), Profile = OutputProfile.StreamVideo, OutputPath = "pipe:1", CanvasOverride = (160, 90),
        });
        using var stream = FfmpegStream.Start(media.Tools, plan);
        var bytes = ReadAll(stream);
        const int frame = 160 * 90 * 4;
        Assert.Equal(0, bytes % frame);
        Assert.InRange(bytes / frame, 88, 92);
    }

    [Fact]
    public async Task The_first_sound_arrives_quickly()
    {
        var plan = new GraphBuilder(media.Tools.HasEncoder, media.Tools.Text).Build(new RenderRequest
        {
            Project = await TwoClips(), Profile = OutputProfile.StreamAudio, OutputPath = "pipe:1",
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var stream = FfmpegStream.Start(media.Tools, plan);
        var buffer = new byte[48000 * 4 / 10];
        Assert.True(stream.ReadExactly(buffer, CancellationToken.None));
        Assert.True(clock.Elapsed.TotalSeconds < 3, $"First tenth of a second took {clock.Elapsed.TotalSeconds:0.00} s.");
    }

    [Fact]
    public async Task Stopping_a_stream_early_cleans_up()
    {
        var plan = new GraphBuilder(media.Tools.HasEncoder, media.Tools.Text).Build(new RenderRequest
        {
            Project = await TwoClips(), Profile = OutputProfile.StreamAudio, OutputPath = "pipe:1",
        });
        var stream = FfmpegStream.Start(media.Tools, plan);
        Assert.True(stream.ReadExactly(new byte[4096], CancellationToken.None));
        stream.Dispose();
        Assert.True(stream.HasExited);
    }
}
