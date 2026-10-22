namespace AccessibleVideoEditor.Core.Sound;

/// <summary>Moments that get a short sound ("earcon"). The file name of each is its name plus ".wav".</summary>
public enum EarconEvent
{
    /// <summary>Moved to another item.</summary>
    Navigate,

    /// <summary>Tried to go past the first or last item, or a limit.</summary>
    Edge,

    /// <summary>Moved to another layer.</summary>
    LayerChange,

    MarkerAdded,

    /// <summary>Jumped to a marker.</summary>
    MarkerReached,

    Deleted,

    /// <summary>An edit worked.</summary>
    Done,

    Undo,

    /// <summary>An edit could not be done.</summary>
    Error,

    Muted,

    Unmuted,

    PlayStart,

    PlayStop,

    /// <summary>A long job (preview or export) has finished.</summary>
    Finished,
}

/// <summary>A set of earcons. Built-in themes are synthesized; user themes are folders of WAV files.</summary>
public sealed class EarconTheme
{
    private readonly Dictionary<EarconEvent, byte[]> _sounds;

    private EarconTheme(string name, string description, Dictionary<EarconEvent, byte[]> sounds)
    {
        Name = name;
        Description = description;
        _sounds = sounds;
    }

    public string Name { get; }

    public string Description { get; }

    /// <summary>The WAV data for an event, or null when the theme has no sound for it.</summary>
    public byte[]? Get(EarconEvent e) => _sounds.TryGetValue(e, out var wav) ? wav : null;

    public static string FileName(EarconEvent e) => e + ".wav";

    public static IReadOnlyList<EarconTheme> BuiltIn { get; } = new[]
    {
        Build("Soft", "Gentle, low sine tones. Easy on the ears for long sessions.", Wave.Sine, 1.0, 0.35),
        Build("Classic", "Clear triangle-wave beeps.", Wave.Triangle, 1.25, 0.4),
        Build("Crisp", "Short, bright, high clicks. Very quick.", Wave.Sine, 2.0, 0.35, crisp: true),
        Build("Retro", "8-bit game console style square waves.", Wave.Square, 1.0, 0.22),
        new EarconTheme("Off", "No sounds; speech only.", new()),
    };

    /// <summary>Loads a user theme folder. Missing files simply have no sound.</summary>
    public static EarconTheme FromFolder(string folder)
    {
        var sounds = new Dictionary<EarconEvent, byte[]>();
        foreach (var e in Enum.GetValues<EarconEvent>())
        {
            var path = Path.Combine(folder, FileName(e));
            if (File.Exists(path))
            {
                sounds[e] = File.ReadAllBytes(path);
            }
        }

        return new EarconTheme(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            "Your own sound theme.", sounds);
    }

    /// <summary>Writes the theme's sounds to a folder, showing the file names a custom theme needs.</summary>
    public void Export(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (e, wav) in _sounds)
        {
            File.WriteAllBytes(Path.Combine(folder, FileName(e)), wav);
        }
    }

    private static EarconTheme Build(string name, string description, Wave wave, double pitch, double gain, bool crisp = false)
    {
        var length = crisp ? 0.5 : 1.0;
        float[] T(double freq, double seconds, double? to = null) =>
            Synth.Tone(freq * pitch, seconds * length, wave, gain, 0.003, crisp ? 0.02 : 0.04, to * pitch);

        var sounds = new Dictionary<EarconEvent, float[]>
        {
            [EarconEvent.Navigate] = T(880, 0.03),
            [EarconEvent.Edge] = Synth.Sequence(T(220, 0.06), Synth.Silence(0.02), T(220, 0.06)),
            [EarconEvent.LayerChange] = T(520, 0.07, 700),
            [EarconEvent.MarkerAdded] = Synth.Sequence(T(660, 0.05), T(990, 0.07)),
            [EarconEvent.MarkerReached] = T(990, 0.05),
            [EarconEvent.Deleted] = T(500, 0.12, 250),
            [EarconEvent.Done] = T(740, 0.05),
            [EarconEvent.Undo] = T(600, 0.1, 400),
            [EarconEvent.Error] = Synth.Sequence(T(180, 0.09), Synth.Silence(0.03), T(150, 0.12)),
            [EarconEvent.Muted] = T(440, 0.08, 300),
            [EarconEvent.Unmuted] = T(300, 0.08, 440),
            [EarconEvent.PlayStart] = T(440, 0.05, 660),
            [EarconEvent.PlayStop] = T(660, 0.05, 440),
            [EarconEvent.Finished] = Synth.Sequence(T(523, 0.08), T(659, 0.08), T(784, 0.14)),
        };

        return new EarconTheme(name, description, sounds.ToDictionary(p => p.Key, p => WavWriter.ToWav(p.Value, stereo: false)));
    }
}
