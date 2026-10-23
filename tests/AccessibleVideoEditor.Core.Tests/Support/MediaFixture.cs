using System.Diagnostics;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.Core.Tests.Support;

/// <summary>
/// Generates small real media files with FFmpeg's built-in test sources, once per test run.
/// Set FFMPEG_PATH to test a specific FFmpeg build.
/// </summary>
public sealed class MediaFixture : IAsyncLifetime
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "ave-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public FfmpegTools Tools { get; private set; } = null!;

    /// <summary>3 s, 320x240, 30 fps test pattern with a 440 Hz tone.</summary>
    public string VideoA => Path.Combine(Folder, "video a.mp4");

    /// <summary>3 s, 640x480, 25 fps colour bars with an 880 Hz tone (different size and rate on purpose).</summary>
    public string VideoB => Path.Combine(Folder, "video b.mp4");

    /// <summary>2 s video without any sound.</summary>
    public string Silent => Path.Combine(Folder, "silent.mp4");

    /// <summary>Solid red 200x200 picture.</summary>
    public string RedPng => Path.Combine(Folder, "red.png");

    /// <summary>Blue 800x600 photo.</summary>
    public string BlueJpg => Path.Combine(Folder, "blue photo.jpg");

    /// <summary>6 s, 330 Hz tone with some noise.</summary>
    public string Music => Path.Combine(Folder, "music.wav");

    /// <summary>4 s AAC tone.</summary>
    public string Voice => Path.Combine(Folder, "voice.m4a");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Folder);
        Tools = await FfmpegTools.LocateAsync();
        await Make("-f", "lavfi", "-i", "testsrc=size=320x240:rate=30:duration=3", "-f", "lavfi", "-i",
            "sine=frequency=440:sample_rate=44100:duration=3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", VideoA);
        await Make("-f", "lavfi", "-i", "smptebars=size=640x480:rate=25:duration=3", "-f", "lavfi", "-i",
            "sine=frequency=880:sample_rate=48000:duration=3", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", VideoB);
        await Make("-f", "lavfi", "-i", "testsrc2=size=320x240:rate=30:duration=2", "-c:v", "libx264", "-pix_fmt", "yuv420p", Silent);
        await Make("-f", "lavfi", "-i", "color=c=red:size=200x200", "-frames:v", "1", RedPng);
        await Make("-f", "lavfi", "-i", "color=c=blue:size=800x600", "-frames:v", "1", BlueJpg);
        await Make("-f", "lavfi", "-i", "sine=frequency=330:sample_rate=44100:duration=6", "-f", "lavfi", "-i",
            "anoisesrc=a=0.02:duration=6", "-filter_complex", "[0][1]amix=inputs=2", "-ac", "2", Music);
        await Make("-f", "lavfi", "-i", "sine=frequency=200:sample_rate=48000:duration=4", "-c:a", "aac", Voice);
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }

        return Task.CompletedTask;
    }

    public string OutPath(string name) => Path.Combine(Folder, "out", name);

    public async Task<Item> ItemFor(string path, LayerKind layerKind, LayerDefaults? defaults = null)
    {
        var info = await MediaProbe.ProbeAsync(Tools, path);
        return MediaProbe.CreateItem(info, defaults ?? new LayerDefaults(), layerKind);
    }

    /// <summary>Renders and returns the output path, asserting FFmpeg succeeded.</summary>
    public async Task<(string Path, RenderPlan Plan)> Render(RenderRequest request)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
        var plan = new GraphBuilder(Tools.HasEncoder, Tools.Text).Build(request);
        await RenderRunner.RunAsync(Tools, plan, request.OutputPath);
        Assert.True(File.Exists(request.OutputPath), "Output file was not created.");
        return (request.OutputPath, plan);
    }

    public async Task<MediaInfo> Probe(string path) => await MediaProbe.ProbeAsync(Tools, path);

    /// <summary>The RGB colour of one pixel of the frame at <paramref name="time"/>.</summary>
    public async Task<(int R, int G, int B)> PixelAt(string video, double time, int x, int y)
    {
        var info = await Probe(video);
        var bytes = await RunBytes("-v", "error", "-ss", time.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-i", video, "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "rgb24", "-");
        var offset = (y * info.Width + x) * 3;
        Assert.True(bytes.Length >= offset + 3, $"Frame too small: {bytes.Length} bytes.");
        return (bytes[offset], bytes[offset + 1], bytes[offset + 2]);
    }

    /// <summary>Peak level of the audio between two times, in dBFS (very negative means silence).</summary>
    public async Task<double> PeakDb(string media, double from, double to)
    {
        var text = await RunText("-hide_banner", "-nostats", "-ss", Num(from), "-t", Num(to - from), "-i", media,
            "-af", "volumedetect", "-vn", "-f", "null", "-");
        var line = text.Split('\n').FirstOrDefault(l => l.Contains("max_volume:"));
        if (line is null)
        {
            return double.NegativeInfinity;
        }

        var value = line[(line.IndexOf("max_volume:", StringComparison.Ordinal) + 11)..].Replace("dB", "").Trim();
        return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Num(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private async Task Make(params string[] args)
    {
        var full = new List<string> { "-hide_banner", "-loglevel", "error", "-y" };
        full.AddRange(args);
        var (code, _, err) = await Run(full);
        Assert.True(code == 0, "Fixture generation failed: " + err);
    }

    private async Task<byte[]> RunBytes(params string[] args)
    {
        var psi = Start(args);
        using var p = Process.Start(psi)!;
        using var ms = new MemoryStream();
        var errTask = p.StandardError.ReadToEndAsync();
        await p.StandardOutput.BaseStream.CopyToAsync(ms);
        await p.WaitForExitAsync();
        await errTask;
        return ms.ToArray();
    }

    private async Task<string> RunText(params string[] args)
    {
        var (_, output, error) = await Run(args);
        return output + error;
    }

    private async Task<(int Code, string Out, string Err)> Run(IEnumerable<string> args)
    {
        using var p = Process.Start(Start(args))!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, await output, await error);
    }

    private ProcessStartInfo Start(IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(Tools.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        return psi;
    }
}

[CollectionDefinition(Name)]
public sealed class MediaCollection : ICollectionFixture<MediaFixture>
{
    public const string Name = "Media";
}
