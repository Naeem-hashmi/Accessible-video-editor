using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Cuts stretches out of a clip and closes the gaps: the clip becomes several pieces of the same file, one
/// after another. Used for shortening pauses and for editing by transcript. Only the clip's own layer
/// moves; other layers keep their times.
/// </summary>
public static class CutEditing
{
    /// <summary>Length of the sound-only fade at every join, so cuts don't click.</summary>
    public const double JoinFade = 0.012;

    /// <summary>Pieces shorter than this are dropped rather than kept as a flash.</summary>
    public const double MinimumPiece = 0.08;

    /// <summary>
    /// Removes <paramref name="cuts"/> (in file seconds) from item <paramref name="index"/>. Cut edges snap
    /// to whole frames at <paramref name="frameRate"/>. Returns how much was removed in the message.
    /// </summary>
    public static EditResult RemoveSourceRanges(Layer layer, int index, IEnumerable<TimeStretch> cuts, double frameRate, string what = "parts")
    {
        if (index < 0 || index >= layer.Items.Count)
        {
            return EditResult.Fail("No item is selected.");
        }

        var item = layer.Items[index];
        if (item.Kind is not (ItemKind.Video or ItemKind.Audio))
        {
            return EditResult.Fail($"{item.Name} is not a video or sound clip.");
        }

        var frame = 1.0 / Math.Max(1, frameRate);
        double Snap(double t) => Math.Round(t / frame) * frame;

        var from = item.SourceIn;
        var to = item.SourceOut;
        var speed = item.EffectiveSpeed;
        var merged = Merge(cuts
            .Select(c => new TimeStretch(Math.Max(from, Snap(c.Start)), Math.Min(to, Snap(c.End))))
            .Where(c => c.Duration > frame / 2));
        if (merged.Count == 0)
        {
            return EditResult.Fail($"Nothing to remove from {item.Name}.");
        }

        // What stays: the stretches between the cuts.
        var kept = new List<TimeStretch>();
        var cursor = from;
        foreach (var cut in merged)
        {
            if (cut.Start - cursor >= MinimumPiece)
            {
                kept.Add(new TimeStretch(cursor, cut.Start));
            }

            cursor = Math.Max(cursor, cut.End);
        }

        if (to - cursor >= MinimumPiece)
        {
            kept.Add(new TimeStretch(cursor, to));
        }

        if (kept.Count == 0)
        {
            return EditResult.Fail($"That would remove all of {item.Name}. Delete the item instead.");
        }

        var removed = item.Duration - kept.Sum(k => k.Duration) / speed;
        var pieces = new List<Item>();
        for (var i = 0; i < kept.Count; i++)
        {
            var piece = item.Clone(newId: i > 0);
            piece.SourceIn = kept[i].Start;
            piece.Duration = kept[i].Duration / speed;
            piece.SoundJoinFade = JoinFade;
            if (i > 0)
            {
                piece.GapBefore = 0;
                piece.TransitionIn = new TransitionSpec();
                piece.FadeIn = 0;
            }

            if (i < kept.Count - 1)
            {
                piece.FadeOut = 0;
            }

            pieces.Add(piece);
        }

        layer.Items.RemoveAt(index);
        layer.Items.InsertRange(index, pieces);
        var count = merged.Count;
        return EditResult.Success(
            $"Removed {count} {(count == 1 ? Singular(what) : what)}, {TimeFormat.Words(removed)} in all, from {item.Name}. It is now {TimeFormat.Words(kept.Sum(k => k.Duration) / speed)} long, in {pieces.Count} {(pieces.Count == 1 ? "piece" : "pieces")}.",
            pieces[0].Id);
    }

    /// <summary>
    /// The cuts that shorten every pause longer than <paramref name="longerThan"/> seconds to
    /// <paramref name="keep"/> seconds (half kept on each side). Silence at the very start and end of the
    /// clip is trimmed to <paramref name="keep"/> whenever it is longer than that.
    /// </summary>
    public static IReadOnlyList<TimeStretch> PauseCuts(SpeechAnalysis analysis, double keep, double longerThan)
    {
        var cuts = new List<TimeStretch>();
        foreach (var pause in analysis.Pauses)
        {
            var atStart = pause.Start <= analysis.Start + 1e-6;
            var atEnd = pause.End >= analysis.End - 1e-6;

            // Silence at the very start or end goes whenever it is longer than what is kept.
            var threshold = atStart || atEnd ? keep : longerThan;
            if (pause.Duration <= threshold + 1e-9 || pause.Duration <= keep)
            {
                continue;
            }

            if (atStart && atEnd)
            {
                continue; // all silence: leave it for the user to decide
            }

            if (atStart)
            {
                cuts.Add(new TimeStretch(pause.Start, pause.End - keep));
            }
            else if (atEnd)
            {
                cuts.Add(new TimeStretch(pause.Start + keep, pause.End));
            }
            else
            {
                cuts.Add(new TimeStretch(pause.Start + keep / 2, pause.End - keep / 2));
            }
        }

        return cuts;
    }

    internal static List<TimeStretch> Merge(IEnumerable<TimeStretch> stretches)
    {
        var result = new List<TimeStretch>();
        foreach (var s in stretches.OrderBy(s => s.Start))
        {
            if (result.Count > 0 && s.Start <= result[^1].End + 1e-9)
            {
                result[^1] = new TimeStretch(result[^1].Start, Math.Max(result[^1].End, s.End));
            }
            else
            {
                result.Add(s);
            }
        }

        return result;
    }

    private static string Singular(string plural) => plural.EndsWith('s') ? plural[..^1] : plural;
}
