using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Selecting several items on a layer (Shift arrows, Control Space, Control A) and doing one thing to all of
/// them at once; stretching items and changing clip speed; and keys that speed up while held.
/// </summary>
public sealed partial class EditorController
{
    private readonly HashSet<Guid> _selection = new();
    private Guid? _selectionLayerId;
    private Guid? _anchorId;

    /// <summary>
    /// How many times the normal step a held key moves (1, then more after a couple of seconds). The window
    /// sets it for each key press from <see cref="HoldAccelerator"/>.
    /// </summary>
    public double HoldFactor { get; set; } = 1;

    /// <summary>True while a key is auto-repeating: changes then fold into one undo step.</summary>
    public bool Repeating { get; set; }

    /// <summary>The selected items' indexes on the focused layer, in order. Empty when nothing is selected.</summary>
    public IReadOnlyList<int> SelectedIndexes
    {
        get
        {
            if (FocusedLayer is not { } layer || _selectionLayerId != layer.Id)
            {
                return [];
            }

            return Enumerable.Range(0, layer.Items.Count).Where(i => _selection.Contains(layer.Items[i].Id)).ToList();
        }
    }

    public bool IsSelected(Guid itemId) => _selectionLayerId == FocusedLayerId && _selection.Contains(itemId);

    public bool HasSelection => SelectedIndexes.Count > 0;

    /// <summary>The items a command works on: the selection, or else the focused item.</summary>
    private IReadOnlyList<int> TargetIndexes()
    {
        var selected = SelectedIndexes;
        return selected.Count > 0 ? selected : FocusedItemIndex >= 0 ? [FocusedItemIndex] : [];
    }

    public void ClearSelection()
    {
        _selection.Clear();
        _anchorId = null;
    }

