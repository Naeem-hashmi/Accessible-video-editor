using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Speech;

/// <summary>
/// Which parts of a file need words. A 25-minute recording cut down to 4 minutes only needs those 4 minutes
/// transcribed, and a transcript that already covers a part does not need it again. All times are file time.
/// </summary>
public static class TranscriptCoverage
{
    /// <summary>Extra seconds kept around each used part, so a clip made a little longer still has words.</summary>
    public const double Margin = 1.0;

    /// <summary>Used parts closer together than this are transcribed as one (starting Whisper costs time too).</summary>
    public const double JoinGap = 4.0;

    /// <summary>Seconds of sound Whisper hears before and after a part, so words at its edges come out whole.</summary>
    public const double Context = 1.0;

    /// <summary>The parts of <paramref name="mediaPath"/> that clips with sound play, on any layer, with a margin.</summary>
    public static IReadOnlyList<TimeStretch> Used(Project project, string mediaPath)
    {
        var parts = new List<TimeStretch>();
        var length = 0.0;
        foreach (var item in project.Layers.SelectMany(l => l.Items))
        {
            if (!item.HasAudio || item.Kind is not (ItemKind.Video or ItemKind.Audio) ||
                !string.Equals(item.Path, mediaPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            length = Math.Max(length, item.SourceDuration);
            parts.Add(new TimeStretch(item.SourceIn - Margin, item.SourceOut + Margin));
        }

        return Join(parts, JoinGap, length);
    }

    /// <summary>The whole file, for when everything should be transcribed.</summary>
    public static IReadOnlyList<TimeStretch> Whole(double length) => [new TimeStretch(0, length > 0 ? length : double.MaxValue)];

    /// <summary>The parts of <paramref name="wanted"/> that <paramref name="existing"/> has no words for yet.</summary>
    public static IReadOnlyList<TimeStretch> Missing(Transcript? existing, IReadOnlyList<TimeStretch> wanted)
    {
        if (existing is null)
        {
            return wanted;
        }

        if (existing.Covered.Count == 0)
        {
            return []; // Transcribed in full (and projects from before parts were remembered).
        }

        var missing = new List<TimeStretch>();
        foreach (var part in wanted)
        {
            var from = part.Start;
            foreach (var done in existing.Covered.Where(c => c.End > part.Start && c.Start < part.End).OrderBy(c => c.Start))
            {
                if (done.Start > from)
                {
                    missing.Add(new TimeStretch(from, done.Start));
                }

                from = Math.Max(from, done.End);
            }

            if (from < part.End)
            {
                missing.Add(new TimeStretch(from, part.End));
            }
        }

        // Slivers (under a quarter second) are not worth starting Whisper for.
        return missing.Where(m => m.Duration >= 0.25).ToList();
    }

    /// <summary>Seconds that will be transcribed.</summary>
    public static double Seconds(IEnumerable<TimeStretch> parts) => parts.Sum(p => p.Duration);

    /// <summary>
    /// Puts the words Whisper heard for <paramref name="part"/> into <paramref name="target"/>: words already
    /// there in that part are replaced, words outside it (heard only as context) are left out.
    /// </summary>
    public static void Merge(Transcript target, Transcript heard, TimeStretch part)
    {
        static bool Inside(TranscriptWord w, TimeStretch p) => (w.Start + w.End) / 2 >= p.Start && (w.Start + w.End) / 2 < p.End;

        target.Words.RemoveAll(w => Inside(w, part));
        target.Words.AddRange(heard.Words.Where(w => Inside(w, part)));
        target.Words.Sort((a, b) => a.Start.CompareTo(b.Start));
        if (target.Language.Length == 0)
        {
            target.Language = heard.Language;
        }

        target.Model = heard.Model;
        target.Covered = Join([.. target.Covered, part], 0.01, 0).ToList();
    }

    /// <summary>Sorts and joins stretches that overlap or are closer than <paramref name="gap"/>; clamps to the file.</summary>
    public static IReadOnlyList<TimeStretch> Join(IEnumerable<TimeStretch> parts, double gap, double length)
    {
        var joined = new List<TimeStretch>();
        foreach (var p in parts.OrderBy(p => p.Start))
        {
            var start = Math.Max(0, p.Start);
            var end = length > 0 ? Math.Min(length, p.End) : p.End;
            if (end <= start)
            {
                continue;
            }

            if (joined.Count > 0 && start - joined[^1].End < gap)
            {
                joined[^1] = joined[^1] with { End = Math.Max(joined[^1].End, end) };
            }
            else
            {
                joined.Add(new TimeStretch(start, end));
            }
        }

        return joined;
    }
}
