using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Tests.Support;

/// <summary>Builds test items and layers without touching files.</summary>
internal static class Make
{
    public static Item Clip(string name, double duration, double gap = 0, double sourceDuration = 0) => new()
    {
        Kind = ItemKind.Video,
        Name = name,
        Path = name,
        Duration = duration,
        SourceDuration = sourceDuration > 0 ? sourceDuration : duration,
        HasVideo = true,
        HasAudio = true,
        GapBefore = gap,
    };

    public static Layer Layer(LayerKind kind, params Item[] items)
    {
        var layer = new Layer { Kind = kind, Name = kind == LayerKind.Video ? "Video 1" : "Audio 1" };
        layer.Items.AddRange(items);
        return layer;
    }

    public static Item WithTransition(this Item item, string video, double duration, string audio = TransitionSpec.None)
    {
        item.TransitionIn = new TransitionSpec { Video = video, Audio = audio, Duration = duration };
        return item;
    }

    public static double[] Starts(Layer layer) => Timeline.Spans(layer).Select(s => Math.Round(s.Start, 3)).ToArray();
}
