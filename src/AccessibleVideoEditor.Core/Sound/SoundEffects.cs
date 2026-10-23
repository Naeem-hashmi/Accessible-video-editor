namespace AccessibleVideoEditor.Core.Sound;

/// <summary>A sound effect or music bed that can be added to a video.</summary>
public sealed record SoundEffect(string FileName, string Name, string Category, string Description, Func<float[]> Generate);

/// <summary>
/// The built-in sound library. Every sound is synthesized here, so it is 100 percent original and free to
/// use in any video, including commercial ones. Users add their own audio files to the user library folder.
/// </summary>
public static class SoundEffects
{
    /// <summary>
    /// Generated sounds that now have real recorded versions (see <see cref="RecordedSounds"/>). They still ship so
    /// older projects play, but the sound lists offer the recordings instead.
    /// </summary>
    public static IReadOnlySet<string> Replaced { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Whoosh.wav", "Swoosh down.wav", "Pop.wav", "Click.wav", "Notification.wav", "Ding.wav", "Success chime.wav",
        "Error buzz.wav", "Typing.wav", "Tick tock.wav",
    };

    /// <summary>The generated sounds still offered: the ones with no real recording to replace them.</summary>
    public static IEnumerable<SoundEffect> Offered => BuiltIn.Where(e => !Replaced.Contains(e.FileName));

    public static IReadOnlyList<SoundEffect> BuiltIn { get; } = new SoundEffect[]
    {
        new("Whoosh.wav", "Whoosh", "Transitions", "Air rushing past. Use on a cut or slide.", Whoosh),
        new("Swoosh down.wav", "Swoosh down", "Transitions", "A falling swoosh.", () => Synth.Normalize(
            Synth.Envelope(Synth.BandSweep(Synth.Noise(0.6, 1, 2), 4000, 300, 3), 0.05, 0.3, 2))),
        new("Riser.wav", "Riser", "Transitions", "Builds tension for 2 seconds before a reveal.", Riser),
        new("Impact.wav", "Impact boom", "Transitions", "A deep hit. Pairs well after a riser.", Impact),
        new("Pop.wav", "Pop", "Interface", "A short bubble pop for text or icons appearing.", () => Synth.Normalize(
            Synth.Tone(900, 0.09, Wave.Sine, 0.8, 0.001, 0.03, 200))),
        new("Click.wav", "Click", "Interface", "A soft mouse-style click.", () => Synth.Normalize(
            Synth.Envelope(Synth.LowPass(Synth.Noise(0.025, 1, 3), 3000), 0.0005, 0.02, 3), 0.6)),
        new("Notification.wav", "Notification", "Interface", "Two quick rising tones.", () => Synth.Normalize(
            Synth.Sequence(Synth.Tone(880, 0.09, Wave.Sine, 0.6), Synth.Silence(0.03), Synth.Tone(1320, 0.14, Wave.Sine, 0.6)))),
        new("Ding.wav", "Ding", "Interface", "A clear bell.", () => Synth.Normalize(Synth.Bell(1046, 1.4))),
        new("Success chime.wav", "Success chime", "Interface", "A happy three-note chime.", SuccessChime),
        new("Error buzz.wav", "Error buzz", "Interface", "A low buzzer for mistakes or wrong answers.", () => Synth.Normalize(
            Synth.Sequence(Synth.Tone(150, 0.18, Wave.Square, 0.5), Synth.Silence(0.05), Synth.Tone(120, 0.3, Wave.Square, 0.5)), 0.6)),
        new("Sparkle.wav", "Magic sparkle", "Fun", "Twinkling bells for something special.", Sparkle),
        new("Camera shutter.wav", "Camera shutter", "Fun", "A photo being taken.", CameraShutter),
        new("Typing.wav", "Typing", "Fun", "Two seconds of keyboard typing.", Typing),
        new("Tick tock.wav", "Tick tock", "Fun", "A clock ticking for 4 seconds.", TickTock),
        new("Heartbeat.wav", "Heartbeat", "Fun", "Two heartbeats.", Heartbeat),
        new("Calm pad.wav", "Calm background pad", "Music beds", "30 seconds of soft, slowly moving chords. Loop or extend under speech.", CalmPad),
        new("Upbeat pulse.wav", "Upbeat pulse", "Music beds", "30 seconds of light rhythmic pulse at 120 beats per minute.", UpbeatPulse),
        new("Intro sting.wav", "Intro sting", "Music beds", "A 3-second rising chord for channel intros.", IntroSting),
    };

