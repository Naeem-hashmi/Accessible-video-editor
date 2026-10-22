using System.Globalization;
using System.Text;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Rendering;

public sealed class RenderException(string message) : Exception(message);

/// <summary>Which layers to render.</summary>
public sealed record RenderScope(Guid? LayerId)
{
    /// <summary>Every layer that is not muted.</summary>
    public static RenderScope AllLayers { get; } = new((Guid?)null);

    /// <summary>One layer on its own, even if it is muted (for listening to just that layer).</summary>
    public static RenderScope Layer(Guid id) => new(id);

    public IReadOnlyList<Layer> Select(Project project) => LayerId is { } id
        ? project.Layers.Where(l => l.Id == id).ToList()
        : project.Layers.Where(l => !l.Muted).ToList();
}

public sealed class RenderRequest
{
    public required Project Project { get; init; }

    public RenderScope Scope { get; init; } = RenderScope.AllLayers;

    /// <summary>Timeline time to start from.</summary>
    public double Start { get; init; }

    /// <summary>Timeline time to stop at, or null for the end.</summary>
    public double? End { get; init; }

    public required OutputProfile Profile { get; init; }

    public required string OutputPath { get; init; }

    /// <summary>Compose at a smaller size than the project canvas (used for fast previews).</summary>
    public (int Width, int Height)? CanvasOverride { get; init; }
}

/// <summary>A ready-to-run FFmpeg job.</summary>
public sealed class RenderPlan
{
    internal RenderPlan(List<string> inputArguments, string filterGraph, List<string> outputArguments, double duration, bool hasVideo)
    {
        InputArguments = inputArguments;
        FilterGraph = filterGraph;
        OutputArguments = outputArguments;
        Duration = duration;
        HasVideo = hasVideo;
    }

    public IReadOnlyList<string> InputArguments { get; }

    public string FilterGraph { get; }

    public IReadOnlyList<string> OutputArguments { get; }

    /// <summary>Length of the result in seconds.</summary>
    public double Duration { get; }

    public bool HasVideo { get; }

    /// <summary>The full ffmpeg command line, reading the filter graph from <paramref name="filterScriptPath"/>.</summary>
    public IReadOnlyList<string> BuildArguments(string filterScriptPath, string filterScriptOption)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-loglevel", "error" };
        args.AddRange(InputArguments);
        args.Add(filterScriptOption);
        args.Add(filterScriptPath);
        args.AddRange(OutputArguments);
        return args;
    }
}

/// <summary>
/// Turns a project into one FFmpeg filter graph. Every layer becomes one stream: its items are normalised
/// (same size, frame rate and sound format), separated by generated gaps, and joined with concat or with
/// xfade/acrossfade for transitions. Video layers are then stacked bottom to top with overlay and all sound
/// is mixed with amix. All lengths are rounded to whole frames and samples so picture and sound stay in step.
/// </summary>
public sealed class GraphBuilder
{
    public const int SampleRate = 48000;

    private const string AudioFormat = "aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly Func<string, bool> _hasEncoder;
    private readonly List<string> _inputs = new();
    private readonly List<string> _chains = new();
    private int _inputCount;
    private int _labelCount;
    private double _fps;
    private int _width;
    private int _height;

    public GraphBuilder(Func<string, bool> hasEncoder)
    {
        _hasEncoder = hasEncoder;
    }

    public static bool ScopeHasVideo(Project project, RenderScope scope) =>
        scope.Select(project).Any(l => l.Kind == LayerKind.Video && l.Items.Count > 0);

    /// <summary>Media files the project needs that can't be found.</summary>
    public static IReadOnlyList<string> MissingFiles(IEnumerable<Layer> layers) => layers
        .SelectMany(l => l.Items)
        .Where(i => i.Kind != ItemKind.Color && !File.Exists(i.Path))
        .Select(i => i.Path)
        .Distinct()
        .ToList();