    /// <summary>Handles selection, stretch and speed commands, and the many-item versions of edits. Null otherwise.</summary>
    private Feedback? ExecuteSelection(CommandId command)
    {
        switch (command)
        {
            case CommandId.SelectPrevious: return ExtendSelection(-1);
            case CommandId.SelectNext: return ExtendSelection(1);
            case CommandId.ToggleSelect: return ToggleSelected();
            case CommandId.SelectAll: return SelectAllOnLayer();
            case CommandId.Stop when HasSelection:
                var count = SelectedIndexes.Count;
                ClearSelection();
                return new Feedback { Speech = $"Selection of {Plural(count, "item")} cleared.", Earcon = EarconEvent.Navigate, Playback = PlaybackRequest.Stop };

            case CommandId.Longer: return Stretch(Settings.SlideStep * HoldFactor);
            case CommandId.Shorter: return Stretch(-Settings.SlideStep * HoldFactor);
            case CommandId.LongerFine: return Stretch(Settings.FineSlideStep * HoldFactor);
            case CommandId.ShorterFine: return Stretch(-Settings.FineSlideStep * HoldFactor);
            case CommandId.Faster: return ChangeSpeed(1);
            case CommandId.Slower: return ChangeSpeed(-1);
            case CommandId.NormalSpeed: return ChangeSpeed(0);
        }

        if (SelectedIndexes.Count == 0)
        {
            return null;
        }

        return command switch
        {
            CommandId.Delete => DeleteSelected(Settings.Ripple),
            CommandId.DeleteKeepTiming => DeleteSelected(false),
            CommandId.Copy => CopySelected(),
            CommandId.Cut => CutSelected(),
            CommandId.Duplicate => DuplicateSelected(),
            CommandId.ToggleItemMute => MuteSelected(),
            CommandId.ItemVolumeUp => SelectedVolume(0.1),
            CommandId.ItemVolumeDown => SelectedVolume(-0.1),
            CommandId.SlideEarlier => SlideSelected(-Settings.SlideStep * HoldFactor),
            CommandId.SlideLater => SlideSelected(Settings.SlideStep * HoldFactor),
            CommandId.SlideEarlierFine => SlideSelected(-Settings.FineSlideStep * HoldFactor),
            CommandId.SlideLaterFine => SlideSelected(Settings.FineSlideStep * HoldFactor),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ choosing items

    private Feedback ExtendSelection(int direction)
    {
        if (FocusedLayer is not { } layer || layer.Items.Count == 0 || FocusedItemIndex < 0)
        {
            return Edge(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        StartSelectionOnThisLayer();
        _anchorId ??= layer.Items[FocusedItemIndex].Id;
        var target = FocusedItemIndex + direction;
        var atEdge = target < 0 || target >= layer.Items.Count;
        target = Math.Clamp(target, 0, layer.Items.Count - 1);
        var anchor = Math.Max(0, layer.IndexOf(_anchorId.Value));
        _selection.Clear();
        for (var i = Math.Min(anchor, target); i <= Math.Max(anchor, target); i++)
        {
            _selection.Add(layer.Items[i].Id);
        }

        FocusedItemId = layer.Items[target].Id;
        Playhead = Timeline.Span(layer, target).Start;
        var edge = atEdge ? (direction < 0 ? " First item." : " Last item.") : "";
        return new Feedback
        {
            Speech = $"{layer.Items[target].Name}. {SelectionSummary()}{edge}",
            Earcon = atEdge ? EarconEvent.Edge : EarconEvent.Navigate,
            Playback = PlaybackRequest.Seek,
        };
    }

    private Feedback ToggleSelected()
    {
        if (FocusedItem is not { } item)
        {
            return Edge(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        StartSelectionOnThisLayer();
        var added = _selection.Add(item.Id);
        if (!added)
        {
            _selection.Remove(item.Id);
        }

        _anchorId = item.Id;
        return Say($"{(added ? "Selected" : "Unselected")} {item.Name}. {SelectionSummary()}", EarconEvent.Navigate);
    }

    private Feedback SelectAllOnLayer()
    {
        if (FocusedLayer is not { } layer || layer.Items.Count == 0)
        {
            return Edge(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        StartSelectionOnThisLayer();
        foreach (var item in layer.Items)
        {
            _selection.Add(item.Id);
        }

        return Say($"All {SelectionSummary()} Escape clears the selection.", EarconEvent.Navigate);
    }

    private void StartSelectionOnThisLayer()
    {
        if (_selectionLayerId != FocusedLayerId)
        {
            ClearSelection();
            _selectionLayerId = FocusedLayerId;
        }
    }

    /// <summary>"3 items selected, 12 seconds in all."</summary>
    public string SelectionSummary()
    {
        var indexes = SelectedIndexes;
        if (indexes.Count == 0)
        {
            return "Nothing selected.";
        }

        var total = indexes.Sum(i => FocusedLayer!.Items[i].Duration);
        return $"{Plural(indexes.Count, "item")} selected, {TimeFormat.Words(total)} in all.";
    }

    // ------------------------------------------------------------------ doing things to many items

    private Feedback DeleteSelected(bool ripple)
    {
        var layerId = FocusedLayerId!.Value;
        var indexes = SelectedIndexes;
        var total = indexes.Sum(i => FocusedLayer!.Items[i].Duration);
        Guid? focus = null;
        var result = Session.Execute("delete items", p =>
        {
            var layer = p.FindLayer(layerId)!;
            foreach (var index in indexes.OrderByDescending(i => i))
            {
                var deleted = ProjectEditor.DeleteItem(layer, index, ripple);
                if (!deleted.Ok)
                {
                    return deleted;
                }
            }

            focus = layer.Items.Count == 0 ? null : layer.Items[Math.Min(indexes[0], layer.Items.Count - 1)].Id;
            var left = layer.Items.Count == 1 ? "1 item" : $"{layer.Items.Count} items";
            return EditResult.Success($"Deleted {Plural(indexes.Count, "selected item")}, {TimeFormat.Words(total)} in all. {left} left on {layer.Name}.", focus);
        });
        if (result.Ok)
        {
            ClearSelection();
        }

        return AfterItemEdit(result, EarconEvent.Deleted);
    }

    private Feedback CopySelected()
    {
        var layer = FocusedLayer!;
        _clipboard = SelectedIndexes.Select(i => layer.Items[i].Clone()).ToList();
        _clipboardLayerKind = layer.Kind;
        return Say($"Copied {Plural(_clipboard.Count, "item")}. Control V pastes them after the focused item.", EarconEvent.Done);
    }

    private Feedback CutSelected()
    {
        CopySelected();
        var deleted = DeleteSelected(Settings.Ripple);
        return deleted.IsError ? deleted : deleted with { Speech = "Cut. " + deleted.Speech };
    }

    private Feedback DuplicateSelected()
    {
        var layerId = FocusedLayerId!.Value;
        var indexes = SelectedIndexes;
        var copies = indexes.Select(i => FocusedLayer!.Items[i].Clone()).ToList();
        copies[0].GapBefore = 0;
        var result = Session.Execute("duplicate items", p =>
        {
            var layer = p.FindLayer(layerId)!;
            var at = indexes[^1];
            foreach (var copy in copies)
            {
                layer.Items.Insert(++at, copy);
            }

            return EditResult.Success($"Duplicated {Plural(copies.Count, "item")}; the copies follow the last selected item. The copies are now selected.", copies[0].Id);
        });
        if (result.Ok)
        {
            _selection.Clear();
            foreach (var copy in copies)
            {
                _selection.Add(copy.Id);
            }

            _anchorId = copies[0].Id;
        }

        return AfterItemEdit(result, EarconEvent.Done);
    }

    private Feedback MuteSelected()
    {
        var layerId = FocusedLayerId!.Value;
        var indexes = SelectedIndexes.Where(i => FocusedLayer!.Items[i].HasAudio).ToList();
        if (indexes.Count == 0)
        {
            return Error("None of the selected items has sound.");
        }

        var mute = indexes.Any(i => !FocusedLayer!.Items[i].AudioMuted);
        var result = Session.Execute("mute items", p =>
        {
            var layer = p.FindLayer(layerId)!;
            foreach (var i in indexes)
            {
                layer.Items[i].AudioMuted = mute;
            }

            return EditResult.Success($"Sound {(mute ? "muted" : "on")} for {Plural(indexes.Count, "item")}.");
        });
        return ToFeedback(result, mute ? EarconEvent.Muted : EarconEvent.Unmuted);
    }

    private Feedback SelectedVolume(double delta)
    {
        var layerId = FocusedLayerId!.Value;
        var indexes = SelectedIndexes.Where(i => FocusedLayer!.Items[i].HasAudio).ToList();
        if (indexes.Count == 0)
        {
            return Error("None of the selected items has sound.");
        }

        var result = Session.Execute("item volume", p =>
        {
            var layer = p.FindLayer(layerId)!;
            var levels = new List<double>();
            foreach (var i in indexes)
            {
                var item = layer.Items[i];
                item.Volume = Math.Round(Math.Clamp(item.Volume + delta, 0, 4), 2);
                if (item.Volume > 0)
                {
                    item.AudioMuted = false;
                }

                levels.Add(item.Volume);
            }

            var level = levels.Distinct().Count() == 1 ? Describer.Percent(levels[0]) : $"{Describer.Percent(levels.Min())} to {Describer.Percent(levels.Max())}";
            return EditResult.Success($"Volume {level} for {Plural(indexes.Count, "item")}.");
        }, merge: Repeating);
        return ToFeedback(result, delta > 0 ? EarconEvent.Unmuted : EarconEvent.Muted);
    }

    /// <summary>
    /// Slides every selected item. With ripple the first one slides and everything after follows; without
    /// ripple each moves on its own, the one in front first, so neighbours make room for each other.
    /// </summary>
    private Feedback SlideSelected(double seconds)
    {
        var layerId = FocusedLayerId!.Value;
        var indexes = SelectedIndexes;
        var ripple = Settings.Ripple;
        var before = Timeline.Span(FocusedLayer!, indexes[0]).Start;
        var result = Session.Execute("slide", p =>
        {
            var layer = p.FindLayer(layerId)!;
            if (ripple)
            {
                return ProjectEditor.SlideItem(layer, indexes[0], seconds, ripple: true);
            }

            var order = seconds > 0 ? indexes.OrderByDescending(i => i) : indexes.OrderBy(i => i);
            foreach (var i in order)
            {
                var slid = ProjectEditor.SlideItem(layer, i, seconds, ripple: false);
                if (!slid.Ok)
                {
                    return slid;
                }
            }

            return EditResult.Success("");
        }, merge: Repeating);
        if (!result.Ok)
        {
            return Error(result.Message);
        }

        var moved = Timeline.Span(FocusedLayer!, indexes[0]).Start - before;
        Playhead = Timeline.Span(FocusedLayer!, FocusedItemIndex).Start;
        return Changed($"Slid {Plural(indexes.Count, "item")} {TimeFormat.Words(Math.Abs(moved))} {(moved >= 0 ? "later" : "earlier")}; the first starts {TimeFormat.Words(Timeline.Span(FocusedLayer!, indexes[0]).Start)}.", EarconEvent.Done);
    }

    private Feedback Stretch(double seconds)
    {
        var indexes = TargetIndexes();
        if (indexes.Count == 0)
        {
            return Error(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        var layerId = FocusedLayerId!.Value;
        var ripple = Settings.Ripple;
        var result = Session.Execute("stretch", p =>
        {
            var layer = p.FindLayer(layerId)!;
            EditResult last = EditResult.Fail("");
            foreach (var i in indexes.OrderByDescending(i => i))
            {
                last = StretchEditing.ChangeLength(layer, i, seconds, ripple);
                if (!last.Ok)
                {
                    return last;
                }
            }

            return indexes.Count == 1
                ? last
                : EditResult.Success($"{(seconds > 0 ? "Longer" : "Shorter")} by {TimeFormat.Words(Math.Abs(seconds))}: {Plural(indexes.Count, "item")}.");
        }, merge: Repeating);
        return ToFeedback(result, EarconEvent.Done);
    }

    /// <summary>One speed step faster (1), slower (-1), or back to normal (0).</summary>
    private Feedback ChangeSpeed(int direction)
    {
        var indexes = TargetIndexes().Where(i => FocusedLayer!.Items[i].Kind is ItemKind.Video or ItemKind.Audio).ToList();
        if (indexes.Count == 0)
        {
            return Error(FocusedItem is { } item
                ? $"{item.Name} is not a video or sound clip. Pictures, colour cards and text are stretched with Alt equals and Alt minus."
                : "No clip selected.");
        }

        var layerId = FocusedLayerId!.Value;
        var ripple = Settings.Ripple;
        var speeds = indexes.Select(i => FocusedLayer!.Items[i].EffectiveSpeed).ToList();
        var targets = speeds.Select(s => direction == 0 ? 1 : StretchEditing.NextSpeed(s, direction)).ToList();
        if (targets.All(t => t is null) || targets.Zip(speeds).All(p => p.First is { } t && Math.Abs(t - p.Second) < 1e-6))
        {
            return Edge(direction > 0 ? $"Already at the fastest, {Item.MaxSpeed:0} times speed."
                : direction < 0 ? $"Already at the slowest, a quarter speed." : "Already at normal speed.");
        }

        var result = Session.Execute("speed", p =>
        {
            var layer = p.FindLayer(layerId)!;
            EditResult last = EditResult.Fail("");
            for (var k = indexes.Count - 1; k >= 0; k--)
            {
                if (targets[k] is not { } speed)
                {
                    continue;
                }

                last = StretchEditing.SetSpeed(layer, indexes[k], speed, ripple);
                if (!last.Ok)
                {
                    return last;
                }
            }

            return indexes.Count == 1 ? last : EditResult.Success($"Speed changed for {Plural(indexes.Count, "clip")}.");
        });
        return ToFeedback(result, EarconEvent.Done);
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>
/// Makes a held key move further each time: normal steps for the first two seconds, then four times, and
/// after four seconds ten times. Letting go starts again from normal.
/// </summary>
public sealed class HoldAccelerator
{
    public static readonly TimeSpan FirstSpeedUp = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SecondSpeedUp = TimeSpan.FromSeconds(4);

    private KeyChord? _held;
    private DateTime _since;

    /// <summary>The factor for this key press. <paramref name="isRepeat"/> is the keyboard's auto-repeat flag.</summary>
    public double Factor(KeyChord chord, bool isRepeat, DateTime now)
    {
        if (!isRepeat || _held != chord)
        {
            _held = chord;
            _since = now;
            return 1;
        }

        var held = now - _since;
        return held >= SecondSpeedUp ? 10 : held >= FirstSpeedUp ? 4 : 1;
    }
}