    /// <summary>Writes every built-in sound to <paramref name="folder"/>, skipping files that already exist.</summary>
    public static void WriteAll(string folder, bool overwrite = false)
    {
        Directory.CreateDirectory(folder);
        foreach (var effect in BuiltIn)
        {
            var path = Path.Combine(folder, effect.FileName);
            if (overwrite || !File.Exists(path))
            {
                WavWriter.Write(path, effect.Generate());
            }
        }
    }

    private static float[] Whoosh()
    {
        var noise = Synth.Noise(0.9, 1, 1);
        var up = Synth.BandSweep(noise, 300, 3500, 2.5);
        return Synth.Normalize(Synth.Envelope(up, 0.35, 0.45, 2));
    }

    private static float[] Riser()
    {
        var noise = Synth.Envelope(Synth.BandSweep(Synth.Noise(2, 0.8, 4), 200, 6000, 2), 1.5, 0.05);
        var tone = Synth.Tone(110, 2, Wave.Saw, 0.25, 1.5, 0.05, 880);
        return Synth.Normalize(Synth.Mix((noise, 0), (Synth.LowPass(tone, 2500), 0)));
    }

    private static float[] Impact()
    {
        var thump = Synth.Decay(Synth.Tone(90, 1.4, Wave.Sine, 1, 0.002, 0.2, 35), 4);
        var crack = Synth.Decay(Synth.LowPass(Synth.Noise(0.4, 0.6, 5), 1200), 8);
        return Synth.Normalize(Synth.Mix((thump, 0), (crack, 0)), 0.9);
    }

    private static float[] SuccessChime() => Synth.Normalize(Synth.Mix(
        (Synth.Bell(784, 1.0, 0.4), 0), (Synth.Bell(988, 1.0, 0.4), 0.12), (Synth.Bell(1175, 1.4, 0.45), 0.24)));

    private static float[] Sparkle()
    {
        var random = new Random(7);
        var parts = new List<(float[], double)>();
        for (var i = 0; i < 14; i++)
        {
            parts.Add((Synth.Bell(1500 + random.Next(0, 2500), 0.5, 0.2, 0.6), i * 0.07));
        }

        return Synth.Normalize(Synth.Mix(parts.ToArray()), 0.7);
    }

    private static float[] CameraShutter()
    {
        float[] Snap(int seed) => Synth.Envelope(Synth.BandSweep(Synth.Noise(0.05, 1, seed), 2500, 1200, 1.5), 0.001, 0.04, 2);
        return Synth.Normalize(Synth.Mix((Snap(8), 0), (Snap(9), 0.09)));
    }

    private static float[] Typing()
    {
        var random = new Random(11);
        var parts = new List<(float[], double)>();
        var t = 0.0;
        var seed = 20;
        while (t < 1.9)
        {
            var key = Synth.Envelope(Synth.BandSweep(Synth.Noise(0.03, 1, seed++), 3000, 1500, 2), 0.0005, 0.025, 3);
            parts.Add((Synth.Gain(key, 0.6 + random.NextDouble() * 0.4), t));
            t += 0.07 + random.NextDouble() * 0.12;
        }

        return Synth.Normalize(Synth.Mix(parts.ToArray()), 0.7);
    }

    private static float[] TickTock()
    {
        var parts = new List<(float[], double)>();
        for (var i = 0; i < 8; i++)
        {
            var pitch = i % 2 == 0 ? 2400 : 1800;
            parts.Add((Synth.Decay(Synth.Tone(pitch, 0.04, Wave.Sine, 0.8, 0.0005, 0.01), 6), i * 0.5));
        }

        return Synth.Normalize(Synth.Mix(parts.ToArray()), 0.6);
    }

