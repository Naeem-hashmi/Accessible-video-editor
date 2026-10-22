using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>Turns plain-language sound settings into FFmpeg audio filters.</summary>
public static class AudioFilters
{
    /// <summary>Bass shelf frequency for the "low" control, in hertz.</summary>
    public const int LowFrequency = 100;

    /// <summary>Centre frequency for the "middle" control, in hertz.</summary>
    public const int MidFrequency = 1000;

    /// <summary>Treble shelf frequency for the "high" control, in hertz.</summary>
    public const int HighFrequency = 8000;

    public static IReadOnlyList<string> For(AudioCleanup audio)
    {
        var filters = new List<string>();
        if (audio.RemoveRumble)
        {
            filters.Add("highpass=f=80");
        }

        if (audio.Hum != HumFilter.Off)
        {
            var mains = audio.Hum == HumFilter.Hz50 ? 50 : 60;
            for (var harmonic = 1; harmonic <= 3; harmonic++)
            {
                filters.Add($"bandreject=f={mains * harmonic}:width_type=q:w=10");
            }
        }

        switch (audio.NoiseReduction)
        {
            case NoiseReduction.Light:
                filters.Add("afftdn=nr=8:nf=-50");
                break;
            case NoiseReduction.Medium:
                filters.Add("afftdn=nr=15:nf=-45");
                break;
            case NoiseReduction.Strong:
                filters.Add("afftdn=nr=25:nf=-40");
                break;
        }

        if (audio.LowDb != 0)
        {
            filters.Add($"bass=g={audio.LowDb}:f={LowFrequency}");
        }

        if (audio.MidDb != 0)
        {
            filters.Add($"equalizer=f={MidFrequency}:t=q:w=1:g={audio.MidDb}");
        }

        if (audio.HighDb != 0)
        {
            filters.Add($"treble=g={audio.HighDb}:f={HighFrequency}");
        }

        switch (audio.Voice)
        {
            case VoicePreset.Clear:
                filters.Add("highpass=f=90");
                filters.Add("equalizer=f=3000:t=q:w=1.2:g=3");
                filters.Add("acompressor=threshold=0.125:ratio=3:attack=10:release=200:makeup=2");
                break;
            case VoicePreset.Warm:
                filters.Add("bass=g=3:f=150");
                filters.Add("treble=g=-2:f=6000");
                filters.Add("acompressor=threshold=0.18:ratio=2:attack=20:release=250:makeup=1.5");
                break;
            case VoicePreset.Broadcast:
                filters.Add("highpass=f=80");
                filters.Add("equalizer=f=200:t=q:w=1:g=2");
                filters.Add("equalizer=f=3500:t=q:w=1:g=3");
                filters.Add("acompressor=threshold=0.1:ratio=4:attack=5:release=150:makeup=2.5");
                break;
        }

        if (audio.Normalize)
        {
            // loudnorm works at a high internal sample rate; bring it back to the project rate.
            filters.Add("loudnorm=I=-16:TP=-1.5:LRA=11");
            filters.Add("aresample=48000");
        }

        return filters;
    }

    public static string NoiseReductionName(NoiseReduction value) => value switch
    {
        NoiseReduction.Light => "Light (gentle hiss removal)",
        NoiseReduction.Medium => "Medium (fan and room noise)",
        NoiseReduction.Strong => "Strong (very noisy recordings)",
        _ => "Off",
    };

    public static string HumName(HumFilter value) => value switch
    {
        HumFilter.Hz50 => "Remove 50 hertz hum (Europe, Asia, Africa)",
        HumFilter.Hz60 => "Remove 60 hertz hum (Americas)",
        _ => "Off",
    };

    public static string VoiceName(VoicePreset value) => value switch
    {
        VoicePreset.Clear => "Clear voice (brighter, more even)",
        VoicePreset.Warm => "Warm voice (fuller, softer)",
        VoicePreset.Broadcast => "Broadcast voice (radio style, strong compression)",
        _ => "None",
    };
}
