using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.Core.Audio;

/// <summary>The beats found in a piece of music.</summary>
/// <param name="Bpm">Tempo in beats per minute.</param>
/// <param name="Beats">Beat times in seconds from the start of the analysed sound.</param>
/// <param name="Confidence">0 to 1: how clearly the music has a steady beat.</param>
public sealed record BeatAnalysis(double Bpm, IReadOnlyList<double> Beats, double Confidence)
{
    /// <summary>Below this the music has no steady beat worth snapping to (ambient, speech, classical rubato).</summary>
    public const double MinimumConfidence = 0.25;

    public bool HasSteadyBeat => Confidence >= MinimumConfidence && Beats.Count >= 4;

    public string Spoken => HasSteadyBeat
        ? $"{Math.Round(Bpm):0} beats per minute, {Beats.Count} beats."
        : "No steady beat found in this sound.";
}

/// <summary>
/// Finds the tempo and beat times of music.
/// </summary>
/// <remarks>
/// 1. Onsets: the sound is cut into short overlapping windows; for each, the rise in loudness across
///    frequencies (spectral flux) shows where notes and drum hits start.
/// 2. Tempo: the onset curve is compared with itself shifted by every beat length from 50 to 220 beats per
///    minute (autocorrelation), favouring tempos near 120, and the best match wins.
/// 3. Beats: dynamic programming picks the chain of onsets that is both loud and evenly spaced at that
///    tempo (D. Ellis, "Beat tracking by dynamic programming", 2007).
/// </remarks>
public static class BeatDetector
{
    public const int SampleRate = 22050;
    private const int WindowSize = 2048;
    private const int Hop = 512;
    private const double FramesPerSecond = SampleRate / (double)Hop;

    /// <summary>Analyses a file (or part of it) with FFmpeg doing the decoding.</summary>
    public static BeatAnalysis Analyse(FfmpegTools tools, string path, double start = 0, double length = 600) =>
        Analyse(FfmpegStream.DecodeMono(tools, path, SampleRate, start, length));

    /// <summary>Analyses mono samples at <see cref="SampleRate"/>.</summary>
    public static BeatAnalysis Analyse(float[] samples)
    {
        var onset = OnsetStrength(samples);
        if (onset.Length < FramesPerSecond * 3)
        {
            return new BeatAnalysis(0, [], 0);
        }

        var (period, confidence) = EstimatePeriod(onset);
        if (period <= 0)
        {
            return new BeatAnalysis(0, [], 0);
        }

        var beats = TrackBeats(onset, period);
        var times = beats.Select(b => RefineOnset(samples, b / FramesPerSecond)).ToList();

        // Refine the tempo from the beats actually found.
        var bpm = 60.0 * FramesPerSecond / period;
        if (times.Count >= 4)
        {
            var intervals = times.Zip(times.Skip(1), (a, b) => b - a).OrderBy(d => d).ToList();
            var median = intervals[intervals.Count / 2];
            bpm = 60.0 / median;
            var fitted = FitGrid(times);
            if (fitted > 0)
            {
                bpm = 60.0 / fitted;
            }
        }

        // A real beat is made of hits. Slow swells (pads, drones) can look periodic but have no attack.
        var hits = times.Count(t => HasAttack(samples, t));
        var hitFraction = times.Count == 0 ? 0 : hits / (double)times.Count;
        confidence *= Math.Clamp(hitFraction / 0.6, 0, 1);
        return new BeatAnalysis(bpm, times, confidence);
    }

