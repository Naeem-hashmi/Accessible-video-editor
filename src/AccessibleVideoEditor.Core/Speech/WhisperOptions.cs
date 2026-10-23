using System.Diagnostics;

namespace AccessibleVideoEditor.Core.Speech;

/// <summary>How Whisper runs: on how many processor threads, how carefully, and on which device.</summary>
/// <param name="Threads">Processor threads; 0 chooses automatically.</param>
/// <param name="BeamSize">
/// Candidates tried for each sentence. 1 (greedy) is about three times faster with the Accurate model and
/// gave the same words in tests; 5 is "extra careful".
/// </param>
/// <param name="SkipSilence">Find speech first (voice activity detection) and only send speech to Whisper.</param>
/// <param name="UseGpu">Use the graphics chip when a GPU build and driver are present.</param>
public sealed record WhisperOptions(int Threads = 0, int BeamSize = 1, bool SkipSilence = true, bool UseGpu = true)
{
    public static WhisperOptions Default { get; } = new();

    /// <summary>
    /// The threads actually used. Automatic is half the logical processors (at least 2, at most 8): on
    /// laptops with fast and efficient cores, using every thread is often slower. The speed test can do better.
    /// </summary>
    public int EffectiveThreads => Threads > 0 ? Threads : Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public string Spoken =>
        $"{EffectiveThreads} threads, {(BeamSize > 1 ? "extra careful" : "normal care")}, " +
        $"{(SkipSilence ? "skipping silence" : "listening to everything")}, graphics chip {(UseGpu ? "on" : "off")}";
}

/// <summary>One timed run of the speed test.</summary>
public sealed record SpeedTestRun(WhisperOptions Options, double Seconds, string Device, int Words)
{
    /// <summary>How many seconds of speech one second of work handles.</summary>
    public double TimesRealTime(double audioSeconds) => Seconds > 0 ? audioSeconds / Seconds : 0;
}

/// <summary>The result of a speed test: every run, and the fastest.</summary>
public sealed record SpeedTestResult(IReadOnlyList<SpeedTestRun> Runs, double AudioSeconds)
{
    public SpeedTestRun Best => Runs.OrderBy(r => r.Seconds).First();

    /// <summary>A plain-language summary, with an estimate for a 25-minute video.</summary>
    public string Spoken
    {
        get
        {
            var best = Best;
            var speed = best.TimesRealTime(AudioSeconds);
            var minutes = speed > 0 ? 25 / speed : 0;
            return $"Fastest: {best.Options.EffectiveThreads} threads on {best.Device}: {AudioSeconds:0} seconds of sound in {best.Seconds:0.0} seconds, " +
                   $"{speed:0.0} times faster than real time. A 25-minute video would take about {Math.Max(1, Math.Round(minutes)):0} minutes " +
                   "(less when there is silence to skip).";
        }
    }
}

/// <summary>
/// Measures Whisper on this computer: transcribes the same sample with different thread counts (and with
/// and without the graphics chip) and keeps the fastest. Laptops differ a lot, so measuring beats guessing.
/// </summary>
public static class WhisperSpeedTest
{
    /// <summary>The thread counts worth trying on this computer.</summary>
    public static IReadOnlyList<int> Candidates(int processors)
    {
        var list = new SortedSet<int> { 2, 4, 6, 8, Math.Clamp(processors / 2, 2, 16), Math.Clamp(processors, 2, 16) };
        return list.Where(t => t <= Math.Max(2, processors)).ToList();
    }

    public static async Task<SpeedTestResult> RunAsync(WhisperRunner whisper, Rendering.FfmpegTools ffmpeg, WhisperModel model,
        string sample, double start, double seconds, string language, IReadOnlyList<int> threadCounts, bool tryGpu,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        var configurations = threadCounts.Select(t => new WhisperOptions(t, 1, SkipSilence: false, UseGpu: false)).ToList();
        if (tryGpu)
        {
            configurations.Add(new WhisperOptions(threadCounts.Max(), 1, SkipSilence: false, UseGpu: true));
        }

        var runs = new List<SpeedTestRun>();
        for (var i = 0; i < configurations.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var clock = Stopwatch.StartNew();
            var transcript = await whisper.TranscribeAsync(ffmpeg, model, sample, language, start, seconds, options: configurations[i], token: token)
                .ConfigureAwait(false);
            runs.Add(new SpeedTestRun(configurations[i], clock.Elapsed.TotalSeconds, whisper.LastDevice, transcript.Words.Count));
            progress?.Report((i + 1) / (double)configurations.Count);
        }

        // A GPU run that fell back to the processor is not a real GPU result.
        var useful = runs.Where(r => !(r.Options.UseGpu && r.Device == "processor")).ToList();
        return new SpeedTestResult(useful.Count > 0 ? useful : runs, seconds);
    }
}
