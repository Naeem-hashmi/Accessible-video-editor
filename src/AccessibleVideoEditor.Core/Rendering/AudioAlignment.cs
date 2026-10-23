using System.Globalization;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>
/// Keeps sound in step with picture for files whose sound does not start at the same moment as their
/// picture. FFmpeg reads each stream from its own first sample, so:
/// sound that starts later gets silence of the missing length put in front, and sound that starts earlier
/// has the part before the picture cut off. Reading from after both have started already lines up
/// (tested in AudioOffsetTests).
/// </summary>
public static class AudioAlignment
{
    /// <summary>
    /// The filter that lines sound up with picture when reading from <paramref name="from"/> seconds,
    /// or null when none is needed. <paramref name="audioOffset"/> is sound start minus picture start.
    /// </summary>
    public static string? Align(double audioOffset, double from)
    {
        from = Math.Max(0, from);
        var missing = audioOffset - from;
        if (missing > 0.001)
        {
            return $"adelay=delays={Seconds(missing * 1000)}:all=1";
        }

        var extra = -audioOffset - from;
        return extra > 0.001 ? $"atrim=start={Seconds(extra)},asetpts=PTS-STARTPTS" : null;
    }

    private static string Seconds(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
