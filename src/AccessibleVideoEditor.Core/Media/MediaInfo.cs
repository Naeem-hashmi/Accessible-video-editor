using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.Core.Media;

/// <summary>What a media file contains, as reported by ffprobe.</summary>
public sealed record MediaInfo
{
    public required string Path { get; init; }

    public ItemKind Kind { get; init; }

    /// <summary>Length in seconds; 0 for still images.</summary>
    public double Duration { get; init; }

    public bool HasVideo { get; init; }

    public bool HasAudio { get; init; }

    public int Width { get; init; }

    public int Height { get; init; }
}

public sealed class MediaProbeException(string message) : Exception(message);

/// <summary>Reads media files with ffprobe and turns them into timeline items.</summary>
public static class MediaProbe
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"];

    public static readonly string[] AudioExtensions =
        [".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg", ".opus", ".wma", ".aif", ".aiff"];

    public static readonly string[] VideoExtensions =
        [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".3gp", ".flv", ".gif"];

    /// <summary>A file-dialog filter for all supported media.</summary>
    public static string FileDialogFilter
    {
        get
        {
            static string Join(IEnumerable<string> exts) => string.Join(";", exts.Select(e => "*" + e));
            var all = ImageExtensions.Concat(AudioExtensions).Concat(VideoExtensions);
            return $"All media|{Join(all)}|Video|{Join(VideoExtensions)}|Audio|{Join(AudioExtensions)}|Pictures|{Join(ImageExtensions)}|All files|*.*";
        }
    }

    public static async Task<MediaInfo> ProbeAsync(FfmpegTools tools, string path, CancellationToken token = default)
    {
        if (!File.Exists(path))
        {
            throw new MediaProbeException($"File not found: {path}");
        }

        var psi = new ProcessStartInfo(tools.FfprobePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-v", "error", "-print_format", "json", "-show_format", "-show_streams", path })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new MediaProbeException("Could not start ffprobe.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        var json = await stdout.ConfigureAwait(false);
        var errors = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            var reason = errors.Trim().Split('\n').LastOrDefault()?.Trim();
            throw new MediaProbeException(
                $"{System.IO.Path.GetFileName(path)} can't be opened. It may not be a media file, or it may be damaged. {reason}");
        }

        return Parse(path, json);
    }

    /// <summary>Interprets ffprobe JSON output.</summary>
    public static MediaInfo Parse(string path, string ffprobeJson)
    {
        using var doc = JsonDocument.Parse(ffprobeJson);
        var root = doc.RootElement;
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();

        var hasVideo = false;
        var hasAudio = false;
        int width = 0, height = 0;
        double streamDuration = 0;
        long videoFrames = 0;

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = GetString(stream, "codec_type");
                if (type == "video")
                {
                    var attachedPicture = stream.TryGetProperty("disposition", out var disposition) &&
                                          disposition.TryGetProperty("attached_pic", out var ap) &&
                                          ap.ValueKind == JsonValueKind.Number && ap.GetInt32() == 1;
                    if (attachedPicture || hasVideo)
                    {
                        continue;
                    }

                    hasVideo = true;
                    width = GetInt(stream, "width");
                    height = GetInt(stream, "height");
                    streamDuration = Math.Max(streamDuration, GetDouble(stream, "duration"));
                    videoFrames = (long)GetDouble(stream, "nb_frames");
                }
                else if (type == "audio")
                {
                    hasAudio = true;
                    streamDuration = Math.Max(streamDuration, GetDouble(stream, "duration"));
                }
            }
        }

        double duration = 0;
        var formatName = "";
        if (root.TryGetProperty("format", out var format))
        {
            duration = GetDouble(format, "duration");
            formatName = GetString(format, "format_name") ?? "";
        }

        if (duration <= 0)
        {
            duration = streamDuration;
        }

        if (!hasVideo && !hasAudio)
        {
            throw new MediaProbeException($"{System.IO.Path.GetFileName(path)} has no picture or sound.");
        }

        var isStill = hasVideo && !hasAudio &&
                      (ImageExtensions.Contains(extension) || formatName.EndsWith("_pipe", StringComparison.Ordinal) ||
                       formatName == "image2" || (extension != ".gif" && videoFrames == 1));

        ItemKind kind;
        if (isStill)
        {
            kind = ItemKind.Image;
            duration = 0;
        }
        else if (hasVideo)
        {
            kind = ItemKind.Video;
        }
        else
        {
            kind = ItemKind.Audio;
        }

        if (kind != ItemKind.Image && duration <= 0)
        {
            throw new MediaProbeException($"Can't tell how long {System.IO.Path.GetFileName(path)} is.");
        }

        return new MediaInfo
        {
            Path = path,
            Kind = kind,
            Duration = duration,
            HasVideo = hasVideo,
            HasAudio = hasAudio,
            Width = width,
            Height = height,
        };
    }

    /// <summary>Makes a timeline item for a probed file, using the layer's defaults.</summary>
    public static Item CreateItem(MediaInfo info, LayerDefaults defaults, LayerKind layerKind)
    {
        var item = new Item
        {
            Kind = info.Kind,
            Path = info.Path,
            Name = System.IO.Path.GetFileName(info.Path),
            SourceDuration = info.Duration,
            Duration = info.Kind == ItemKind.Image ? defaults.ImageDuration : info.Duration,
            HasVideo = info.HasVideo,
            HasAudio = info.HasAudio,
            SourceWidth = info.Width,
            SourceHeight = info.Height,
            Volume = defaults.Volume,
            TransitionIn = defaults.Transition.Clone(),
            Placement = defaults.Placement.Clone(),
            Audio = defaults.Audio.Clone(),
        };

        if (layerKind == LayerKind.Audio && item.Kind == ItemKind.Video)
        {
            // A video file on an audio layer contributes only its sound.
            item.HasVideo = false;
        }

        item.Duration = Math.Max(Timeline.MinItemDuration, item.Duration);
        return item;
    }

    public static Item CreateColorCard(string colorName, double duration, LayerDefaults defaults) => new()
    {
        Kind = ItemKind.Color,
        Name = $"{colorName} colour card",
        ColorName = ColorCatalog.IsKnown(colorName) ? colorName : "Black",
        Duration = Math.Max(Timeline.MinItemDuration, duration),
        HasVideo = true,
        TransitionIn = defaults.Transition.Clone(),
        Placement = defaults.Placement.Clone(),
    };

    /// <summary>Which kind of layer a file belongs on.</summary>
    public static LayerKind PreferredLayer(MediaInfo info) => info.Kind == ItemKind.Audio ? LayerKind.Audio : LayerKind.Video;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double GetDouble(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return 0;
        }

        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => 0,
        };
    }

    private static int GetInt(JsonElement e, string name) => (int)GetDouble(e, name);
}
