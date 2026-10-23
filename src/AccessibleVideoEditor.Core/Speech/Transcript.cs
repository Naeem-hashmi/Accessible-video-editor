namespace AccessibleVideoEditor.Core.Speech;

/// <summary>One spoken word, timed in seconds of its media file.</summary>
public sealed class TranscriptWord
{
    public string Text { get; set; } = "";

    public double Start { get; set; }

    public double End { get; set; }

    /// <summary>Whisper's confidence, 0 to 1.</summary>
    public double Confidence { get; set; } = 1;

    public TranscriptWord Clone() => (TranscriptWord)MemberwiseClone();
}

/// <summary>
/// The words spoken in one media file. Stored once per file in the project, in file time, so every clip
/// made from the file (after splits and cuts) shows the words it still plays.
/// </summary>
public sealed class Transcript
{
    public string MediaPath { get; set; } = "";

    /// <summary>Language code, for example en, hi or ur.</summary>
    public string Language { get; set; } = "";

    /// <summary>The Whisper model used, for example large-v3-turbo.</summary>
    public string Model { get; set; } = "";

    public List<TranscriptWord> Words { get; set; } = new();

    /// <summary>
    /// The parts of the file that have been transcribed, in file time. Empty means the whole file.
    /// See <see cref="TranscriptCoverage"/>.
    /// </summary>
    public List<Audio.TimeStretch> Covered { get; set; } = new();

    /// <summary>Words that fall inside the file stretch [from, to).</summary>
    public IEnumerable<TranscriptWord> WordsBetween(double from, double to) =>
        Words.Where(w => (w.Start + w.End) / 2 >= from - 1e-6 && (w.Start + w.End) / 2 < to - 1e-6);

    /// <summary>True when both have the same words at the same times (nothing was corrected in between).</summary>
    public bool SameWords(Transcript other) =>
        Words.Count == other.Words.Count &&
        Words.Zip(other.Words).All(p => p.First.Text == p.Second.Text && Math.Abs(p.First.Start - p.Second.Start) < 1e-6 && Math.Abs(p.First.End - p.Second.End) < 1e-6);

    public Transcript Clone() => new()
    {
        MediaPath = MediaPath,
        Language = Language,
        Model = Model,
        Words = Words.Select(w => w.Clone()).ToList(),
        Covered = [.. Covered],
    };

    /// <summary>Languages offered, with their Whisper codes. Whisper knows 99; these are the common ones.</summary>
    public static IReadOnlyList<(string Code, string Name)> Languages { get; } =
    [
        ("auto", "Detect automatically"),
        ("en", "English"),
        ("ur", "Urdu"),
        ("hi", "Hindi"),
        ("ar", "Arabic"),
        ("bn", "Bengali"),
        ("pa", "Punjabi"),
        ("es", "Spanish"),
        ("fr", "French"),
        ("de", "German"),
        ("pt", "Portuguese"),
        ("ru", "Russian"),
        ("tr", "Turkish"),
        ("fa", "Persian"),
        ("zh", "Chinese"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("id", "Indonesian"),
    ];

    public static string LanguageName(string code) =>
        Languages.FirstOrDefault(l => l.Code == code).Name ?? code;
}
