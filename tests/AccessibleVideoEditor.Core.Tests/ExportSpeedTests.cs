using System.Diagnostics;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;
using Xunit.Abstractions;

namespace AccessibleVideoEditor.Core.Tests;

[Collection(MediaCollection.Name)]
public sealed class ExportSpeedTests(MediaFixture media, ITestOutputHelper output)
{
    [Fact]
    public void The_speed_choice_picks_the_encoder_and_the_video_chip_only_when_it_works()
    {
        Assert.Equal(new VideoEncoder("libx264", "medium"), VideoEncoders.Choose(ExportSpeed.BestQuality, ["h264_qsv"]));
        Assert.Equal("veryfast", VideoEncoders.Choose(ExportSpeed.Balanced, ["h264_qsv"]).X264Preset);
        Assert.Equal("h264_qsv", VideoEncoders.Choose(ExportSpeed.Fastest, ["h264_qsv", "h264_nvenc"]).Name);
        Assert.Equal(new VideoEncoder("libx264", "superfast", 2), VideoEncoders.Choose(ExportSpeed.Fastest, []));
        Assert.Contains("Quick Sync", new VideoEncoder("h264_qsv").Spoken);
        var qsv = VideoEncoders.Args(new VideoEncoder("h264_qsv"), 18, 30);
        Assert.Equal(["-c:v", "h264_qsv"], qsv.Take(2));
        Assert.Contains("-global_quality", qsv);
        Assert.Contains("veryfast", VideoEncoders.Args(VideoEncoders.Choose(ExportSpeed.Balanced, []), 18, 30));
        Assert.Contains("17", VideoEncoders.Args(VideoEncoders.Choose(ExportSpeed.Balanced, []), 18, 30)); // A little more quality for the faster preset.
    }

    [Fact]
    public async Task Encoders_listed_but_not_usable_are_not_offered()
    {
        var working = await VideoEncoders.DetectAsync(media.Tools);
        output.WriteLine($"Listed: {string.Join(", ", VideoEncoders.HardwareNames.Where(media.Tools.HasEncoder))}; working: {string.Join(", ", working)}");
        Assert.All(working, w => Assert.Contains(w, VideoEncoders.HardwareNames));
        // Every encoder reported as working really exports a file.
        foreach (var name in working)
        {
            var (file, _) = await Export(new VideoEncoder(name), $"hw {name}.mp4", 2);
            Assert.InRange((await media.Probe(file)).Duration, 1.8, 2.2);
        }
    }

    private async Task<(string File, double Seconds)> Export(VideoEncoder encoder, string name, double seconds)
    {
        var project = new Project { Width = 1920, Height = 1080, FrameRate = 30 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var item = await media.ItemFor(media.VideoB, LayerKind.Video);
        item.Duration = Math.Min(seconds, item.Duration);
        layer.Items.Add(item);
        while (layer.Items.Sum(i => i.Duration) < seconds - 0.01)
        {
            var copy = item.Clone();
            copy.Duration = Math.Min(item.Duration, seconds - layer.Items.Sum(i => i.Duration));
            layer.Items.Add(copy);
        }

        var clock = Stopwatch.StartNew();
        var (file, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.YouTube1080, OutputPath = media.OutPath(name), Encoder = encoder });
        return (file, clock.Elapsed.TotalSeconds);
    }

    [Fact]
    public async Task Faster_settings_export_faster_and_still_make_a_good_file()
    {
        var times = new Dictionary<ExportSpeed, double>();
        foreach (var speed in new[] { ExportSpeed.BestQuality, ExportSpeed.Balanced, ExportSpeed.Fastest })
        {
            var (file, seconds) = await Export(VideoEncoders.Choose(speed, []), $"speed {speed}.mp4", 9);
            var info = await media.Probe(file);
            Assert.InRange(info.Duration, 8.8, 9.2);
            Assert.Equal(1920, info.Width);
            times[speed] = seconds;
            output.WriteLine($"{speed}: {seconds:0.00} s for 9 s of 1080p, {new FileInfo(file).Length / 1024} KB");
        }

        Assert.True(times[ExportSpeed.Balanced] < times[ExportSpeed.BestQuality], $"Balanced {times[ExportSpeed.Balanced]:0.00} s vs best {times[ExportSpeed.BestQuality]:0.00} s");
        Assert.True(times[ExportSpeed.Fastest] < times[ExportSpeed.BestQuality], $"Fastest {times[ExportSpeed.Fastest]:0.00} s vs best {times[ExportSpeed.BestQuality]:0.00} s");
    }
}
