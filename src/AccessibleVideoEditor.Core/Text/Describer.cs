using System.Globalization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Text;

public enum Verbosity
{
    /// <summary>Just the essentials, for experienced users.</summary>
    Brief,

    /// <summary>Position, length and anything unusual.</summary>
    Normal,

    /// <summary>Every setting.</summary>
    Detailed,
}

/// <summary>Builds the sentences the screen reader speaks for layers, items and the playhead.</summary>
public sealed class Describer(Verbosity verbosity = Verbosity.Normal, TimeStyle timeStyle = TimeStyle.Words)
{
    public Verbosity Verbosity { get; set; } = verbosity;

    public TimeStyle TimeStyle { get; set; } = timeStyle;

    public string Time(double seconds) => TimeFormat.Format(seconds, TimeStyle);

    public static string KindName(ItemKind kind, LayerKind layer) => kind switch
    {
        ItemKind.Video when layer == LayerKind.Audio => "video sound",
        ItemKind.Video => "video",
        ItemKind.Image => "picture",
        ItemKind.Audio => "audio",
        ItemKind.Color => "colour card",
        ItemKind.Text => "text",
        _ => "item",
    };

    /// <summary>"2 times speed" or "slow motion, half speed".</summary>
    public static string SpeedText(double speed)
    {
        var x = Math.Round(speed, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return speed < 1 ? $"slow motion, {x} times speed" : $"{x} times speed";
    }

    public static string LayerKindName(LayerKind kind) => kind == LayerKind.Video ? "Video" : "Audio";

    public string Layer(Project project, Layer layer)
    {
        var number = project.Layers.IndexOf(layer) + 1;
        var parts = new List<string> { $"Layer {number}", LayerKindName(layer.Kind) };
        if (!string.Equals(layer.Name, $"{LayerKindName(layer.Kind)} {number}", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(layer.Name);
        }

        parts.Add(layer.Items.Count switch
        {
            0 => "empty",
            1 => "1 item",
            var n => $"{n} items",
        });

        if (layer.Muted)
        {
            parts.Add("muted");
        }

        if (Verbosity != Verbosity.Brief && layer.Items.Count > 0)
        {
            parts.Add($"ends {Time(Timeline.LayerEnd(layer))}");
        }

        if (Math.Abs(layer.Volume - 1) > 0.001)
        {
            parts.Add($"volume {Percent(layer.Volume)}");
        }

        if (Verbosity == Verbosity.Detailed)
        {
            parts.Add($"of {project.Layers.Count} layers");
        }

        return string.Join(", ", parts);
    }

    public string Item(Layer layer, int index)
    {
        var item = layer.Items[index];
        var span = Timeline.Span(layer, index);
        var parts = new List<string>();

        if (Verbosity == Verbosity.Brief)
        {
            parts.Add(item.Name);
            parts.Add(Time(span.Start));
            parts.Add($"{Time(item.Duration)} long");
        }
        else
        {
            parts.Add($"{index + 1} of {layer.Items.Count}");
            parts.Add(item.Name);
            parts.Add(KindName(item.Kind, layer.Kind));
            parts.Add($"starts {Time(span.Start)}");
            parts.Add($"length {Time(item.Duration)}");
        }

        if (Math.Abs(item.EffectiveSpeed - 1) > 0.001)
        {
            parts.Add(SpeedText(item.EffectiveSpeed));
        }

        if (item.GapBefore > Timeline.Epsilon)
        {
            parts.Add($"gap before {Time(item.GapBefore)}");
        }

        if (span.Overlap > 0)
        {
            var name = layer.Kind == LayerKind.Video
                ? TransitionCatalog.FindVideo(item.TransitionIn.Video).Name
                : TransitionCatalog.FindAudio(item.TransitionIn.Audio).Name;
            parts.Add($"{name} {Time(span.Overlap)}");
        }

        if (item.AudioMuted && item.HasAudio)
        {
            parts.Add("sound muted");
        }
        else if (Math.Abs(item.Volume - 1) > 0.001 && item.HasAudio)
        {
            parts.Add($"volume {Percent(item.Volume)}");
        }

        if (Verbosity != Verbosity.Brief && layer.Kind == LayerKind.Video && item.Placement.Position != PositionPreset.FullFrame)
        {
            parts.Add(PlacementText(item.Placement));
        }

        if (Verbosity == Verbosity.Detailed)
        {
            parts.Add($"ends {Time(span.End)}");
            if (item.IsTimeLimited && item.SourceIn > Timeline.Epsilon)
            {
                parts.Add($"from {Time(item.SourceIn)} into the file");
            }

            if (item.FadeIn > 0.01)
            {
                parts.Add($"fade in {Time(item.FadeIn)}");
            }

            if (item.FadeOut > 0.01)
            {
                parts.Add($"fade out {Time(item.FadeOut)}");
            }

            if (item.Placement.OpacityPercent < 99.95 && layer.Kind == LayerKind.Video)
            {
                parts.Add($"opacity {Math.Round(item.Placement.OpacityPercent).ToString(CultureInfo.InvariantCulture)} percent");
            }

            if (!item.Audio.IsDefault && item.HasAudio)
            {
                parts.Add(AudioText(item.Audio));
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>What every layer has at <paramref name="time"/>, for the "what's here" key.</summary>
    public string WhatsHere(Project project, double time)
    {
        if (project.Layers.Count == 0)
        {
            return "The project has no layers.";
        }

        var parts = new List<string> { $"At {Time(time)}" };
        for (var i = 0; i < project.Layers.Count; i++)
        {
            var layer = project.Layers[i];
            var index = Timeline.ItemAt(layer, time);
            var label = $"layer {i + 1}{(layer.Muted ? " muted" : "")}";
            if (index < 0)
            {
                parts.Add($"{label}: nothing");
                continue;
            }

            var item = layer.Items[index];
            var span = Timeline.Span(layer, index);
            var where = layer.Kind == LayerKind.Video && item.Placement.Position != PositionPreset.FullFrame
                ? $", {PlacementText(item.Placement)}"
                : "";
            parts.Add($"{label}: {item.Name}{where}, {Time(span.End - time)} left");
        }

        return string.Join(". ", parts) + ".";
    }

    public static string PlacementText(Placement placement)
    {
        var size = Math.Round(placement.SizePercent).ToString(CultureInfo.InvariantCulture);
        return placement.Position switch
        {
            PositionPreset.FullFrame => "full frame",
            PositionPreset.Custom =>
                $"custom position {Math.Round(placement.CustomXPercent).ToString(CultureInfo.InvariantCulture)} percent across, {Math.Round(placement.CustomYPercent).ToString(CultureInfo.InvariantCulture)} percent down, {size} percent size",
            _ => $"{PositionName(placement.Position).ToLowerInvariant()}, {size} percent size",
        };
    }

    public static string PositionName(PositionPreset position) => position switch
    {
        PositionPreset.FullFrame => "Full frame",
        PositionPreset.Center => "Centre",
        PositionPreset.TopLeft => "Top left",
        PositionPreset.TopCenter => "Top centre",
        PositionPreset.TopRight => "Top right",
        PositionPreset.MiddleLeft => "Middle left",
        PositionPreset.MiddleRight => "Middle right",
        PositionPreset.BottomLeft => "Bottom left",
        PositionPreset.BottomCenter => "Bottom centre",
        PositionPreset.BottomRight => "Bottom right",
        PositionPreset.Custom => "Custom",
        _ => position.ToString(),
    };

    public static string AudioText(AudioCleanup audio)
    {
        var parts = new List<string>();
        if (audio.NoiseReduction != NoiseReduction.Off)
        {
            parts.Add($"noise reduction {audio.NoiseReduction.ToString().ToLowerInvariant()}");
        }

        if (audio.Hum != HumFilter.Off)
        {
            parts.Add(audio.Hum == HumFilter.Hz50 ? "50 hertz hum removed" : "60 hertz hum removed");
        }

        if (audio.RemoveRumble)
        {
            parts.Add("rumble removed");
        }

        if (audio.LowDb != 0 || audio.MidDb != 0 || audio.HighDb != 0)
        {
            parts.Add($"equaliser low {Db(audio.LowDb)}, middle {Db(audio.MidDb)}, high {Db(audio.HighDb)}");
        }

        if (audio.Voice != VoicePreset.None)
        {
            parts.Add($"{audio.Voice.ToString().ToLowerInvariant()} voice");
        }

        if (audio.Normalize)
        {
            parts.Add("loudness evened out");
        }

        return parts.Count == 0 ? "no sound changes" : string.Join(", ", parts);
    }

    public static string Percent(double fraction) =>
        Math.Round(fraction * 100).ToString(CultureInfo.InvariantCulture) + " percent";

    public static string Db(int db) => db switch
    {
        0 => "0",
        > 0 => $"plus {db}",
        _ => $"minus {-db}",
    };
}
