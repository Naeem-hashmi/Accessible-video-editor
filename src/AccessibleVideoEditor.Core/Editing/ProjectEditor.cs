using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// All timeline edits. Each method either changes the project and returns a success message, or leaves
/// the project untouched and returns a failure message explaining why.
/// </summary>
/// <remarks>
/// "Ripple" decides what happens to the items after the one you edit. With ripple on, later items move
/// with the change (no holes). With ripple off, later items keep their time on the timeline.
/// </remarks>
public static class ProjectEditor
{
    private const double MarkerTolerance = 0.05;

    // ---------------------------------------------------------------- layers

    public static string DefaultLayerName(Project project, LayerKind kind)
    {
        var baseName = kind == LayerKind.Video ? "Video" : "Audio";
        var number = project.Layers.Count(l => l.Kind == kind) + 1;
        var name = $"{baseName} {number}";
        while (project.Layers.Any(l => l.Name == name))
        {
            number++;
            name = $"{baseName} {number}";
        }

        return name;
    }

    public static Layer AddLayer(Project project, LayerKind kind, string? name = null, int? index = null)
    {
        var layer = new Layer
        {
            Kind = kind,
            Name = string.IsNullOrWhiteSpace(name) ? DefaultLayerName(project, kind) : name.Trim(),
        };
        if (kind == LayerKind.Video && project.Layers.Any(l => l.Kind == LayerKind.Video))
        {
            // A second video layer is an overlay: start new pictures in the bottom right corner.
            layer.Defaults.Placement = new Placement { Position = PositionPreset.BottomRight, SizePercent = 30 };
        }

        var at = index is { } i ? Math.Clamp(i, 0, project.Layers.Count) : project.Layers.Count;
        project.Layers.Insert(at, layer);
        return layer;
    }

    public static EditResult DeleteLayer(Project project, Guid layerId)
    {
        var index = project.LayerIndexOf(layerId);
        if (index < 0)
        {
            return EditResult.Fail("That layer no longer exists.");
        }

        var layer = project.Layers[index];
        project.Layers.RemoveAt(index);
        var focus = project.Layers.Count == 0 ? (Guid?)null : project.Layers[Math.Min(index, project.Layers.Count - 1)].Id;
        return EditResult.Success($"Deleted layer {layer.Name}.", focusLayer: focus);
    }

    /// <summary>Moves a layer up (towards the top of the picture, higher number) or down.</summary>
    public static EditResult MoveLayer(Project project, Guid layerId, int delta)
    {
        var index = project.LayerIndexOf(layerId);
        if (index < 0)
        {
            return EditResult.Fail("That layer no longer exists.");
        }

        var target = index + delta;
        if (target < 0)
        {
            return EditResult.Fail("This is already layer 1.");
        }

        if (target >= project.Layers.Count)
        {
            return EditResult.Fail($"This is already the last layer, layer {project.Layers.Count}.");
        }

        var layer = project.Layers[index];
        project.Layers.RemoveAt(index);
        project.Layers.Insert(target, layer);
        var where = layer.Kind == LayerKind.Video
            ? (delta > 0 ? " Its picture is now in front of the layer below." : " Its picture is now behind the layer above.")
            : "";
        return EditResult.Success($"{layer.Name} is now layer {target + 1}.{where}", focusLayer: layer.Id);
    }

    public static EditResult ToggleMute(Project project, Guid layerId)
    {
        var layer = project.FindLayer(layerId);
        if (layer is null)
        {
            return EditResult.Fail("That layer no longer exists.");
        }

        layer.Muted = !layer.Muted;
        return EditResult.Success(layer.Muted ? $"{layer.Name} muted." : $"{layer.Name} unmuted.");
    }

