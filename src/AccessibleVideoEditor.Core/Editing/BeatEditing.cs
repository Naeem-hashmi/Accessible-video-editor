using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>Puts found beats on music items and lines pictures and clips up with them.</summary>
public static class BeatEditing
{
    /// <summary>Stores the beats of <paramref name="analysis"/> on a music item.</summary>
    public static EditResult SetBeats(Layer layer, int index, BeatAnalysis analysis)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item is selected.");
        }

        var item = layer.Items[index];
        if (!analysis.HasSteadyBeat)
        {
            item.Beats.Clear();
            item.Bpm = 0;
            return EditResult.Fail($"No steady beat found in {item.Name}. Try music with drums.");
        }

        item.Beats = [.. analysis.Beats];
        item.Bpm = Math.Round(analysis.Bpm, 1);
        var visible = BeatsOf(layer, index).Count;
        return EditResult.Success($"{item.Name}: {Math.Round(analysis.Bpm):0} beats per minute, {visible} beats on the timeline.", item.Id);
    }

    /// <summary>Timeline times of the beats of one item, only where the item actually plays.</summary>
    public static IReadOnlyList<double> BeatsOf(Layer layer, int index)
    {
        var item = layer.Items[index];
        var span = Timeline.Span(layer, index);
        return item.Beats
            .Select(b => span.Start + (b - item.SourceIn))
            .Where(t => t >= span.Start - Timeline.Epsilon && t <= span.End + Timeline.Epsilon)
            .ToList();
    }

    /// <summary>Every beat on the timeline from every unmuted layer, sorted.</summary>
    public static IReadOnlyList<double> BeatTimes(Project project)
    {
        var times = new List<double>();
        foreach (var layer in project.Layers.Where(l => !l.Muted))
        {
            for (var i = 0; i < layer.Items.Count; i++)
            {
                if (layer.Items[i].Beats.Count > 0)
                {
                    times.AddRange(BeatsOf(layer, i));
                }
            }
        }

        times.Sort();
        return times;
    }

    /// <summary>The next (direction 1) or previous (-1) beat from <paramref name="time"/>, or null.</summary>
    public static double? NeighbourBeat(Project project, double time, int direction)
    {
        var beats = BeatTimes(project);
        return direction > 0
            ? beats.Where(b => b > time + 0.01).Cast<double?>().FirstOrDefault()
            : beats.Where(b => b < time - 0.01).Cast<double?>().LastOrDefault();
    }

    /// <summary>
    /// Makes every item on <paramref name="layer"/> end on a beat: pictures and colour cards last a whole
    /// number of groups of <paramref name="beatsPerItem"/> beats, and clips are shortened to the beat
    /// nearest their end (never made longer than their file).
    /// </summary>
    public static EditResult CutToBeats(Project project, Layer layer, int beatsPerItem = 1)
    {
        if (layer.Items.Count == 0)
        {
            return EditResult.Fail($"{layer.Name} is empty.");
        }

        var beats = BeatTimes(project).Where(b => b > 0).ToList();
        if (beats.Count < 2)
        {
            return EditResult.Fail("There are no beats yet. Select the music and use Find beats first.");
        }

        beatsPerItem = Math.Max(1, beatsPerItem);
        var changed = 0;
        for (var i = 0; i < layer.Items.Count; i++)
        {
            var spans = Timeline.Spans(layer);
            var span = spans[i];
            var item = layer.Items[i];
            var minimumEnd = span.Start + Math.Max(Timeline.MinItemDuration, 0.25);
            var maximumEnd = item.IsTimeLimited ? span.Start + item.AvailableDuration : double.PositiveInfinity;
            var candidates = beats.Where(b => b >= minimumEnd - Timeline.Epsilon && b <= maximumEnd + Timeline.Epsilon).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            double end;
            if (item.IsTimeLimited)
            {
                // Clips: the beat nearest their current end that they can reach.
                end = candidates.OrderBy(b => Math.Abs(b - span.End)).First();
            }
            else
            {
                // Pictures: a whole number of groups of beats from the first beat at or after their start.
                var first = beats.FindIndex(b => b >= span.Start - 0.02);
                var ends = new List<double>();
                for (var k = first + beatsPerItem; first >= 0 && k < beats.Count; k += beatsPerItem)
                {
                    if (beats[k] >= minimumEnd - Timeline.Epsilon)
                    {
                        ends.Add(beats[k]);
                    }
                }

                end = (ends.Count > 0 ? ends : candidates).OrderBy(b => Math.Abs(b - span.End)).First();
            }

            var duration = end - span.Start;
            if (Math.Abs(duration - item.Duration) > 0.001)
            {
                item.Duration = duration;
                changed++;
            }
        }

        var count = layer.Items.Count;
        return changed == 0
            ? EditResult.Success($"{layer.Name} already lines up with the beats.")
            : EditResult.Success($"Cut {layer.Name} to the beat: {changed} of {count} items now end on a beat. Length {TimeFormat.Words(Timeline.LayerEnd(layer))}.");
    }
}
