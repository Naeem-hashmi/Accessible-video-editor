using System.Globalization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Text;

/// <summary>
/// Says in words what the picture looks like at a moment: which pictures and text are visible, where
/// they are, how big, and what covers what. This is the screen reader user's view of the preview.
/// </summary>
public static class FrameDescriber
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Where one item sits, e.g. "top right, 15 percent wide and 15 percent high, touching the top edge".</summary>
    public static string Placement(Item item, int width, int height)
    {
        var rect = Geometry.RectOf(item, width, height);
        if (item.Kind != ItemKind.Text && rect.Width >= width - 1 && rect.Height >= height - 1)
        {
            var fill = item.Placement.Fill ? ", filling the frame with the edges cut off" : "";
            return "full frame" + fill + Extras(item);
        }

        var parts = new List<string>
        {
            Geometry.Area(rect, width, height),
            $"{Pct(rect.Width / width)} wide and {Pct(rect.Height / height)} high",
        };

        if (item.Placement.Position == PositionPreset.Custom || item.Kind == ItemKind.Text)
        {
            parts.Add($"left edge {Pct(rect.X / width)} across, top edge {Pct(rect.Y / height)} down");
        }

        var edges = Edges(rect, width, height);
        if (edges.Count > 0)
        {
            parts.Add("touching the " + Join(edges) + (edges.Count == 1 ? " edge" : " edges"));
        }

        return string.Join(", ", parts) + Extras(item);
    }

    /// <summary>Everything visible at <paramref name="time"/>, bottom layer first.</summary>
    public static string Describe(Project project, double time)
    {
        var visible = new List<(int Layer, Item Item, FrameRect Rect)>();
        for (var i = 0; i < project.Layers.Count; i++)
        {
            var layer = project.Layers[i];
            if (layer.Kind != LayerKind.Video || layer.Muted)
            {
                continue;
            }

            var index = Timeline.ItemAt(layer, time);
            if (index < 0)
            {
                continue;
            }

            var item = layer.Items[index];
            if (!item.HasVideo && item.Kind is not (ItemKind.Text or ItemKind.Color))
            {
                continue;
            }

            visible.Add((i + 1, item, Geometry.RectOf(item, project.Width, project.Height)));
        }

        if (visible.Count == 0)
        {
            return $"At {TimeFormat.Words(time)} the picture is black.";
        }

        var frameArea = (double)project.Width * project.Height;
        var sentences = new List<string> { $"At {TimeFormat.Words(time)}" };
        var covered = visible.Any(v => v.Rect.Area >= frameArea * 0.98 && v.Item.Kind != ItemKind.Text);
        if (!covered)
        {
            sentences.Add("Parts of the frame are black");
        }

        for (var k = 0; k < visible.Count; k++)
        {
            var (layer, item, rect) = visible[k];
            var sentence = $"Layer {layer}, {Name(item)}, {Placement(item, project.Width, project.Height)}";
            var over = new List<string>();
            for (var j = k - 1; j >= 0; j--)
            {
                var below = visible[j];
                var shared = rect.Intersect(below.Rect).Area;
                if (shared <= rect.Area * 0.02)
                {
                    continue;
                }

                over.Add(below.Rect.Area >= frameArea * 0.98
                    ? $"over {Name(below.Item)}"
                    : shared >= below.Rect.Area * 0.95
                        ? $"completely covering {Name(below.Item)}"
                        : $"covering part of {Name(below.Item)}");
            }

            if (over.Count > 0)
            {
                sentence += ", " + string.Join(", ", over);
            }

            sentences.Add(sentence);
        }

        return string.Join(". ", sentences) + ".";
    }

    /// <summary>Short spoken name: the file name, or the words for text.</summary>
    public static string Name(Item item)
    {
        if (item.Kind == ItemKind.Text)
        {
            var words = item.Text.Content.Replace("\r", "").Replace('\n', ' ').Trim();
            if (words.Length > 40)
            {
                words = words[..40] + "...";
            }

            return $"text \"{words}\"";
        }

        return item.Name;
    }

    private static string Extras(Item item)
    {
        var parts = new List<string>();
        if (item.Placement.OpacityPercent < 99.95)
        {
            parts.Add($"{Math.Round(item.Placement.OpacityPercent).ToString(Inv)} percent opacity");
        }

        if (item.Placement.HasCrop)
        {
            var crops = new List<string>();
            void Add(double value, string edge)
            {
                if (value > 0.01)
                {
                    crops.Add($"{Math.Round(value).ToString(Inv)} percent off the {edge}");
                }
            }

            Add(item.Placement.CropLeft, "left");
            Add(item.Placement.CropRight, "right");
            Add(item.Placement.CropTop, "top");
            Add(item.Placement.CropBottom, "bottom");
            parts.Add("cropped " + Join(crops));
        }

        return parts.Count == 0 ? "" : ", " + string.Join(", ", parts);
    }

    private static List<string> Edges(FrameRect rect, int width, int height)
    {
        var edges = new List<string>();
        var tolerance = Math.Max(2, Math.Min(width, height) * 0.01);
        if (rect.Y <= tolerance)
        {
            edges.Add("top");
        }

        if (rect.Bottom >= height - tolerance)
        {
            edges.Add("bottom");
        }

        if (rect.X <= tolerance)
        {
            edges.Add("left");
        }

        if (rect.Right >= width - tolerance)
        {
            edges.Add("right");
        }

        return edges;
    }

    private static string Join(IReadOnlyList<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };

    private static string Pct(double fraction) =>
        Math.Round(Math.Clamp(fraction, 0, 1) * 100).ToString(Inv) + " percent";
}