    /// <summary>
    /// Solo: mutes every other layer so only this one is heard and seen. If the layer is already soloed,
    /// unmutes everything again.
    /// </summary>
    public static EditResult ToggleSolo(Project project, Guid layerId)
    {
        var layer = project.FindLayer(layerId);
        if (layer is null)
        {
            return EditResult.Fail("That layer no longer exists.");
        }

        var others = project.Layers.Where(l => l.Id != layerId).ToList();
        if (others.Count == 0)
        {
            return EditResult.Fail("This is the only layer.");
        }

        var alreadySolo = !layer.Muted && others.All(l => l.Muted);
        if (alreadySolo)
        {
            foreach (var l in project.Layers)
            {
                l.Muted = false;
            }

            return EditResult.Success("Solo off. All layers unmuted.");
        }

        layer.Muted = false;
        foreach (var l in others)
        {
            l.Muted = true;
        }

        return EditResult.Success($"Solo {layer.Name}. All other layers muted.");
    }

    public static EditResult UnmuteAll(Project project)
    {
        if (project.Layers.All(l => !l.Muted))
        {
            return EditResult.Fail("No layers are muted.");
        }

        foreach (var layer in project.Layers)
        {
            layer.Muted = false;
        }

        return EditResult.Success("All layers unmuted.");
    }

    public static EditResult SlideLayer(Layer layer, double delta)
    {
        if (layer.Items.Count == 0)
        {
            return EditResult.Fail("This layer is empty.");
        }

        var first = layer.Items[0];
        var newGap = Math.Max(0, first.GapBefore + delta);
        if (Math.Abs(newGap - first.GapBefore) < Timeline.Epsilon)
        {
            return EditResult.Fail("The layer already starts at the beginning.");
        }

        first.GapBefore = newGap;
        return EditResult.Success($"Layer starts at {TimeFormat.Words(newGap)}.");
    }

    // ---------------------------------------------------------------- adding items

    /// <summary>Adds an item after the last item of the layer, after a gap if needed.</summary>
    public static EditResult AppendItem(Layer layer, Item item, double gapBefore = 0)
    {
        item.GapBefore = Math.Max(0, gapBefore);
        layer.Items.Add(item);
        var span = Timeline.Span(layer, layer.Items.Count - 1);
        return EditResult.Success(
            $"Added {item.Name} to {layer.Name} at {TimeFormat.Words(span.Start)}.", item.Id, layer.Id);
    }

    /// <summary>Inserts an item directly after <paramref name="index"/> (or at the start for -1).</summary>
    public static EditResult InsertAfter(Layer layer, int index, Item item)
    {
        var at = Math.Clamp(index + 1, 0, layer.Items.Count);
        item.GapBefore = 0;
        layer.Items.Insert(at, item);
        var span = Timeline.Span(layer, at);
        return EditResult.Success($"Inserted {item.Name} at {TimeFormat.Words(span.Start)}.", item.Id, layer.Id);
    }

    /// <summary>True when <paramref name="item"/> fits at <paramref name="time"/> without moving anything.</summary>
    public static bool HasRoomAt(Layer layer, double time, double duration) => FindRoom(layer, time, duration) >= 0;

    /// <summary>Places an item at an exact time without moving other items, if there is free space there.</summary>
    public static EditResult InsertAtTime(Layer layer, Item item, double time)
    {
        var at = FindRoom(layer, time, item.Duration);
        if (at < 0)
        {
            return EditResult.Fail($"No free space on {layer.Name} at {TimeFormat.Words(time)}.");
        }

        var spans = Timeline.Spans(layer);
        var previousEnd = at == 0 ? 0 : spans[at - 1].End;
        item.GapBefore = Math.Max(0, time - previousEnd);
        if (at < layer.Items.Count)
        {
            var next = layer.Items[at];
            next.GapBefore = Math.Max(0, spans[at].Start - (time + item.Duration));
        }

        layer.Items.Insert(at, item);
        return EditResult.Success($"Added {item.Name} to {layer.Name} at {TimeFormat.Words(time)}.", item.Id, layer.Id);
    }

