namespace AccessibleVideoEditor.Core.Model;

/// <summary>Where an item sits on the project timeline.</summary>
/// <param name="Index">Item index within its layer.</param>
/// <param name="Start">Timeline start in seconds.</param>
/// <param name="End">Timeline end in seconds.</param>
/// <param name="Overlap">Seconds this item overlaps the previous one because of its transition.</param>
public readonly record struct ItemSpan(int Index, double Start, double End, double Overlap)
{
    public double Duration => End - Start;

    public bool Contains(double time) => time >= Start - Timeline.Epsilon && time < End - Timeline.Epsilon;
}

/// <summary>
/// Timeline arithmetic. Items in a layer play one after another: each starts after the previous one ends,
/// plus its gap, minus the overlap of its transition. Everything that needs positions uses these methods,
/// so the spoken timeline and the rendered video always agree.
/// </summary>
public static class Timeline
{
    public const double Epsilon = 1e-6;

    /// <summary>Shortest allowed item, in seconds.</summary>
    public const double MinItemDuration = 0.1;

    public static bool TransitionIsActive(LayerKind kind, TransitionSpec spec) => kind == LayerKind.Video
        ? spec.Video != TransitionSpec.None
        : spec.Audio != TransitionSpec.None;

    /// <summary>
    /// The real overlap of the transition into item <paramref name="index"/>. Transitions only work between
    /// touching items (no gap) and never take more than half of either item.
    /// </summary>
    public static double EffectiveTransition(Layer layer, int index)
    {
        if (index <= 0 || index >= layer.Items.Count)
        {
            return 0;
        }

        var item = layer.Items[index];
        if (item.GapBefore > Epsilon || !TransitionIsActive(layer.Kind, item.TransitionIn) ||
            item.TransitionIn.Duration <= Epsilon)
        {
            return 0;
        }

        var previous = layer.Items[index - 1];
        var overlap = Math.Min(item.TransitionIn.Duration, Math.Min(previous.Duration / 2, item.Duration / 2));
        return overlap < 0.01 ? 0 : overlap;
    }

    public static IReadOnlyList<ItemSpan> Spans(Layer layer)
    {
        var spans = new ItemSpan[layer.Items.Count];
        double end = 0;
        for (var i = 0; i < layer.Items.Count; i++)
        {
            var item = layer.Items[i];
            var overlap = EffectiveTransition(layer, i);
            var start = i == 0 ? item.GapBefore : end + item.GapBefore - overlap;
            end = start + item.Duration;
            spans[i] = new ItemSpan(i, start, end, overlap);
        }

        return spans;
    }

    public static ItemSpan Span(Layer layer, int index) => Spans(layer)[index];

    public static double LayerEnd(Layer layer)
    {
        var spans = Spans(layer);
        return spans.Count == 0 ? 0 : spans[^1].End;
    }

    /// <summary>The length of the whole project, including muted layers.</summary>
    public static double ProjectDuration(Project project) =>
        project.Layers.Count == 0 ? 0 : project.Layers.Max(LayerEnd);

    /// <summary>The length counting only the given layers.</summary>
    public static double Duration(IEnumerable<Layer> layers)
    {
        double max = 0;
        foreach (var layer in layers)
        {
            max = Math.Max(max, LayerEnd(layer));
        }

        return max;
    }

    /// <summary>The item playing at <paramref name="time"/>, or -1. During a transition the incoming item wins.</summary>
    public static int ItemAt(Layer layer, double time)
    {
        var spans = Spans(layer);
        for (var i = spans.Count - 1; i >= 0; i--)
        {
            if (spans[i].Contains(time))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The item to focus when arriving on a layer at <paramref name="time"/>: the one playing then,
    /// otherwise the next one, otherwise the last. -1 for an empty layer.
    /// </summary>
    public static int NearestItem(Layer layer, double time)
    {
        if (layer.Items.Count == 0)
        {
            return -1;
        }

        var at = ItemAt(layer, time);
        if (at >= 0)
        {
            return at;
        }

        var spans = Spans(layer);
        for (var i = 0; i < spans.Count; i++)
        {
            if (spans[i].Start >= time)
            {
                return i;
            }
        }

        return spans.Count - 1;
    }

    /// <summary>All points the marker keys can jump to: named markers plus the range start and end, sorted.</summary>
    public static IReadOnlyList<double> MarkerTimes(Project project)
    {
        var times = project.Markers.Select(m => m.Time).ToList();
        if (project.RangeStart is { } start)
        {
            times.Add(start);
        }

        if (project.RangeEnd is { } end)
        {
            times.Add(end);
        }

        times.Sort();
        return times;
    }
}
