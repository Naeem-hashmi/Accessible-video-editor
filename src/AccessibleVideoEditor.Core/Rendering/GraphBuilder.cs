using System.Globalization;
using System.Text;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Rendering;

public sealed class RenderException(string message) : Exception(message);

/// <summary>A file FFmpeg needs next to the filter graph: text to draw, or a font to copy.</summary>
public sealed record AuxiliaryFile(string? Content, string? CopyFrom);

/// <summary>Whether this FFmpeg can draw text, and how to find fonts.</summary>
/// <param name="Available">FFmpeg has the drawtext filter.</param>
/// <param name="SupportsAlign">drawtext has the text_align option (FFmpeg 6.1 and later).</param>
/// <param name="ResolveFont">Font name and bold flag to a font file path.</param>
public sealed record TextRendering(bool Available, bool SupportsAlign, Func<string, bool, string?> ResolveFont)
{
    public static TextRendering Unavailable { get; } = new(false, false, (_, _) => null);
}

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

    /// <summary>The video encoder for an export, from <see cref="VideoEncoders.Choose"/>. Null uses x264 at the profile's preset.</summary>
    public VideoEncoder? Encoder { get; init; }

    /// <summary>Playback speed for live preview: 1, 2 or 4.</summary>
    public double Speed { get; init; } = 1;

    /// <summary>Compose at a smaller size than the project canvas (used for fast previews).</summary>
    public (int Width, int Height)? CanvasOverride { get; init; }

    /// <summary>
    /// Gain for the whole mix in decibels, measured beforehand by <see cref="Loudness"/> so the export
    /// reaches <see cref="Project.MasterLoudness"/> exactly. Null uses a one-pass estimate instead.
    /// </summary>
    public double? MasterGainDb { get; init; }

    /// <summary>Leave out the loudness levelling (used when measuring).</summary>
    public bool SkipMasterLoudness { get; init; }
}

