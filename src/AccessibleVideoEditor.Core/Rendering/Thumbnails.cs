using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>Where small pictures go along an item on the drawn timeline, and which moment of the file each shows.</summary>
public static class Thumbnails
{
    public readonly record struct Slot(double X, double SourceTime);

    /// <summary>
    /// Thumbnails <paramref name="thumbWidth"/> pixels wide side by side along an item drawn from <paramref name="left"/>
    /// to <paramref name="right"/>. Each shows the file at the middle of its slot, rounded to a step so that
    /// scrolling and small edits reuse the pictures already made. Only slots between
    /// <paramref name="visibleLeft"/> and <paramref name="visibleRight"/> are returned.
    /// </summary>
    public static IReadOnlyList<Slot> Layout(Item item, double left, double right, double pixelsPerSecond, double thumbWidth,
        double visibleLeft = double.NegativeInfinity, double visibleRight = double.PositiveInfinity)
    {
        if (item.Kind is not (ItemKind.Video or ItemKind.Image) || !item.HasVideo || thumbWidth <= 0 || right - left < thumbWidth / 2)
        {
            return [];
        }

        if (item.Kind == ItemKind.Image)
        {
            return left + thumbWidth > visibleLeft && left < visibleRight ? [new Slot(left, 0)] : [];
        }

        var slots = new List<Slot>();
        var secondsPerThumb = thumbWidth / pixelsPerSecond * item.EffectiveSpeed;
        var step = RoundStep(secondsPerThumb);
        for (var x = left; x < right; x += thumbWidth)
        {
            if (x + thumbWidth < visibleLeft || x > visibleRight)
            {
                continue;
            }

            var middle = item.ToSource((x - left + thumbWidth / 2) / pixelsPerSecond);
            var time = Math.Round(middle / step) * step;
            if (item.SourceDuration > 0)
            {
                time = Math.Min(time, Math.Max(0, item.SourceDuration - 0.1));
            }

            slots.Add(new Slot(x, Math.Max(0, time)));
        }

        return slots;
    }

    /// <summary>A tidy step (0.5, 1, 2, 5, 10, 30 or 60 seconds) close to <paramref name="seconds"/>.</summary>
    public static double RoundStep(double seconds) =>
        new[] { 0.5, 1, 2, 5, 10, 30, 60 }.FirstOrDefault(s => s >= seconds * 0.75, 60);
}