    public RenderPlan Build(RenderRequest request)
    {
        _inputs.Clear();
        _chains.Clear();
        _inputCount = 0;
        _labelCount = 0;

        var source = request.Project;
        _fps = source.FrameRate > 0 ? source.FrameRate : 30;
        (_width, _height) = request.CanvasOverride ?? (source.Width, source.Height);
        _width = Even(_width);
        _height = Even(_height);

        var windowed = ProjectWindow.StartingAt(source, Math.Max(0, request.Start));
        var layers = request.Scope.Select(windowed).Where(l => l.Items.Count > 0).ToList();
        if (layers.Count == 0)
        {
            throw new RenderException(request.Scope.LayerId is null
                ? "There is nothing to play here. All layers are empty or muted."
                : "This layer has nothing to play from here.");
        }

        var missing = MissingFiles(layers);
        if (missing.Count > 0)
        {
            throw new RenderException("These files can't be found: " + string.Join(", ", missing.Select(Path.GetFileName)) + ".");
        }

        var total = Timeline.Duration(layers);
        if (request.End is { } end)
        {
            total = Math.Min(total, end - Math.Max(0, request.Start));
        }

        if (total < 0.05)
        {
            throw new RenderException("There is nothing to play here.");
        }

        var totalFrames = Math.Max(1, (long)Math.Round(total * _fps));
        var totalSamples = (long)Math.Round(totalFrames / _fps * SampleRate);
        var outputVideo = request.Profile.HasVideo;

        var videoLabels = new List<string>();
        var audioLabels = new List<string>();
        foreach (var layer in layers)
        {
            var (video, audio) = BuildLayer(layer, outputVideo);
            if (video is not null)
            {
                videoLabels.Add(video);
            }

            audioLabels.Add(audio);
        }

        var outputArgs = new List<string>();
        if (outputVideo)
        {
            var current = Label("base");
            _chains.Add($"color=c=black:s={_width}x{_height}:r={Num(_fps)},format=yuv420p,trim=end_frame={totalFrames}[{current}]");
            foreach (var layerVideo in videoLabels)
            {
                var next = Label("stack");
                _chains.Add($"[{current}][{layerVideo}]overlay=x=0:y=0:eof_action=pass:format=auto[{next}]");
                current = next;
            }

            var finish = new StringBuilder($"[{current}]format=yuv420p");
            var (outW, outH) = OutputSize(request);
            if (outW != _width || outH != _height)
            {
                finish.Append(CultureInfo.InvariantCulture,
                    $",scale={outW}:{outH}:force_original_aspect_ratio=decrease:force_divisible_by=2,pad={outW}:{outH}:(ow-iw)/2:(oh-ih)/2:color=black");
            }

            finish.Append(",setsar=1[vout]");
            _chains.Add(finish.ToString());
            outputArgs.AddRange(["-map", "[vout]"]);
            outputArgs.AddRange(VideoCodecArgs(request.Profile));
        }

        var mix = new StringBuilder();
        if (audioLabels.Count == 1)
        {
            mix.Append(CultureInfo.InvariantCulture, $"[{audioLabels[0]}]anull");
        }
        else
        {
            foreach (var label in audioLabels)
            {
                mix.Append(CultureInfo.InvariantCulture, $"[{label}]");
            }

            mix.Append(CultureInfo.InvariantCulture, $"amix=inputs={audioLabels.Count}:duration=longest:dropout_transition=0:normalize=0");
        }

        mix.Append(CultureInfo.InvariantCulture,
            $",apad=whole_len={totalSamples},atrim=end_sample={totalSamples},alimiter=limit=0.98:attack=5:release=50:level=0,{AudioFormat}[aout]");
        _chains.Add(mix.ToString());
        outputArgs.AddRange(["-map", "[aout]"]);
        outputArgs.AddRange(AudioCodecArgs(request.Profile));

        var seconds = totalFrames / _fps;
        if (request.Profile.Extension == ".mp4" || request.Profile.Extension == ".m4a")
        {
            outputArgs.AddRange(["-movflags", "+faststart"]);
        }

        outputArgs.AddRange(["-t", Num(seconds), "-progress", "pipe:1", "-nostats", request.OutputPath]);
        return new RenderPlan(new List<string>(_inputs), string.Join(";\n", _chains), outputArgs, seconds, outputVideo);
    }

    // ------------------------------------------------------------------ layers

    private sealed record Segment(string? Video, string Audio, long Frames, long Samples, long OverlapFrames, long OverlapSamples, string VideoTransition, string AudioCurve);

