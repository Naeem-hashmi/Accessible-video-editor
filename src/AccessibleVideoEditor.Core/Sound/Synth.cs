namespace AccessibleVideoEditor.Core.Sound;

public enum Wave
{
    Sine,
    Triangle,
    Square,
    Saw,
}

/// <summary>
/// A tiny synthesizer. All built-in sounds are generated from maths, so they are free of any copyright
/// and need no sound files. Buffers are mono, 44.1 kHz, values between -1 and 1.
/// </summary>
public static class Synth
{
    public const int SampleRate = 44100;

    public static int Length(double seconds) => Math.Max(1, (int)Math.Round(seconds * SampleRate));

    public static float[] Silence(double seconds) => new float[Length(seconds)];

    /// <summary>A tone gliding from <paramref name="from"/> to <paramref name="to"/> hertz.</summary>
    public static float[] Tone(double from, double seconds, Wave wave = Wave.Sine, double gain = 0.5,
        double attack = 0.005, double release = 0.05, double? to = null)
    {
        var n = Length(seconds);
        var buffer = new float[n];
        var end = to ?? from;
        double phase = 0;
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / n;
            var freq = from * Math.Pow(end / from, t);
            phase += freq / SampleRate;
            phase -= Math.Floor(phase);
            buffer[i] = (float)(Oscillator(wave, phase) * gain);
        }

        return Envelope(buffer, attack, release);
    }

    /// <summary>A bell or chime: several partials, each dying away.</summary>
    public static float[] Bell(double frequency, double seconds, double gain = 0.4, double brightness = 1)
    {
        var n = Length(seconds);
        var buffer = new float[n];
        (double Ratio, double Level, double Decay)[] partials =
        [
            (1.0, 1.0, 1.0), (2.0, 0.5 * brightness, 1.6), (2.76, 0.35 * brightness, 2.2),
            (5.4, 0.2 * brightness, 3.5), (8.93, 0.1 * brightness, 5),
        ];
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / SampleRate;
            double sample = 0;
            foreach (var (ratio, level, decay) in partials)
            {
                sample += Math.Sin(2 * Math.PI * frequency * ratio * t) * level * Math.Exp(-t * decay * 4 / seconds);
            }

            buffer[i] = (float)(sample * gain / 2);
        }

        return Envelope(buffer, 0.002, 0.02);
    }

    /// <summary>White noise from a fixed seed, so the sound is the same every time.</summary>
    public static float[] Noise(double seconds, double gain = 0.5, int seed = 1)
    {
        var random = new Random(seed);
        var buffer = new float[Length(seconds)];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)((random.NextDouble() * 2 - 1) * gain);
        }

        return buffer;
    }

    /// <summary>
    /// Band-pass filter whose centre glides from <paramref name="from"/> to <paramref name="to"/> hertz
    /// (a state-variable filter). Used for whooshes.
    /// </summary>
    public static float[] BandSweep(float[] input, double from, double to, double q = 2)
    {
        var output = new float[input.Length];
        double low = 0, band = 0;
        var damping = 1 / q;
        for (var i = 0; i < input.Length; i++)
        {
            var t = (double)i / input.Length;
            var cutoff = from * Math.Pow(to / from, t);
            var f = 2 * Math.Sin(Math.PI * Math.Min(cutoff, SampleRate / 6.0) / SampleRate);
            var high = input[i] - low - damping * band;
            band += f * high;
            low += f * band;
            output[i] = (float)band;
        }

        return output;
    }

    /// <summary>Simple one-pole low-pass filter.</summary>
    public static float[] LowPass(float[] input, double cutoff)
    {
        var output = new float[input.Length];
        var a = Math.Exp(-2 * Math.PI * cutoff / SampleRate);
        double y = 0;
        for (var i = 0; i < input.Length; i++)
        {
            y = (1 - a) * input[i] + a * y;
            output[i] = (float)y;
        }

        return output;
    }

    /// <summary>Fades the start and end to avoid clicks; <paramref name="shape"/> above 1 makes a sharper decay.</summary>
    public static float[] Envelope(float[] buffer, double attack, double release, double shape = 1)
    {
        var a = Math.Min(buffer.Length / 2, Length(attack));
        var r = Math.Min(buffer.Length / 2, Length(release));
        for (var i = 0; i < a; i++)
        {
            buffer[i] *= (float)((double)i / a);
        }

        for (var i = 0; i < r; i++)
        {
            buffer[buffer.Length - 1 - i] *= (float)Math.Pow((double)i / r, shape);
        }

        return buffer;
    }

    /// <summary>Applies an exponential decay over the whole buffer.</summary>
    public static float[] Decay(float[] buffer, double speed)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= (float)Math.Exp(-speed * i / buffer.Length);
        }

        return buffer;
    }

    public static float[] Gain(float[] buffer, double gain)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= (float)gain;
        }

        return buffer;
    }

    /// <summary>Plays buffers one after another.</summary>
    public static float[] Sequence(params float[][] parts)
    {
        var output = new float[parts.Sum(p => p.Length)];
        var at = 0;
        foreach (var part in parts)
        {
            part.CopyTo(output, at);
            at += part.Length;
        }

        return output;
    }

    /// <summary>Mixes buffers, each starting at its own time offset.</summary>
    public static float[] Mix(params (float[] Buffer, double Offset)[] parts)
    {
        var length = parts.Max(p => Length(p.Offset) + p.Buffer.Length);
        var output = new float[length];
        foreach (var (buffer, offset) in parts)
        {
            var start = offset <= 0 ? 0 : Length(offset);
            for (var i = 0; i < buffer.Length; i++)
            {
                output[start + i] += buffer[i];
            }
        }

        return output;
    }

    /// <summary>Scales so the loudest sample reaches <paramref name="peak"/>.</summary>
    public static float[] Normalize(float[] buffer, double peak = 0.8)
    {
        var max = buffer.Length == 0 ? 0 : buffer.Max(Math.Abs);
        return max < 1e-6 ? buffer : Gain(buffer, peak / max);
    }

    /// <summary>Repeats a buffer to fill <paramref name="seconds"/>.</summary>
    public static float[] Loop(float[] buffer, double seconds)
    {
        var output = new float[Length(seconds)];
        for (var i = 0; i < output.Length; i++)
        {
            output[i] = buffer[i % buffer.Length];
        }

        return output;
    }

    private static double Oscillator(Wave wave, double phase) => wave switch
    {
        Wave.Triangle => 4 * Math.Abs(phase - 0.5) - 1,
        Wave.Square => phase < 0.5 ? 0.7 : -0.7,
        Wave.Saw => (2 * phase - 1) * 0.7,
        _ => Math.Sin(2 * Math.PI * phase),
    };
}

/// <summary>Writes 16-bit PCM WAV files.</summary>
public static class WavWriter
{
    public static byte[] ToWav(float[] mono, bool stereo = true, int sampleRate = Synth.SampleRate)
    {
        var channels = stereo ? 2 : 1;
        var dataBytes = mono.Length * channels * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in mono)
        {
            var value = (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
            for (var c = 0; c < channels; c++)
            {
                writer.Write(value);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static void Write(string path, float[] mono, bool stereo = true)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, ToWav(mono, stereo));
    }
}