    /// <summary>True if the sound gets at least 4.5 dB louder within about 25 ms near <paramref name="time"/>.</summary>
    internal static bool HasAttack(float[] samples, double time)
    {
        // 23 ms windows: long enough that low notes (down to about 45 Hz) don't wobble the level by themselves.
        const int window = 512;
        const int step = 128;
        var from = Math.Max(0, (int)((time - 0.07) * SampleRate));
        var to = Math.Min(samples.Length - window, (int)((time + 0.04) * SampleRate));
        var energies = new List<double>();
        for (var start = from; start <= to; start += step)
        {
            double sum = 0;
            for (var i = start; i < start + window; i++)
            {
                sum += samples[i] * samples[i];
            }

            energies.Add(10 * Math.Log10(sum / window + 1e-10));
        }

        for (var i = 4; i < energies.Count; i++)
        {
            var before = Math.Min(Math.Min(energies[i - 4], energies[i - 3]), Math.Min(energies[i - 2], energies[i - 1]));
            if (energies[i] - before >= 4.5 && energies[i] > -60)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves a beat to the exact moment the sound rises (within 3 ms), since onset frames are 23 ms apart.
    /// </summary>
    internal static double RefineOnset(float[] samples, double time)
    {
        const int window = 128;
        const int step = 32;
        var from = (int)((time - 0.06) * SampleRate);
        var to = (int)((time + 0.04) * SampleRate);
        from = Math.Max(0, from);
        to = Math.Min(samples.Length - window, to);
        if (to - from < 4 * step)
        {
            return time;
        }

        var energies = new List<(int Start, double Db)>();
        for (var start = from; start <= to; start += step)
        {
            double sum = 0;
            for (var i = start; i < start + window; i++)
            {
                sum += samples[i] * samples[i];
            }

            energies.Add((start, 10 * Math.Log10(sum / window + 1e-10)));
        }

        // The steepest rise in loudness is where the hit starts.
        var best = -1;
        var bestRise = 0.0;
        for (var i = 1; i < energies.Count; i++)
        {
            var rise = energies[i].Db - energies[i - 1].Db;
            if (rise > bestRise)
            {
                bestRise = rise;
                best = i;
            }
        }

        // Only trust a clear rise (6 dB in 3 ms); otherwise keep the frame time.
        return best < 0 || bestRise < 6 ? time : (energies[best].Start + window * 0.75) / SampleRate;
    }

    /// <summary>Least-squares beat length from beat times (more exact than any single interval).</summary>
    private static double FitGrid(IReadOnlyList<double> times)
    {
        var n = times.Count;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var i = 0; i < n; i++)
        {
            sx += i;
            sy += times[i];
            sxx += (double)i * i;
            sxy += i * times[i];
        }

        var denominator = n * sxx - sx * sx;
        return denominator <= 0 ? 0 : (n * sxy - sx * sy) / denominator;
    }

    /// <summary>One value per hop: how strongly new sound starts there.</summary>
    internal static double[] OnsetStrength(float[] samples)
    {
        var frames = samples.Length / Hop + 1;
        var bins = WindowSize / 2;
        // Three bands, so kick and snare count more than hi-hats when deciding where the beat is.
        var lowTop = (int)(200.0 / SampleRate * WindowSize);
        var midTop = (int)(2500.0 / SampleRate * WindowSize);
        var maxBin = (int)(8000.0 / SampleRate * WindowSize);
        var window = new double[WindowSize];
        for (var i = 0; i < WindowSize; i++)
        {
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / WindowSize);
        }

        var re = new double[WindowSize];
        var im = new double[WindowSize];
        var previous = new double[bins];
        var current = new double[bins];
        var flux = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            // Windows are centred on f * Hop, so onset frame f means time f * Hop / SampleRate.
            var start = f * Hop - WindowSize / 2;
            for (var i = 0; i < WindowSize; i++)
            {
                var s = start + i;
                re[i] = s >= 0 && s < samples.Length ? samples[s] * window[i] : 0;
                im[i] = 0;
            }

            Fft(re, im);
            double low = 0, mid = 0, high = 0;
            for (var k = 1; k < maxBin; k++)
            {
                current[k] = Math.Log(1 + 100 * Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
                if (f > 0)
                {
                    var rise = Math.Max(0, current[k] - previous[k]);
                    if (k < lowTop)
                    {
                        low += rise;
                    }
                    else if (k < midTop)
                    {
                        mid += rise;
                    }
                    else
                    {
                        high += rise;
                    }
                }
            }

            flux[f] = 0.5 * low / (lowTop - 1) + 0.3 * mid / (midTop - lowTop) + 0.2 * high / (maxBin - midTop);
            (previous, current) = (current, previous);
        }

        // Remove slow changes in overall loudness, keep only the rises.
        var smoothed = MovingAverage(flux, 16);
        var onset = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            onset[f] = Math.Max(0, flux[f] - smoothed[f]);
        }

        // A little smoothing so a beat between two frames still lines up with itself.
        onset = Smooth(onset, 1.0);
        var std = Math.Sqrt(onset.Select(v => v * v).Average());
        if (std > 0)
        {
            for (var f = 0; f < frames; f++)
            {
                onset[f] /= std;
            }
        }

