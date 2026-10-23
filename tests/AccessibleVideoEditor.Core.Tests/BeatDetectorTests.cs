using System.Globalization;
using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

[Collection(MediaCollection.Name)]
public sealed class BeatDetectorTests(MediaFixture media)
{
    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>A drum pattern: kick on every beat, hi-hat on the off-beats, snare on 2 and 4, plus noise.</summary>
    private Task<string> Drums(double bpm, double offset, double seconds, string name)
    {
        var p = N(60 / bpm);
        var o = N(offset);
        var kick = $"if(gte(t,{o}),0.9*sin(2*PI*(50+90*exp(-30*mod(t-{o},{p})))*mod(t-{o},{p}))*exp(-14*mod(t-{o},{p})),0)";
        var hat = $"if(gte(t,{o}),0.25*(2*random(0)-1)*exp(-90*mod(t-{o}+{p}/2,{p})),0)";
        var snare = $"if(gte(t,{o}),0.45*(2*random(1)-1)*exp(-25*mod(t-{o}-{p},2*{p})),0)";
        var noise = "0.02*(2*random(2)-1)";
        return media.Generate(name, "-f", "lavfi", "-i", $"aevalsrc='{kick}+{hat}+{snare}+{noise}':s=44100:d={N(seconds)}", "-ac", "2");
    }

    [Theory]
    [InlineData(120, 0.25)]
    [InlineData(97, 0.6)]
    [InlineData(140, 0.1)]
    [InlineData(84, 0.4)]
    public async Task Finds_the_tempo_and_the_beats_of_a_drum_pattern(double bpm, double offset)
    {
        var file = await Drums(bpm, offset, 20, $"drums {bpm}.wav");
        var result = BeatDetector.Analyse(media.Tools, file);
        Assert.True(result.HasSteadyBeat, $"confidence {result.Confidence}");
        Assert.True(Math.Abs(result.Bpm - bpm) <= 1,
            $"Found {result.Bpm:0.00} BPM, confidence {result.Confidence:0.00}, beats {string.Join(" ", result.Beats.Take(12).Select(b => b.ToString("0.000")))}");

        var period = 60 / bpm;
        Assert.InRange(result.Beats.Count, (int)(20 / period) - 3, (int)(20 / period) + 1);
        var signed = result.Beats.Select(b => b - (offset + Math.Round((b - offset) / period) * period)).ToList();
        Assert.True(signed.All(e => Math.Abs(e) < 0.015), "errors ms: " + string.Join(" ", signed.Select(e => (e * 1000).ToString("0"))));
        foreach (var beat in result.Beats)
        {
            // Distance to the nearest true kick.
            var k = Math.Round((beat - offset) / period);
            var error = Math.Abs(beat - (offset + k * period));
            Assert.True(error < 0.035, $"Beat at {beat:0.000} is {error * 1000:0} ms from a kick.");
        }

        Assert.Contains($"{Math.Round(bpm)} beats per minute", result.Spoken);
    }

    [Fact]
    public void The_built_in_upbeat_pulse_is_120_bpm_and_the_calm_pad_has_no_beat()
    {
        var folder = Path.Combine(media.Folder, "effects");
        AccessibleVideoEditor.Core.Sound.SoundEffects.WriteAll(folder);
        var pulse = BeatDetector.Analyse(media.Tools, Path.Combine(folder, "Upbeat pulse.wav"));
        Assert.True(pulse.HasSteadyBeat);
        Assert.InRange(pulse.Bpm, 119, 121);
        var pad = BeatDetector.Analyse(media.Tools, Path.Combine(folder, "Calm pad.wav"));
        Assert.False(pad.HasSteadyBeat, $"Calm pad: {pad.Bpm:0} BPM, confidence {pad.Confidence:0.00}");
    }

    [Fact]
    public async Task Steady_noise_has_no_beat()
    {
        var file = await media.Generate("noise.wav", "-f", "lavfi", "-i", "anoisesrc=a=0.3:c=pink:d=15", "-ac", "1");
        var result = BeatDetector.Analyse(media.Tools, file);
        Assert.False(result.HasSteadyBeat, $"confidence {result.Confidence}, {result.Bpm} BPM");
        Assert.Equal("No steady beat found in this sound.", result.Spoken);
    }

    [Fact]
    public void Very_short_or_silent_sound_has_no_beat()
    {
        Assert.False(BeatDetector.Analyse(new float[BeatDetector.SampleRate]).HasSteadyBeat);
        Assert.False(BeatDetector.Analyse(new float[BeatDetector.SampleRate * 10]).HasSteadyBeat);
    }

    [Fact]
    public void The_fft_matches_a_known_sine()
    {
        var re = new double[64];
        var im = new double[64];
        for (var i = 0; i < 64; i++)
        {
            re[i] = Math.Cos(2 * Math.PI * 5 * i / 64);
        }

        BeatDetector.Fft(re, im);
        Assert.Equal(32, Math.Sqrt(re[5] * re[5] + im[5] * im[5]), 6);
        Assert.Equal(0, Math.Sqrt(re[6] * re[6] + im[6] * im[6]), 6);
    }
}
