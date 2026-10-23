using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.Core.Audio;

/// <summary>A stretch of time in seconds.</summary>
public readonly record struct TimeStretch(double Start, double End)
{
    public double Duration => End - Start;
}

/// <summary>Where a recording has sound (speech) and where it pauses, in seconds from the analysed start.</summary>
public sealed record SpeechAnalysis(
    IReadOnlyList<TimeStretch> Sound,
    IReadOnlyList<TimeStretch> Pauses,
    double Length,
    double ThresholdDb,
    double NoiseFloorDb)
{
    /// <summary>A noisy recording: the quiet parts are within 25 dB of the loud parts.</summary>
    public bool IsNoisy { get; init; }

    /// <summary>The same analysis moved by <paramref name="offset"/> seconds (for example into file time).</summary>
    public SpeechAnalysis Shift(double offset) => this with
    {
        Sound = Sound.Select(s => new TimeStretch(s.Start + offset, s.End + offset)).ToList(),
        Pauses = Pauses.Select(s => new TimeStretch(s.Start + offset, s.End + offset)).ToList(),
    };
}

/// <summary>
/// Finds pauses in speech by loudness. The sound is measured in 20 ms steps; the threshold adapts to each
/// recording: 10 dB above its background noise, and never more than 35 dB below its loud parts. Sound is
/// extended by 80 ms on each side so soft word endings and breaths into words are kept.
/// </summary>
public static class SpeechDetector
{
    public const int SampleRate = 16000;
    private const double Step = 0.02;
    private const double Hangover = 0.08;

    public static SpeechAnalysis Analyse(FfmpegTools tools, string path, double start, double length, double minimumPause = 0.5, double? thresholdDb = null) =>
        Analyse(FfmpegStream.DecodeMono(tools, path, SampleRate, start, length), minimumPause, thresholdDb);

    public static SpeechAnalysis Analyse(float[] samples, double minimumPause = 0.5, double? thresholdDb = null)
    {
        var frameLength = (int)(Step * SampleRate);
        var frames = samples.Length / frameLength;
        var length = samples.Length / (double)SampleRate;
        if (frames == 0)
        {
            return new SpeechAnalysis([], [], length, 0, 0);
        }

        var db = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var i = f * frameLength; i < (f + 1) * frameLength; i++)
            {
                sum += samples[i] * samples[i];
            }

            db[f] = 10 * Math.Log10(sum / frameLength + 1e-12);
        }

        var sorted = db.OrderBy(v => v).ToArray();
        var floor = sorted[(int)(0.1 * (frames - 1))];
        var loud = sorted[(int)(0.95 * (frames - 1))];
        var threshold = thresholdDb ?? Math.Max(floor + 10, loud - 35);
        if (loud < -60)
        {
            // Nothing but near-silence.
            threshold = Math.Max(threshold, loud + 1);
        }

        // Mark loud frames, then widen them by the hangover.
        var loudFrame = db.Select(v => v > threshold).ToArray();
        var widen = (int)Math.Round(Hangover / Step);
        var sound = new bool[frames];
        for (var f = 0; f < frames; f++)
        {
            if (!loudFrame[f])
            {
                continue;
            }

            for (var k = Math.Max(0, f - widen); k <= Math.Min(frames - 1, f + widen); k++)
            {
                sound[k] = true;
            }
        }

        // Runs of quiet frames at least `minimumPause` long are pauses; the rest is sound.
        var pauses = new List<TimeStretch>();
        var f0 = 0;
        while (f0 < frames)
        {
            if (sound[f0])
            {
                f0++;
                continue;
            }

            var f1 = f0;
            while (f1 < frames && !sound[f1])
            {
                f1++;
            }

            var startTime = f0 * Step;
            var endTime = f1 == frames ? length : f1 * Step;
            if (endTime - startTime >= minimumPause - 1e-9)
            {
                pauses.Add(new TimeStretch(startTime, endTime));
            }

            f0 = f1;
        }

        var stretches = new List<TimeStretch>();
        var cursor = 0.0;
        foreach (var pause in pauses)
        {
            if (pause.Start > cursor + 1e-9)
            {
                stretches.Add(new TimeStretch(cursor, pause.Start));
            }

            cursor = pause.End;
        }

        if (cursor < length - 1e-9)
        {
            stretches.Add(new TimeStretch(cursor, length));
        }

        return new SpeechAnalysis(stretches, pauses, length, threshold, floor) { IsNoisy = loud - floor < 25 };
    }
}