    private (string? Video, string Audio) BuildLayer(Layer layer, bool outputVideo)
    {
        var withVideo = outputVideo && layer.Kind == LayerKind.Video;
        var spans = Timeline.Spans(layer);
        var segments = new List<Segment>();

        for (var i = 0; i < layer.Items.Count; i++)
        {
            var item = layer.Items[i];
            if (item.GapBefore > Timeline.Epsilon)
            {
                var gapFrames = Frames(item.GapBefore);
                if (gapFrames > 0)
                {
                    segments.Add(GapSegment(gapFrames, withVideo));
                }
            }

            var frames = Math.Max(1, Frames(item.Duration));
            var samples = Samples(frames);
            var overlap = spans[i].Overlap;
            var overlapFrames = overlap > 0 && frames > 1 ? Math.Clamp(Frames(overlap), 1, frames - 1) : 0;
            var video = withVideo ? ItemVideo(item, frames) : null;
            var audio = ItemAudio(item, samples);
            var curve = item.TransitionIn.Audio == TransitionSpec.None ? "tri" : item.TransitionIn.Audio;
            segments.Add(new Segment(video, audio, frames, samples, overlapFrames, Samples(overlapFrames),
                item.TransitionIn.Video, curve));
        }

        var (v, a) = JoinSegments(segments, withVideo);
        if (Math.Abs(layer.Volume - 1.0) > 1e-9)
        {
            var louder = Label("lvol");
            _chains.Add($"[{a}]volume={Num(layer.Volume)}[{louder}]");
            a = louder;
        }

        return (v, a);
    }

    private (string? Video, string Audio) JoinSegments(List<Segment> segments, bool withVideo)
    {
        string? accVideo = null;
        string? accAudio = null;
        long accFrames = 0;
        var pending = new List<Segment>();

        void Flush()
        {
            var parts = new List<(string? V, string A)>();
            if (accAudio is not null)
            {
                parts.Add((accVideo, accAudio));
            }

            parts.AddRange(pending.Select(p => (p.Video, p.Audio)));
            accFrames += pending.Sum(p => p.Frames);
            pending.Clear();
            if (parts.Count == 1)
            {
                (accVideo, accAudio) = parts[0];
                return;
            }

            if (parts.Count == 0)
            {
                return;
            }

            var inputs = new StringBuilder();
            foreach (var (pv, pa) in parts)
            {
                if (withVideo)
                {
                    inputs.Append(CultureInfo.InvariantCulture, $"[{pv}]");
                }

                inputs.Append(CultureInfo.InvariantCulture, $"[{pa}]");
            }

            var outAudio = Label("ja");
            if (withVideo)
            {
                // FFmpeg 7 loses the frame rate after concat; xfade needs it, so state it again.
                var joined = Label("jc");
                var outVideo = Label("jv");
                _chains.Add($"{inputs}concat=n={parts.Count}:v=1:a=1[{joined}][{outAudio}]");
                _chains.Add($"[{joined}]fps={Num(_fps)}[{outVideo}]");
                accVideo = outVideo;
            }
            else
            {
                _chains.Add($"{inputs}concat=n={parts.Count}:v=0:a=1[{outAudio}]");
            }

            accAudio = outAudio;
        }

        foreach (var segment in segments)
        {
            if (segment.OverlapFrames > 0 && (accAudio is not null || pending.Count > 0))
            {
                Flush();
                var offsetFrames = accFrames - segment.OverlapFrames;
                if (withVideo)
                {
                    var outVideo = Label("xv");
                    _chains.Add(
                        $"[{accVideo}][{segment.Video}]xfade=transition={segment.VideoTransition}:duration={Num(segment.OverlapFrames / _fps)}:offset={Num(offsetFrames / _fps)},fps={Num(_fps)}[{outVideo}]");
                    accVideo = outVideo;
                }

                var outAudio = Label("xa");
                _chains.Add(
                    $"[{accAudio}][{segment.Audio}]acrossfade=ns={segment.OverlapSamples}:c1={segment.AudioCurve}:c2={segment.AudioCurve}[{outAudio}]");
                accAudio = outAudio;
                accFrames += segment.Frames - segment.OverlapFrames;
            }
            else
            {
                pending.Add(segment);
            }
        }

        Flush();
        return (accVideo, accAudio!);
    }

    private Segment GapSegment(long frames, bool withVideo)
    {
        string? video = null;
        if (withVideo)
        {
            video = Label("gv");
            _chains.Add($"color=c=black@0.0:s={_width}x{_height}:r={Num(_fps)},format=yuva420p,trim=end_frame={frames},setpts=PTS-STARTPTS,fps={Num(_fps)}[{video}]");
        }

        var audio = Label("ga");
        _chains.Add(Silence(Samples(frames), audio));
        return new Segment(video, audio, frames, Samples(frames), 0, 0, TransitionSpec.None, "tri");
    }

    // ------------------------------------------------------------------ items