        return onset;
    }

    /// <summary>Beat length in onset frames, and how clear it is.</summary>
    internal static (double Period, double Confidence) EstimatePeriod(double[] onset)
    {
        var maxLag = (int)Math.Ceiling(FramesPerSecond * 60 / 50);
        var n = onset.Length;
        if (n <= maxLag * 2)
        {
            maxLag = n / 2;
        }

        var mean = onset.Average();
        var centred = onset.Select(v => v - mean).ToArray();
        double zero = centred.Sum(v => v * v);
        if (zero <= 0)
        {
            return (0, 0);
        }

        var ac = new double[Math.Min(n - 1, 4 * maxLag + 2)];
        for (var lag = 1; lag < ac.Length; lag++)
        {
            double sum = 0;
            for (var i = lag; i < n; i++)
            {
                sum += centred[i] * centred[i - lag];
            }

            ac[lag] = sum / zero * n / (n - lag);
        }

        double Ac(double lag)
        {
            var i = (int)Math.Floor(lag);
            if (i < 1 || i + 1 >= ac.Length)
            {
                return 0;
            }

            var f = lag - i;
            return ac[i] * (1 - f) + ac[i + 1] * f;
        }

        // Score every tempo from 50 to 220 BPM in small steps: the beat itself and its multiples (a comb)
        // must all line up, and tempos near 120 are preferred (a log-normal weight one octave wide).
        var bestPeriod = 0.0;
        var bestScore = double.NegativeInfinity;
        for (var bpm = 50.0; bpm <= 220.0; bpm += 0.25)
        {
            var lag = 60 * FramesPerSecond / bpm;
            double comb = 0, norm = 0;
            for (var m = 1; m <= 4 && m * lag < ac.Length - 1; m++)
            {
                comb += Ac(m * lag) / m;
                norm += 1.0 / m;
            }

            var weight = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120.0), 2));
            var score = weight * comb / norm;
            if (score > bestScore)
            {
                bestScore = score;
                bestPeriod = lag;
            }
        }

        if (bestPeriod <= 0)
        {
            return (0, 0);
        }

        var period = bestPeriod;
        var confidence = Math.Clamp(Ac(period), 0, 1);
        return (period, confidence);
    }

    /// <summary>Ellis dynamic-programming beat tracker. Returns beat positions in onset frames.</summary>
    internal static List<int> TrackBeats(double[] onset, double period)
    {
        var n = onset.Length;
        // Smooth the onsets with a Gaussian a fraction of a beat wide.
        var sigma = period / 32;
        var radius = (int)Math.Ceiling(4 * sigma);
        var local = new double[n];
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var j = i + k;
                if (j >= 0 && j < n)
                {
                    sum += onset[j] * Math.Exp(-0.5 * k * k / (sigma * sigma));
                }
            }

            local[i] = sum;
        }

        const double tightness = 100;
        var score = new double[n];
        var back = new int[n];
        var from = (int)Math.Round(period / 2);
        var to = (int)Math.Round(period * 2);
        for (var i = 0; i < n; i++)
        {
            var best = 0.0;
            var bestPrev = -1;
            for (var prev = i - to; prev <= i - from; prev++)
            {
                if (prev < 0)
                {
                    continue;
                }

                var penalty = Math.Log((i - prev) / period);
                var candidate = score[prev] - tightness * penalty * penalty;
                if (bestPrev < 0 || candidate > best)
                {
                    best = candidate;
                    bestPrev = prev;
                }
            }

            score[i] = local[i] + (bestPrev >= 0 ? Math.Max(0, best) : 0);
            back[i] = bestPrev >= 0 && best > 0 ? bestPrev : -1;
        }

        // End on the best-scoring beat in the last beat period.
        var lastFrom = Math.Max(0, n - (int)Math.Ceiling(period));
        var last = lastFrom;
        for (var i = lastFrom; i < n; i++)
        {
            if (score[i] > score[last])
            {
                last = i;
            }
        }

        var beats = new List<int>();
        for (var b = last; b >= 0; b = back[b])
        {
            beats.Add(b);
        }

        beats.Reverse();

        // Drop weak beats at the very start and end (silence or fade).
        var threshold = 0.5 * Math.Sqrt(beats.Select(b => local[b] * local[b]).DefaultIfEmpty(0).Average());
        while (beats.Count > 0 && local[beats[0]] < threshold)
        {
            beats.RemoveAt(0);
        }

        while (beats.Count > 0 && local[beats[^1]] < threshold)
        {
            beats.RemoveAt(beats.Count - 1);
        }

        return beats;
    }

    private static double[] Smooth(double[] values, double sigma)
    {
        var radius = (int)Math.Ceiling(3 * sigma);
        var kernel = Enumerable.Range(-radius, 2 * radius + 1).Select(k => Math.Exp(-0.5 * k * k / (sigma * sigma))).ToArray();
        var total = kernel.Sum();
        var result = new double[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            double sum = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var j = i + k;
                if (j >= 0 && j < values.Length)
                {
                    sum += values[j] * kernel[k + radius];
                }
            }

            result[i] = sum / total;
        }

        return result;
    }

    private static double[] MovingAverage(double[] values, int radius)
    {
        var result = new double[values.Length];
        double sum = 0;
        var count = 0;
        var lo = 0;
        var hi = -1;
        for (var i = 0; i < values.Length; i++)
        {
            while (hi < Math.Min(values.Length - 1, i + radius))
            {
                sum += values[++hi];
                count++;
            }

            while (lo < i - radius)
            {
                sum -= values[lo++];
                count--;
            }

            result[i] = sum / count;
        }

        return result;
    }

    /// <summary>In-place radix-2 FFT; the length must be a power of two.</summary>
    internal static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wr = Math.Cos(angle);
            var wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    var next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}