    /// <summary>The insert index for an item at <paramref name="time"/>, or -1 if it would overlap something.</summary>
    private static int FindRoom(Layer layer, double time, double duration)
    {
        var spans = Timeline.Spans(layer);
        var previousEnd = 0.0;
        for (var i = 0; i < spans.Count; i++)
        {
            if (time + Timeline.Epsilon >= previousEnd && time + duration <= spans[i].Start + Timeline.Epsilon)
            {
                return i;
            }

            previousEnd = spans[i].End;
        }

        return time + Timeline.Epsilon >= previousEnd ? spans.Count : -1;
    }

    // ---------------------------------------------------------------- removing items

    public static EditResult DeleteItem(Layer layer, int index, bool ripple)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var spans = Timeline.Spans(layer);
        var item = layer.Items[index];
        if (index + 1 < layer.Items.Count)
        {
            var next = layer.Items[index + 1];
            if (ripple)
            {
                next.GapBefore += item.GapBefore;
            }
            else
            {
                var previousEnd = index == 0 ? 0 : spans[index - 1].End;
                next.GapBefore = Math.Max(0, spans[index + 1].Start - previousEnd);
            }
        }

        layer.Items.RemoveAt(index);
        Guid? focus = layer.Items.Count == 0 ? null : layer.Items[Math.Min(index, layer.Items.Count - 1)].Id;
        return EditResult.Success($"Deleted {item.Name}.", focus, layer.Id);
    }

    /// <summary>Removes everything between <paramref name="from"/> and <paramref name="to"/> on one layer.</summary>
    public static EditResult DeleteRange(Layer layer, double from, double to, bool ripple)
    {
        if (to - from < Timeline.MinItemDuration)
        {
            return EditResult.Fail("The range is too short. Set range start with left bracket and range end with right bracket.");
        }

        SplitIfInside(layer, to);
        SplitIfInside(layer, from);

        var removed = 0;
        for (var i = layer.Items.Count - 1; i >= 0; i--)
        {
            var span = Timeline.Span(layer, i);
            if (span.Start >= from - 0.01 && span.End <= to + 0.01)
            {
                DeleteItem(layer, i, ripple: false);
                removed++;
            }
        }

        if (removed == 0)
        {
            return EditResult.Fail($"Nothing between {TimeFormat.Words(from)} and {TimeFormat.Words(to)} on {layer.Name}.");
        }

        if (ripple)
        {
            var spans = Timeline.Spans(layer);
            for (var i = 0; i < spans.Count; i++)
            {
                if (spans[i].Start >= to - 0.01)
                {
                    var item = layer.Items[i];
                    item.GapBefore = Math.Max(0, item.GapBefore - (to - from));
                    break;
                }
            }
        }

        var focus = layer.Items.Count == 0 ? (Guid?)null : layer.Items[Math.Max(0, Timeline.NearestItem(layer, from))].Id;
        return EditResult.Success(
            $"Removed {TimeFormat.Words(to - from)} from {layer.Name}{(ripple ? ", later items moved earlier" : "")}.",
            focus, layer.Id);
    }

    private static void SplitIfInside(Layer layer, double time)
    {
        var index = Timeline.ItemAt(layer, time);
        if (index < 0)
        {
            return;
        }

        var span = Timeline.Span(layer, index);
        if (time - span.Start >= Timeline.MinItemDuration && span.End - time >= Timeline.MinItemDuration)
        {
            SplitItem(layer, index, time);
        }
    }

    // ---------------------------------------------------------------- moving items

    /// <summary>
    /// Changes the play order: moves the item <paramref name="delta"/> places. Gaps and transitions stay
    /// where they are on the timeline; only the content changes places.
    /// </summary>
    public static EditResult MoveItem(Layer layer, int index, int delta) =>
        MoveItemTo(layer, index, index + delta);

    public static EditResult MoveItemTo(Layer layer, int index, int target)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        if (target < 0)
        {
            return EditResult.Fail("Already the first item.");
        }

        if (target >= layer.Items.Count)
        {
            return EditResult.Fail("Already the last item.");
        }

        if (target == index)
        {
            return EditResult.Fail(index == 0 ? "Already the first item." : "Already the last item.");
        }

        var gaps = layer.Items.Select(i => i.GapBefore).ToArray();
        var transitions = layer.Items.Select(i => i.TransitionIn).ToArray();
        var item = layer.Items[index];
        layer.Items.RemoveAt(index);
        layer.Items.Insert(target, item);
        for (var i = 0; i < layer.Items.Count; i++)
        {
            layer.Items[i].GapBefore = gaps[i];
            layer.Items[i].TransitionIn = transitions[i];
        }

        var span = Timeline.Span(layer, target);
        return EditResult.Success(
            $"Moved to position {target + 1} of {layer.Items.Count}, starts {TimeFormat.Words(span.Start)}.", item.Id);
    }

    /// <summary>Slides an item later (positive) or earlier (negative) in time.</summary>
    public static EditResult SlideItem(Layer layer, int index, double delta, bool ripple)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var before = Timeline.Spans(layer);
        var item = layer.Items[index];
        var previousEnd = index == 0 ? 0 : before[index - 1].End;
        var desiredStart = before[index].Start + delta;
        var newGap = Math.Max(0, desiredStart - previousEnd);

        if (Math.Abs(newGap - item.GapBefore) < Timeline.Epsilon)
        {
            if (delta > 0)
            {
                return EditResult.Fail("It overlaps the previous item because of its transition. Slide by more than the transition length, or remove the transition.");
            }

            return EditResult.Fail(index == 0
                ? "Already at the start of the project."
                : "Can't slide earlier: it already touches the previous item.");
        }

        var oldGap = item.GapBefore;
        var hasNext = index + 1 < layer.Items.Count;
        var oldNextGap = hasNext ? layer.Items[index + 1].GapBefore : 0;
        item.GapBefore = newGap;

        if (!ripple && hasNext)
        {
            var next = layer.Items[index + 1];
            var newEnd = Timeline.Span(layer, index).End;
            var room = before[index + 1].Start - newEnd;
            if (room < -Timeline.Epsilon)
            {
                item.GapBefore = oldGap;
                next.GapBefore = oldNextGap;
                return EditResult.Fail("Can't slide later: the next item is in the way. Turn on ripple with Control R to push later items along.");
            }

            next.GapBefore = Math.Max(0, room);
        }

        var after = Timeline.Span(layer, index);
        var moved = after.Start - before[index].Start;
        var direction = moved >= 0 ? "later" : "earlier";
        return EditResult.Success(
            $"Slid {TimeFormat.Words(Math.Abs(moved))} {direction}, starts {TimeFormat.Words(after.Start)}.");
    }

    // ---------------------------------------------------------------- cutting items

    public static EditResult SplitItem(Layer layer, int index, double time)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item at the playhead on this layer.");
        }

        var span = Timeline.Span(layer, index);
        var offset = time - span.Start;
        if (offset < Timeline.MinItemDuration || span.End - time < Timeline.MinItemDuration)
        {
            return EditResult.Fail("The playhead is too close to the edge of the item to split it.");
        }

        var item = layer.Items[index];
        var second = item.Clone();
        second.Duration = item.Duration - offset;
        if (item.Kind is ItemKind.Video or ItemKind.Audio)
        {
            second.SourceIn = item.SourceIn + offset;
        }

        second.GapBefore = 0;
        second.TransitionIn = new TransitionSpec();
        second.FadeIn = 0;
        item.Duration = offset;
        item.FadeOut = 0;
        item.FadeIn = Math.Min(item.FadeIn, item.Duration / 2);
        second.FadeOut = Math.Min(second.FadeOut, second.Duration / 2);
        layer.Items.Insert(index + 1, second);
        return EditResult.Success(
            $"Split {item.Name} at {TimeFormat.Words(time)}. First part {TimeFormat.Words(item.Duration)}, second part {TimeFormat.Words(second.Duration)}.",
            second.Id);
    }

    /// <summary>
    /// Cuts off the beginning of an item up to <paramref name="time"/>. With ripple the item keeps its start
    /// and everything after it moves earlier; without ripple its end stays put and a gap opens before it.
    /// </summary>
    public static EditResult TrimStart(Layer layer, int index, double time, bool ripple)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var spans = Timeline.Spans(layer);
        var span = spans[index];
        var cut = time - span.Start;
        if (cut <= Timeline.Epsilon)
        {
            return EditResult.Fail("Move the playhead inside the item, after its start, then trim.");
        }

        if (span.End - time < Timeline.MinItemDuration)
        {
            return EditResult.Fail("That would leave nothing of the item.");
        }

        var item = layer.Items[index];
        var previousEnd = index == 0 ? 0 : spans[index - 1].End;
        if (item.Kind is ItemKind.Video or ItemKind.Audio)
        {
            item.SourceIn += cut;
        }

        item.Duration -= cut;
        if (!ripple)
        {
            item.GapBefore = Math.Max(0, time - previousEnd);
        }

        item.FadeIn = Math.Min(item.FadeIn, item.Duration / 2);
        item.FadeOut = Math.Min(item.FadeOut, item.Duration / 2);
        return EditResult.Success($"Trimmed {TimeFormat.Words(cut)} from the start. Now {TimeFormat.Words(item.Duration)} long.");
    }

    /// <summary>Cuts off the end of an item from <paramref name="time"/>.</summary>
    public static EditResult TrimEnd(Layer layer, int index, double time, bool ripple)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var spans = Timeline.Spans(layer);
        var span = spans[index];
        if (time >= span.End - Timeline.Epsilon)
        {
            return EditResult.Fail("Move the playhead inside the item, before its end, then trim.");
        }

        if (time - span.Start < Timeline.MinItemDuration)
        {
            return EditResult.Fail("That would leave nothing of the item.");
        }

        var item = layer.Items[index];
        var cut = span.End - time;
        item.Duration = time - span.Start;
        item.FadeIn = Math.Min(item.FadeIn, item.Duration / 2);
        item.FadeOut = Math.Min(item.FadeOut, item.Duration / 2);
        if (!ripple && index + 1 < layer.Items.Count)
        {
            var next = layer.Items[index + 1];
            next.GapBefore = Math.Max(0, spans[index + 1].Start - time);
        }

        return EditResult.Success($"Trimmed {TimeFormat.Words(cut)} from the end. Now {TimeFormat.Words(item.Duration)} long.");
    }

    // ---------------------------------------------------------------- item settings

    /// <summary>Copies edited settings onto an item, keeping every value within safe limits.</summary>
    public static EditResult UpdateItem(Layer layer, int index, Item edited)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var item = layer.Items[index];
        item.Name = string.IsNullOrWhiteSpace(edited.Name) ? item.Name : edited.Name.Trim();
        if (item.Kind == ItemKind.Color && ColorCatalog.IsKnown(edited.ColorName))
        {
            item.ColorName = edited.ColorName;
        }

        if (item.IsTimeLimited)
        {
            item.SourceIn = Math.Clamp(edited.SourceIn, 0, Math.Max(0, item.SourceDuration - Timeline.MinItemDuration));
        }

        item.Duration = Math.Clamp(edited.Duration, Timeline.MinItemDuration,
            Math.Max(Timeline.MinItemDuration, item.AvailableDuration));
        item.GapBefore = Math.Max(0, edited.GapBefore);
        item.Volume = Math.Clamp(edited.Volume, 0, 4);
        item.AudioMuted = edited.AudioMuted;
        item.FadeIn = Math.Clamp(edited.FadeIn, 0, item.Duration / 2);
        item.FadeOut = Math.Clamp(edited.FadeOut, 0, item.Duration / 2);
        item.TransitionIn = new TransitionSpec
        {
            Video = TransitionCatalog.IsKnownVideo(edited.TransitionIn.Video) ? edited.TransitionIn.Video : TransitionSpec.None,
            Audio = TransitionCatalog.IsKnownAudio(edited.TransitionIn.Audio) ? edited.TransitionIn.Audio : TransitionSpec.None,
            Duration = Math.Clamp(edited.TransitionIn.Duration, 0.1, 10),
        };
        item.Placement = ClampPlacement(edited.Placement);
        item.Audio = ClampAudio(edited.Audio);
        return EditResult.Success($"Updated {item.Name}.", item.Id);
    }

    public static Placement ClampPlacement(Placement p) => new()
    {
        Position = p.Position,
        SizePercent = Math.Clamp(p.SizePercent, 5, 100),
        OpacityPercent = Math.Clamp(p.OpacityPercent, 0, 100),
        CustomXPercent = Math.Clamp(p.CustomXPercent, 0, 100),
        CustomYPercent = Math.Clamp(p.CustomYPercent, 0, 100),
    };

    public static AudioCleanup ClampAudio(AudioCleanup a) => new()
    {
        NoiseReduction = a.NoiseReduction,
        Hum = a.Hum,
        RemoveRumble = a.RemoveRumble,
        LowDb = Math.Clamp(a.LowDb, -12, 12),
        MidDb = Math.Clamp(a.MidDb, -12, 12),
        HighDb = Math.Clamp(a.HighDb, -12, 12),
        Voice = a.Voice,
        Normalize = a.Normalize,
    };

    /// <summary>Sets the transition into the item that starts at a marker on this layer.</summary>
    public static EditResult SetTransitionAt(Layer layer, double time, TransitionSpec spec)
    {
        var spans = Timeline.Spans(layer);
        var best = -1;
        var bestDistance = double.MaxValue;
        for (var i = 1; i < spans.Count; i++)
        {
            // A join runs from where this item starts to where the previous one ends (they differ when a
            // transition overlaps them or a gap separates them). Measure the distance to that stretch.
            var from = Math.Min(spans[i].Start, spans[i - 1].End);
            var to = Math.Max(spans[i].Start, spans[i - 1].End);
            var distance = time < from ? from - time : time > to ? time - to : 0;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        if (best < 0 || bestDistance > 0.5)
        {
            return EditResult.Fail($"There is no join between two items on {layer.Name} at {TimeFormat.Words(time)}. Split the item here first with S, or move the marker to a join.");
        }

        var item = layer.Items[best];
        item.TransitionIn = new TransitionSpec
        {
            Video = TransitionCatalog.IsKnownVideo(spec.Video) ? spec.Video : TransitionSpec.None,
            Audio = TransitionCatalog.IsKnownAudio(spec.Audio) ? spec.Audio : TransitionSpec.None,
            Duration = Math.Clamp(spec.Duration, 0.1, 10),
        };

        var name = layer.Kind == LayerKind.Video
            ? TransitionCatalog.FindVideo(item.TransitionIn.Video).Name
            : TransitionCatalog.FindAudio(item.TransitionIn.Audio).Name;
        var warning = item.GapBefore > Timeline.Epsilon
            ? $" It will only play when the items touch; there is a gap of {TimeFormat.Words(item.GapBefore)}."
            : "";
        return EditResult.Success($"{name} into {item.Name}.{warning}", item.Id);
    }

    // ---------------------------------------------------------------- markers and range

    public static EditResult AddMarker(Project project, double time, string? name = null)
    {
        if (project.Markers.Any(m => Math.Abs(m.Time - time) < MarkerTolerance))
        {
            return EditResult.Fail($"There is already a marker at {TimeFormat.Words(time)}.");
        }

        var marker = new Marker
        {
            Time = Math.Max(0, time),
            Name = string.IsNullOrWhiteSpace(name) ? NextMarkerName(project) : name.Trim(),
        };
        project.Markers.Add(marker);
        SortMarkers(project);
        return EditResult.Success($"{marker.Name} added at {TimeFormat.Words(marker.Time)}.");
    }

    public static string NextMarkerName(Project project)
    {
        var number = project.Markers.Count + 1;
        while (project.Markers.Any(m => m.Name == $"Marker {number}"))
        {
            number++;
        }

        return $"Marker {number}";
    }

    public static EditResult DeleteMarker(Project project, Guid markerId)
    {
        var marker = project.Markers.FirstOrDefault(m => m.Id == markerId);
        if (marker is null)
        {
            return EditResult.Fail("That marker no longer exists.");
        }

        project.Markers.Remove(marker);
        return EditResult.Success($"Deleted {marker.Name}.");
    }

    public static EditResult UpdateMarker(Project project, Guid markerId, string name, double time)
    {
        var marker = project.Markers.FirstOrDefault(m => m.Id == markerId);
        if (marker is null)
        {
            return EditResult.Fail("That marker no longer exists.");
        }

        if (project.Markers.Any(m => m.Id != markerId && Math.Abs(m.Time - time) < MarkerTolerance))
        {
            return EditResult.Fail($"There is already a marker at {TimeFormat.Words(time)}.");
        }

        marker.Name = string.IsNullOrWhiteSpace(name) ? marker.Name : name.Trim();
        marker.Time = Math.Max(0, time);
        SortMarkers(project);
        return EditResult.Success($"{marker.Name} at {TimeFormat.Words(marker.Time)}.");
    }

    public static EditResult SetRangeStart(Project project, double time)
    {
        project.RangeStart = Math.Max(0, time);
        return EditResult.Success($"Range start {TimeFormat.Words(time)}.{FixRange(project)}");
    }

    public static EditResult SetRangeEnd(Project project, double time)
    {
        project.RangeEnd = Math.Max(0, time);
        return EditResult.Success($"Range end {TimeFormat.Words(time)}.{FixRange(project)}");
    }

    public static EditResult ClearRange(Project project)
    {
        if (project.RangeStart is null && project.RangeEnd is null)
        {
            return EditResult.Fail("No range is set.");
        }

        project.RangeStart = null;
        project.RangeEnd = null;
        return EditResult.Success("Range cleared.");
    }

    private static string FixRange(Project project)
    {
        if (project.RangeStart is { } s && project.RangeEnd is { } e)
        {
            if (s > e)
            {
                project.RangeStart = e;
                project.RangeEnd = s;
                return $" Start and end swapped. Range is {TimeFormat.Words(s - e)} long.";
            }

            return $" Range is {TimeFormat.Words(e - s)} long.";
        }

        return "";
    }

    /// <summary>The next (direction 1) or previous (-1) marker time after <paramref name="time"/>.</summary>
    public static double? NeighbourMarker(Project project, double time, int direction)
    {
        var times = Timeline.MarkerTimes(project);
        if (direction > 0)
        {
            foreach (var t in times)
            {
                if (t > time + MarkerTolerance)
                {
                    return t;
                }
            }
        }
        else
        {
            for (var i = times.Count - 1; i >= 0; i--)
            {
                if (times[i] < time - MarkerTolerance)
                {
                    return times[i];
                }
            }
        }

        return null;
    }

    /// <summary>A spoken name for whatever marker sits at <paramref name="time"/>.</summary>
    public static string MarkerLabelAt(Project project, double time)
    {
        var names = new List<string>();
        if (project.RangeStart is { } s && Math.Abs(s - time) < MarkerTolerance)
        {
            names.Add("Range start");
        }

        if (project.RangeEnd is { } e && Math.Abs(e - time) < MarkerTolerance)
        {
            names.Add("Range end");
        }

        names.AddRange(project.Markers.Where(m => Math.Abs(m.Time - time) < MarkerTolerance).Select(m => m.Name));
        return names.Count == 0 ? "Marker" : string.Join(", ", names);
    }

    private static void SortMarkers(Project project) => project.Markers.Sort((a, b) => a.Time.CompareTo(b.Time));
}