    private string ItemVideo(Item item, long frames)
    {
        var label = Label("iv");
        var chain = new StringBuilder();
        if (item.Kind == ItemKind.Color)
        {
            chain.Append(CultureInfo.InvariantCulture,
                $"color=c=0x{ColorCatalog.HexFor(item.ColorName)}:s={_width}x{_height}:r={Num(_fps)},format=yuva420p");
        }
        else
        {
            var input = AddInput(item, forVideo: true);
            chain.Append(CultureInfo.InvariantCulture, $"[{input}:v:0]setpts=PTS-STARTPTS,fps={Num(_fps)}");
        }

        var (boxW, boxH) = BoxSize(item.Placement);
        var (x, y) = PadPosition(item.Placement);
        chain.Append(CultureInfo.InvariantCulture,
            $",scale={boxW}:{boxH}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,format=yuva420p");
        if (item.Placement.OpacityPercent < 99.95)
        {
            chain.Append(CultureInfo.InvariantCulture,
                $",format=rgba,colorchannelmixer=aa={Num(Math.Clamp(item.Placement.OpacityPercent, 0, 100) / 100)},format=yuva420p");
        }

        chain.Append(CultureInfo.InvariantCulture, $",pad={_width}:{_height}:'{x}':'{y}':color=black@0.0");
        chain.Append(CultureInfo.InvariantCulture,
            $",tpad=stop_mode=clone:stop_duration={Num(frames / _fps + 1)},trim=end_frame={frames},setpts=PTS-STARTPTS,fps={Num(_fps)}");

        var length = frames / _fps;
        var fadeIn = Math.Min(item.FadeIn, length / 2);
        var fadeOut = Math.Min(item.FadeOut, length / 2);
        if (fadeIn > 0.01)
        {
            chain.Append(CultureInfo.InvariantCulture, $",fade=t=in:st=0:d={Num(fadeIn)}:alpha=1");
        }

        if (fadeOut > 0.01)
        {
            chain.Append(CultureInfo.InvariantCulture, $",fade=t=out:st={Num(length - fadeOut)}:d={Num(fadeOut)}:alpha=1");
        }

        chain.Append(CultureInfo.InvariantCulture, $"[{label}]");
        _chains.Add(chain.ToString());
        return label;
    }

    private string ItemAudio(Item item, long samples)
    {
        var label = Label("ia");
        var usable = item.HasAudio && !item.AudioMuted && item.Kind is ItemKind.Video or ItemKind.Audio && item.Volume > 0;
        if (!usable)
        {
            _chains.Add(Silence(samples, label));
            return label;
        }

        var input = AddInput(item, forVideo: false);
        var chain = new StringBuilder();
        chain.Append(CultureInfo.InvariantCulture, $"[{input}:a:0]asetpts=PTS-STARTPTS,aresample={SampleRate},{AudioFormat}");
        foreach (var filter in AudioFilters.For(item.Audio))
        {
            chain.Append(',').Append(filter);
        }

        if (Math.Abs(item.Volume - 1.0) > 1e-9)
        {
            chain.Append(CultureInfo.InvariantCulture, $",volume={Num(item.Volume)}");
        }

        chain.Append(CultureInfo.InvariantCulture,
            $",{AudioFormat},apad=whole_len={samples},atrim=end_sample={samples},asetpts=PTS-STARTPTS");

        var length = samples / (double)SampleRate;
        var fadeIn = Math.Min(item.FadeIn, length / 2);
        var fadeOut = Math.Min(item.FadeOut, length / 2);
        if (fadeIn > 0.01)
        {
            chain.Append(CultureInfo.InvariantCulture, $",afade=t=in:st=0:d={Num(fadeIn)}");
        }

        if (fadeOut > 0.01)
        {
            chain.Append(CultureInfo.InvariantCulture, $",afade=t=out:st={Num(length - fadeOut)}:d={Num(fadeOut)}");
        }

        chain.Append(CultureInfo.InvariantCulture, $"[{label}]");
        _chains.Add(chain.ToString());
        return label;
    }

    private static string Silence(long samples, string label) =>
        $"anullsrc=r={SampleRate}:cl=stereo,{AudioFormat},atrim=end_sample={samples},asetpts=PTS-STARTPTS[{label}]";

