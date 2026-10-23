using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Stretching items from the keyboard: making any item longer or shorter, and playing a video or sound clip
/// faster or slower (which changes its length but keeps all of its content).
/// </summary>
public static class StretchEditing
{
    /// <summary>The speeds offered one step at a time.</summary>
    public static IReadOnlyList<double> SpeedSteps { get; } = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 3, 4];

    /// <summary>The next speed step up (<paramref name="direction"/> 1) or down (-1), or null at the limit.</summary>
    public static double? NextSpeed(double current, int direction)
    {
        var next = direction > 0
            ? SpeedSteps.Where(s => s > current + 1e-6).Cast<double?>().FirstOrDefault()
            : SpeedSteps.Where(s => s < current - 1e-6).Cast<double?>().LastOrDefault();
        return next;
    }

    /// <summary>
    /// Plays a clip at <paramref name="speed"/>, keeping the same part of the file: twice as fast is half as long.
    /// Without ripple, the next item keeps its time (and the change fails if it would overlap it).
    /// </summary>
    public static EditResult SetSpeed(Layer layer, int index, double speed, bool ripple)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var item = layer.Items[index];
        if (item.Kind is not (ItemKind.Video or ItemKind.Audio))
        {
            return EditResult.Fail($"{item.Name} is not a video or sound clip, so it has no speed. Make it longer or shorter instead.");
        }

        speed = Math.Round(Math.Clamp(speed, Item.MinSpeed, Item.MaxSpeed), 3);
        var sourceLength = item.SourceLength;
        var duration = Math.Max(Timeline.MinItemDuration, sourceLength / speed);
        return Resize(layer, index, duration, ripple, () =>
        {
            item.Speed = speed;
            item.Duration = duration;
        }, () =>
        {
            var what = Math.Abs(speed - 1) < 1e-6 ? "Normal speed" : char.ToUpperInvariant(Describer.SpeedText(speed)[0]) + Describer.SpeedText(speed)[1..];
            return $"{what}, now {TimeFormat.Words(item.Duration)} long.";
        });
    }

    /// <summary>
    /// Makes an item longer or shorter by <paramref name="delta"/> seconds, keeping its start. A clip shows more
    /// or less of its file (as far as the file goes); pictures, colour cards and text simply stay on longer.
    /// </summary>
    public static EditResult ChangeLength(Layer layer, int index, double delta, bool ripple)
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item selected.");
        }

        var item = layer.Items[index];
        var target = Math.Max(Timeline.MinItemDuration, item.Duration + delta);
        if (item.IsTimeLimited)
        {
            target = Math.Min(target, item.AvailableDuration);
        }

        target = Math.Round(target, 6);
        if (Math.Abs(target - item.Duration) < 1e-6)
        {
            return EditResult.Fail(delta > 0
                ? $"{item.Name} can't be longer: its file ends here. Play it slower to stretch it."
                : $"{item.Name} is already as short as it can be.");
        }

        return Resize(layer, index, target, ripple, () =>
        {
            item.Duration = target;
            item.FadeIn = Math.Min(item.FadeIn, target / 2);
            item.FadeOut = Math.Min(item.FadeOut, target / 2);
        }, () => $"{(delta > 0 ? "Longer" : "Shorter")}, now {TimeFormat.Words(item.Duration)} long, ends {TimeFormat.Words(Timeline.Span(layer, index).End)}.");
    }

    /// <summary>Applies a change of length; without ripple the next item stays where it is.</summary>
    private static EditResult Resize(Layer layer, int index, double newDuration, bool ripple, Action apply, Func<string> message)
    {
        var growth = newDuration - layer.Items[index].Duration;
        if (!ripple && index + 1 < layer.Items.Count)
        {
            // The next item keeps its time: the gap before it shrinks or grows by the same amount.
            var next = layer.Items[index + 1];
            if (growth > next.GapBefore + Timeline.Epsilon)
            {
                return EditResult.Fail("The next item is in the way. Turn on ripple with Control R to push later items along, or slide the next item first.");
            }

            apply();
            next.GapBefore = Math.Max(0, next.GapBefore - growth);
        }
        else
        {
            apply();
        }

        return EditResult.Success(message(), layer.Items[index].Id);
    }
}