/// <summary>A ready-to-run FFmpeg job.</summary>
public sealed class RenderPlan
{
    internal RenderPlan(List<string> inputArguments, string filterGraph, List<string> outputArguments, double duration, bool hasVideo,
        IReadOnlyDictionary<string, AuxiliaryFile> auxiliaryFiles)
    {
        AuxiliaryFiles = auxiliaryFiles;
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

    /// <summary>Files (by name) that must sit in FFmpeg's working folder: text to draw and fonts.</summary>
    public IReadOnlyDictionary<string, AuxiliaryFile> AuxiliaryFiles { get; }

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

    private readonly TextRendering _text;
    private readonly Dictionary<string, AuxiliaryFile> _aux = new();
    private readonly Dictionary<string, string> _fontNames = new();

    public GraphBuilder(Func<string, bool> hasEncoder, TextRendering? text = null)
    {
        _hasEncoder = hasEncoder;
        _text = text ?? TextRendering.Unavailable;
    }

    public static bool ScopeHasVideo(Project project, RenderScope scope) =>
        scope.Select(project).Any(l => l.Kind == LayerKind.Video && l.Items.Count > 0);

    /// <summary>Media files the project needs that can't be found.</summary>
    public static IReadOnlyList<string> MissingFiles(IEnumerable<Layer> layers) => layers
        .SelectMany(l => l.Items)
        .Where(i => i.Kind is not (ItemKind.Color or ItemKind.Text) && !File.Exists(i.Path))
        .Select(i => i.Path)
        .Distinct()
        .ToList();

    public RenderPlan Build(RenderRequest request)
    {
        _inputs.Clear();
        _chains.Clear();
        _aux.Clear();
        _fontNames.Clear();
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

        if (request.Profile.HasVideo && !_text.Available && layers.Any(l => l.Kind == LayerKind.Video && l.Items.Any(i => i.Kind == ItemKind.Text)))
        {
            throw new RenderException("This copy of FFmpeg can't draw text. Reinstall the program, or mute the text layer.");
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
        var speed = request.Speed is 2 or 4 ? request.Speed : 1;

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

            if (Math.Abs(speed - 1) > 1e-9)
            {
                finish.Append(CultureInfo.InvariantCulture, $",setpts=PTS/{Num(speed)}");
            }

            if (request.Profile.Kind == OutputKind.StreamVideo)
            {
                finish.Append(CultureInfo.InvariantCulture, $",fps={OutputProfile.StreamFrameRate},format=bgra");
            }

            finish.Append(",setsar=1[vout]");
            _chains.Add(finish.ToString());
            outputArgs.AddRange(["-map", "[vout]"]);
            outputArgs.AddRange(request.Profile.Kind == OutputKind.StreamVideo
                ? ["-c:v", "rawvideo", "-pix_fmt", "bgra", "-f", "rawvideo"]
                : VideoCodecArgs(request.Profile, request.Encoder));
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

        var leveled = request.Project.MasterLoudness is not null && !request.Profile.IsStream && !request.SkipMasterLoudness;
        if (leveled && request.MasterGainDb is { } gain)
        {
            mix.Append(CultureInfo.InvariantCulture, $",volume={Num(Math.Clamp(gain, -40, 40))}dB");
        }
        else if (leveled)
        {
            mix.Append(CultureInfo.InvariantCulture, $",loudnorm=I={Num(Math.Clamp(request.Project.MasterLoudness!.Value, -30, -5))}:TP=-1.5:LRA=11,aresample={SampleRate}");
        }

        // A levelled mix keeps 1 dB of headroom, as streaming sites ask; otherwise just stop clipping.
        var limit = leveled ? "0.891" : "0.98";
        mix.Append(CultureInfo.InvariantCulture,
            $",apad=whole_len={totalSamples},atrim=end_sample={totalSamples},alimiter=limit={limit}:attack=5:release=50:level=0");
        for (var s = speed; s > 1.0001; s /= 2)
        {
            // atempo changes speed without changing pitch; one step doubles it.
            mix.Append(",atempo=2");
        }

        if (request.Profile.Kind == OutputKind.StreamVideo)
        {
            // The picture stream has no sound output; the sound is finished off inside the graph.
            mix.Append(",anullsink");
            _chains.Add(mix.ToString());
        }
        else
        {
            mix.Append(CultureInfo.InvariantCulture, $",{AudioFormat}[aout]");
            _chains.Add(mix.ToString());
            outputArgs.AddRange(["-map", "[aout]"]);
            outputArgs.AddRange(request.Profile.Kind == OutputKind.StreamAudio
                ? ["-c:a", "pcm_s16le", "-ar", SampleRate.ToString(Inv), "-ac", "2", "-f", "s16le"]
                : AudioCodecArgs(request.Profile));
        }

        var seconds = totalFrames / _fps / speed;
        if (request.Profile.Extension == ".mp4" || request.Profile.Extension == ".m4a")
        {
            outputArgs.AddRange(["-movflags", "+faststart"]);
        }

        if (request.Profile.IsStream)
        {
            // Standard output carries the media itself, so no progress lines there.
            outputArgs.AddRange(["-t", Num(seconds), "-nostats", "pipe:1"]);
        }
        else
        {
            outputArgs.AddRange(["-t", Num(seconds), "-progress", "pipe:1", "-nostats", Path.GetFullPath(request.OutputPath)]);
        }
        return new RenderPlan(new List<string>(_inputs), string.Join(";\n", _chains), outputArgs, seconds, outputVideo,
            new Dictionary<string, AuxiliaryFile>(_aux));
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
        if (item.Kind == ItemKind.Text)
        {
            return TextVideo(item, frames);
        }

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
            var speed = item.EffectiveSpeed;
            var pts = Math.Abs(speed - 1) > 1e-6 ? $"(PTS-STARTPTS)/{Num(speed)}" : "PTS-STARTPTS";
            chain.Append(CultureInfo.InvariantCulture, $"[{input}:v:0]setpts={pts},fps={Num(_fps)}");
        }

        var placement = item.Placement;
        if (placement.HasCrop && item.Kind != ItemKind.Color)
        {
            var left = Math.Clamp(placement.CropLeft, 0, 90) / 100;
            var right = Math.Clamp(placement.CropRight, 0, 90 - left * 100) / 100;
            var top = Math.Clamp(placement.CropTop, 0, 90) / 100;
            var bottom = Math.Clamp(placement.CropBottom, 0, 90 - top * 100) / 100;
            chain.Append(CultureInfo.InvariantCulture,
                $",crop=w=iw*{Num(1 - left - right)}:h=ih*{Num(1 - top - bottom)}:x=iw*{Num(left)}:y=ih*{Num(top)}");
        }

        var (boxW, boxH) = BoxSize(placement);
        var (x, y) = PadPosition(placement);
        if (placement.Position == PositionPreset.FullFrame && placement.Fill)
        {
            chain.Append(CultureInfo.InvariantCulture,
                $",scale={_width}:{_height}:force_original_aspect_ratio=increase,crop={_width}:{_height},setsar=1,format=yuva420p");
        }
        else
        {
            chain.Append(CultureInfo.InvariantCulture,
                $",scale={boxW}:{boxH}:force_original_aspect_ratio=decrease:force_divisible_by=2,setsar=1,format=yuva420p");
        }
        if (placement.CornerRadiusPercent > 0.01)
        {
            // Rounded corners: pixels outside a quarter circle at each corner become transparent.
            var k = Num(Math.Clamp(placement.CornerRadiusPercent, 0, 50) / 100);
            var r = $"(min(W,H)*{k})";
            chain.Append(CultureInfo.InvariantCulture,
                $",geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='alpha(X,Y)*lte(pow(max(0,max({r}-X,X-(W-1-{r}))),2)+pow(max(0,max({r}-Y,Y-(H-1-{r}))),2),{r}*{r})'");
        }

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
        if (AudioAlignment.Align(item.AudioOffset, item.SourceIn) is { } delay)
        {
            chain.Append(',').Append(delay);
        }

        if (Tempo(item.EffectiveSpeed) is { } tempo)
        {
            chain.Append(',').Append(tempo);
        }

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
        if (DipExpression(item, length) is { } dips)
        {
            chain.Append(CultureInfo.InvariantCulture, $",volume='{dips}':eval=frame");
        }

        var fadeIn = Math.Min(Math.Max(item.FadeIn, item.SoundJoinFade), length / 2);
        var fadeOut = Math.Min(Math.Max(item.FadeOut, item.SoundJoinFade), length / 2);
        if (fadeIn > 0.001)
        {
            chain.Append(CultureInfo.InvariantCulture, $",afade=t=in:st=0:d={Num(fadeIn)}");
        }

        if (fadeOut > 0.001)
        {
            chain.Append(CultureInfo.InvariantCulture, $",afade=t=out:st={Num(length - fadeOut)}:d={Num(fadeOut)}");
        }

        chain.Append(CultureInfo.InvariantCulture, $"[{label}]");
        _chains.Add(chain.ToString());
        return label;
    }

    /// <summary>
    /// The volume over time for an item's <see cref="Item.VolumeDips"/>, as an FFmpeg expression of t
    /// (seconds from the item's start), or null when no dip falls inside the item.
    /// </summary>
    internal static string? DipExpression(Item item, double length)
    {
        var factors = new List<string>();
        foreach (var dip in item.VolumeDips.OrderBy(d => d.Start))
        {
            var ramp = Math.Max(0.01, dip.Ramp);
            var start = item.ToTimeline(dip.Start);
            var end = item.ToTimeline(dip.End);
            if (end + ramp <= 0 || start - ramp >= length || end <= start)
            {
                continue;
            }

            var level = Math.Clamp(dip.Level, 0, 1);
            // 1 outside the dip, `level` inside, straight ramps of `ramp` seconds before and after.
            factors.Add($"(1-{Num(1 - level)}*clip((t-{Num(start - ramp)})/{Num(ramp)},0,1)*clip(({Num(end + ramp)}-t)/{Num(ramp)},0,1))");
        }

        return factors.Count == 0 ? null : string.Join("*", factors);
    }

    /// <summary>
    /// Text drawn on a transparent frame. The words go through a text file and the font is copied into
    /// FFmpeg's working folder, so no text or path ever needs escaping in the filter graph.
    /// </summary>
    private string TextVideo(Item item, long frames)
    {
        var style = item.Text;
        var label = Label("tv");
        var length = frames / _fps;
        var font = FontFile(style.Font, style.Bold);
        var size = (int)Geometry.TextPixelSize(style, _height);
        var content = string.IsNullOrWhiteSpace(style.Content) ? " " : style.Content.Replace("\r\n", "\n").Replace('\r', '\n');
        var anim = Math.Min(Math.Max(0.05, style.AnimationDuration), length / 2);

        var common = new StringBuilder();
        common.Append(CultureInfo.InvariantCulture, $"fontfile={font}:expansion=none:fontsize={size}:fontcolor=0x{ColorCatalog.HexFor(style.ColorName)}");
        common.Append(CultureInfo.InvariantCulture, $":line_spacing={Math.Max(1, size / 5)}");
        if (_text.SupportsAlign)
        {
            common.Append(":text_align=C");
        }

        if (style.Outline)
        {
            common.Append(CultureInfo.InvariantCulture, $":borderw={Math.Max(1, size / 16)}:bordercolor=0x000000@0.85");
        }

        if (style.Background != TextBackground.None)
        {
            // drawtext on a transparent frame applies the box opacity three times over, so ask for the
            // cube root to end up with the intended strength (60 percent dark, 75 percent light).
            var box = style.Background == TextBackground.DarkBox
                ? $"0x000000@{Num(Math.Round(Math.Cbrt(0.6), 3))}"
                : $"0xFFFFFF@{Num(Math.Round(Math.Cbrt(0.75), 3))}";
            common.Append(CultureInfo.InvariantCulture, $":box=1:boxcolor={box}:boxborderw={Math.Max(2, size * 3 / 10)}");
        }

        var (x, y, alpha) = TextMotion(item, length, anim);
        var chain = new StringBuilder();
        chain.Append(CultureInfo.InvariantCulture, $"color=c=black@0.0:s={_width}x{_height}:r={Num(_fps)},format=rgba");

        if (style.AnimationIn == TextAnimation.Typewriter)
        {
            var steps = TypewriterSteps(content);
            var stepTime = anim / steps.Count;
            for (var i = 0; i < steps.Count; i++)
            {
                var file = TextFile(steps[i]);
                var enable = i == steps.Count - 1
                    ? $"gte(t,{Num(i * stepTime)})"
                    : $"between(t,{Num(i * stepTime)},{Num((i + 1) * stepTime - 0.0001)})";
                // Keep the left edge where the finished line will start, so the words don't drift.
                var typeX = TypewriterX(item);
                chain.Append(CultureInfo.InvariantCulture, $",drawtext={common}:textfile={file}:x='{typeX}':y='{y}':alpha='{alpha}':enable='{enable}'");
            }
        }
        else
        {
            chain.Append(CultureInfo.InvariantCulture, $",drawtext={common}:textfile={TextFile(content)}:x='{x}':y='{y}':alpha='{alpha}'");
        }

        chain.Append(",format=yuva420p");
        if (item.Placement.OpacityPercent < 99.95)
        {
            chain.Append(CultureInfo.InvariantCulture,
                $",format=rgba,colorchannelmixer=aa={Num(Math.Clamp(item.Placement.OpacityPercent, 0, 100) / 100)},format=yuva420p");
        }

        chain.Append(CultureInfo.InvariantCulture,
            $",trim=end_frame={frames},setpts=PTS-STARTPTS,fps={Num(_fps)}");
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

    /// <summary>drawtext x, y and alpha expressions for the resting place plus the in and out animations.</summary>
    private (string X, string Y, string Alpha) TextMotion(Item item, double length, double anim)
    {
        var (baseX, baseY) = TextBasePosition(item.Placement);
        var a = Num(anim);
        var d = Num(length);
        var half = Num(length / 2);
        // Eased progress: 0 at the start of the animation, 1 when the text is at rest.
        var pin = $"(1-pow(1-min(t/{a},1),2))";
        var pout = $"(1-pow(1-min(({d}-t)/{a},1),2))";

        string Move(TextAnimation animation, string p, bool vertical)
        {
            var rest = vertical ? baseY : baseX;
            return (animation, vertical) switch
            {
                (TextAnimation.SlideFromLeft, false) => $"({rest})-(1-{p})*(({rest})+text_w)",
                (TextAnimation.SlideFromRight, false) => $"({rest})+(1-{p})*(w-({rest}))",
                (TextAnimation.SlideFromTop, true) => $"({rest})-(1-{p})*(({rest})+text_h)",
                (TextAnimation.SlideFromBottom, true) => $"({rest})+(1-{p})*(h-({rest}))",
                (TextAnimation.Rise, true) => $"({rest})+(1-{p})*h*0.06",
                _ => rest,
            };
        }

        string Phase(Func<string, string, string> inPart, Func<string, string, string> outPart) =>
            $"if(lt(t,{half}),{inPart(pin, "in")},{outPart(pout, "out")})";

        var outAnimation = item.Text.AnimationOut == TextAnimation.Typewriter ? TextAnimation.Fade : item.Text.AnimationOut;
        var inAnimation = item.Text.AnimationIn;
        var x = Phase((p, _) => Move(inAnimation, p, false), (p, _) => Move(outAnimation, p, false));
        var y = Phase((p, _) => Move(inAnimation, p, true), (p, _) => Move(outAnimation, p, true));

        static bool Fades(TextAnimation animation) => animation is TextAnimation.Fade or TextAnimation.Rise;
        var alphaIn = Fades(inAnimation) ? $"min(t/{a},1)" : "1";
        var alphaOut = Fades(outAnimation) ? $"min(({d}-t)/{a},1)" : "1";
        var alpha = $"max(0,min(1,{alphaIn}*{alphaOut}))";
        return (x, y, alpha);
    }

    /// <summary>The resting position, as drawtext expressions of the text's own size.</summary>
    private (string X, string Y) TextBasePosition(Placement placement)
    {
        var m = Geometry.Margin(_width, _height, placement.MarginPercent).ToString(Inv);
        const string center = "(w-text_w)/2";
        const string middle = "(h-text_h)/2";
        var right = $"w-text_w-{m}";
        var bottom = $"h-text_h-{m}";
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

        return ($"max(0,min(w-text_w,{x}))", $"max(0,min(h-text_h,{y}))");
    }

    /// <summary>Left edge for typewriter text: where the whole text will sit, estimated from its length.</summary>
    private string TypewriterX(Item item) => Num(Math.Round(Geometry.RectOf(item, _width, _height).X));

    /// <summary>The text revealed step by step: one letter at a time for short text, then by words.</summary>
    private static List<string> TypewriterSteps(string content)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(content);
        while (enumerator.MoveNext())
        {
            elements.Add((string)enumerator.Current);
        }

        var steps = new List<string>();
        var maxSteps = 60;
        var stride = Math.Max(1, (int)Math.Ceiling(elements.Count / (double)maxSteps));
        for (var end = stride; end < elements.Count; end += stride)
        {
            var prefix = string.Concat(elements.Take(end));
            if (prefix.Trim().Length > 0)
            {
                steps.Add(prefix);
            }
        }

        steps.Add(content);
        return steps;
    }

    private string TextFile(string content)
    {
        var name = $"text{_aux.Count}.txt";
        _aux[name] = new AuxiliaryFile(content, null);
        return name;
    }

    private string FontFile(string fontName, bool bold)
    {
        var key = $"{fontName}|{bold}";
        if (_fontNames.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var path = _text.ResolveFont(fontName, bold)
                   ?? throw new RenderException($"No font file was found for {fontName}.");
        var name = $"font{_fontNames.Count}{Path.GetExtension(path).ToLowerInvariant()}";
        _aux[name] = new AuxiliaryFile(null, path);
        _fontNames[key] = name;
        return name;
    }

    /// <summary>
    /// Changes sound speed without changing its pitch, or null at normal speed. One atempo step goes down to
    /// half speed, so slower speeds use two.
    /// </summary>
    internal static string? Tempo(double speed)
    {
        if (Math.Abs(speed - 1) < 1e-6)
        {
            return null;
        }

        var steps = new List<string>();
        while (speed < 0.5 - 1e-9)
        {
            steps.Add("atempo=0.5");
            speed /= 0.5;
        }

        steps.Add($"atempo={Num(speed)}");
        return string.Join(",", steps);
    }

    private static string Silence(long samples, string label) =>
        $"anullsrc=r={SampleRate}:cl=stereo,{AudioFormat},atrim=end_sample={samples},asetpts=PTS-STARTPTS[{label}]";

    /// <summary>Adds a media file as an FFmpeg input, seeking to the item's start, and returns its index.</summary>
    private int AddInput(Item item, bool forVideo)
    {
        if (item.Kind == ItemKind.Image)
        {
            _inputs.AddRange(["-loop", "1", "-framerate", Num(_fps), "-t", Num(item.Duration + 1), "-i", Path.GetFullPath(item.Path)]);
        }
        else
        {
            if (item.SourceIn > 0.0005)
            {
                _inputs.AddRange(["-ss", Num(item.SourceIn)]);
            }

            _inputs.AddRange(["-t", Num(item.SourceLength + 1)]);
            // Each input feeds either picture or sound; skip decoding the other.
            _inputs.Add(forVideo ? "-an" : "-vn");

            _inputs.AddRange(["-i", Path.GetFullPath(item.Path)]);
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
        var m = Geometry.Margin(_width, _height, placement.MarginPercent).ToString(Inv);
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

    private IEnumerable<string> VideoCodecArgs(OutputProfile profile, VideoEncoder? encoder)
    {
        var rate = Num(_fps);
        if (encoder is not null && (encoder.IsHardware || _hasEncoder("libx264")) && _hasEncoder(encoder.Name))
        {
            return VideoEncoders.Args(encoder, profile.Crf, _fps);
        }

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