    private static float[] Heartbeat()
    {
        float[] Beat(double gain) => Synth.Decay(Synth.Tone(60, 0.18, Wave.Sine, gain, 0.005, 0.05, 40), 5);
        return Synth.Normalize(Synth.Mix((Beat(1), 0), (Beat(0.7), 0.25), (Beat(1), 1.0), (Beat(0.7), 1.25), (Synth.Silence(1.8), 0)), 0.9);
    }

    private static float[] CalmPad()
    {
        // C major 7, A minor 7, F major 7, G6: four bars of 7.5 seconds.
        double[][] chords =
        [
            [130.81, 164.81, 196.0, 246.94], [110.0, 130.81, 164.81, 196.0],
            [87.31, 130.81, 174.61, 220.0], [98.0, 146.83, 196.0, 246.94],
        ];
        var parts = new List<(float[], double)>();
        for (var c = 0; c < chords.Length; c++)
        {
            foreach (var f in chords[c])
            {
                var note = Synth.Tone(f, 8.5, Wave.Triangle, 0.12, 1.8, 2.2);
                parts.Add((Synth.LowPass(note, 1200), c * 7.5));
                parts.Add((Synth.Tone(f * 2.003, 8.5, Wave.Sine, 0.04, 2, 2.2), c * 7.5));
            }
        }

        var pad = Synth.Mix(parts.ToArray());
        Array.Resize(ref pad, Synth.Length(30));
        return Synth.Normalize(Synth.Envelope(pad, 1.5, 2.5), 0.6);
    }

    private static float[] UpbeatPulse()
    {
        const double beat = 0.5;
        double[] bass = [65.41, 65.41, 55.0, 55.0, 43.65, 43.65, 49.0, 49.0];
        double[][] chords =
        [
            [261.63, 329.63, 392.0], [261.63, 329.63, 392.0], [220.0, 261.63, 329.63], [220.0, 261.63, 329.63],
            [174.61, 220.0, 261.63], [174.61, 220.0, 261.63], [196.0, 246.94, 293.66], [196.0, 246.94, 293.66],
        ];
        var parts = new List<(float[], double)>();
        var beats = (int)(30 / beat);
        for (var i = 0; i < beats; i++)
        {
            var t = i * beat;
            var bar = i / 4 % bass.Length;
            parts.Add((Synth.Decay(Synth.Tone(55, 0.25, Wave.Sine, 0.9, 0.002, 0.05, 38), 5), t));
            parts.Add((Synth.Tone(bass[bar], beat * 0.9, Wave.Triangle, 0.35, 0.01, 0.1), t));
            if (i % 2 == 1)
            {
                parts.Add((Synth.Envelope(Synth.BandSweep(Synth.Noise(0.06, 0.5, 30 + i), 7000, 9000, 2), 0.001, 0.05, 2), t));
            }

            foreach (var f in chords[bar])
            {
                parts.Add((Synth.Tone(f, 0.18, Wave.Square, 0.05, 0.005, 0.08), t + beat / 2));
            }
        }

        var music = Synth.Mix(parts.ToArray());
        Array.Resize(ref music, Synth.Length(30));
        return Synth.Normalize(Synth.Envelope(music, 0.05, 1.5), 0.7);
    }

    private static float[] IntroSting()
    {
        var parts = new List<(float[], double)>();
        double[] notes = [261.63, 329.63, 392.0, 523.25];
        for (var i = 0; i < notes.Length; i++)
        {
            parts.Add((Synth.Tone(notes[i], 3 - i * 0.15, Wave.Saw, 0.15, 0.02, 1.2), i * 0.15));
            parts.Add((Synth.Bell(notes[i] * 2, 2.5 - i * 0.15, 0.25), i * 0.15));
        }

        parts.Add((Impact(), 0.6));
        var sting = Synth.Mix(parts.ToArray());
        return Synth.Normalize(Synth.Envelope(Synth.LowPass(sting, 5000), 0.01, 0.8), 0.85);
    }
}