    /// <summary>Adds a media file as an FFmpeg input, seeking to the item's start, and returns its index.</summary>
    private int AddInput(Item item, bool forVideo)
    {
        if (item.Kind == ItemKind.Image)
        {
            _inputs.AddRange(["-loop", "1", "-framerate", Num(_fps), "-t", Num(item.Duration + 1), "-i", item.Path]);
        }
        else
        {
            if (item.SourceIn > 0.0005)
            {
                _inputs.AddRange(["-ss", Num(item.SourceIn)]);
            }

            _inputs.AddRange(["-t", Num(item.Duration + 1)]);
            // Each input feeds either picture or sound; skip decoding the other.
            _inputs.Add(forVideo ? "-an" : "-vn");

            _inputs.AddRange(["-i", item.Path]);
        }

        return _inputCount++;
    }

    // ------------------------------------------------------------------ geometry

    private (int Width, int Height) BoxSize(Placement placement)
    {
        if (placement.Position == PositionPreset.FullFrame)
        {
            return (_width, _height);
        }

        var scale = Math.Clamp(placement.SizePercent, 5, 100) / 100.0;
        return (Math.Max(2, Even(_width * scale)), Math.Max(2, Even(_height * scale)));
    }

    /// <summary>FFmpeg pad expressions placing the scaled picture; always kept inside the frame.</summary>
    private (string X, string Y) PadPosition(Placement placement)
    {
        var margin = Even(Math.Min(_width, _height) * 0.04);
        var m = margin.ToString(Inv);
        const string center = "(ow-iw)/2";
        const string middle = "(oh-ih)/2";
        var right = $"ow-iw-{m}";
        var bottom = $"oh-ih-{m}";

        var (x, y) = placement.Position switch
        {
            PositionPreset.TopLeft => (m, m),
            PositionPreset.TopCenter => (center, m),
            PositionPreset.TopRight => (right, m),
            PositionPreset.MiddleLeft => (m, middle),
            PositionPreset.MiddleRight => (right, middle),
            PositionPreset.BottomLeft => (m, bottom),
            PositionPreset.BottomCenter => (center, bottom),
            PositionPreset.BottomRight => (right, bottom),
            PositionPreset.Custom => (Num(_width * Math.Clamp(placement.CustomXPercent, 0, 100) / 100),
                Num(_height * Math.Clamp(placement.CustomYPercent, 0, 100) / 100)),
            _ => (center, middle),
        };

        return ($"max(0,min(ow-iw,{x}))", $"max(0,min(oh-ih,{y}))");
    }

    private (int Width, int Height) OutputSize(RenderRequest request)
    {
        var profile = request.Profile;
        if (profile.Width <= 0 || profile.Height <= 0 || request.CanvasOverride is not null)
        {
            return (_width, _height);
        }

        return (Even(profile.Width), Even(profile.Height));
    }

    // ------------------------------------------------------------------ codecs

    private IEnumerable<string> VideoCodecArgs(OutputProfile profile)
    {
        var rate = Num(_fps);
        if (_hasEncoder("libx264"))
        {
            return ["-c:v", "libx264", "-preset", profile.X264Preset, "-crf", profile.Crf.ToString(Inv),
                "-pix_fmt", "yuv420p", "-r", rate];
        }

        if (OperatingSystem.IsWindows() && _hasEncoder("h264_mf"))
        {
            return ["-c:v", "h264_mf", "-b:v", profile.Crf <= 18 ? "12M" : "6M", "-pix_fmt", "nv12", "-r", rate];
        }

        return ["-c:v", "mpeg4", "-q:v", "3", "-pix_fmt", "yuv420p", "-r", rate];
    }

    private IEnumerable<string> AudioCodecArgs(OutputProfile profile)
    {
        var codec = profile.AudioCodec;
        if (codec == "libmp3lame" && !_hasEncoder("libmp3lame"))
        {
            throw new RenderException("This copy of FFmpeg can't make MP3 files. Choose M4A or WAV instead.");
        }

        var args = new List<string> { "-c:a", codec, "-ar", SampleRate.ToString(Inv), "-ac", "2" };
        if (!string.IsNullOrEmpty(profile.AudioBitrate))
        {
            args.AddRange(["-b:a", profile.AudioBitrate]);
        }

        return args;
    }

    // ------------------------------------------------------------------ helpers

    private long Frames(double seconds) => (long)Math.Round(seconds * _fps);

    private long Samples(long frames) => (long)Math.Round(frames / _fps * SampleRate);

    private string Label(string prefix) => $"{prefix}{_labelCount++}";

    private static int Even(double value) => Math.Max(2, (int)Math.Round(value / 2) * 2);

    internal static string Num(double value) => value.ToString("0.#########", Inv);
}
