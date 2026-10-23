using System.Text.Json;
using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core.Sound;

/// <summary>A real recorded sound (not synthesized) shipped with the program, with where it came from.</summary>
public sealed record RecordedSound
{
    /// <summary>Path relative to the recordings folder, for example "Typing/Mechanical keyboard.mp3".</summary>
    [JsonPropertyName("file")]
    public string File { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("creator")]
    public string Creator { get; init; } = "";

    /// <summary>The page the recording was published on.</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("license")]
    public string License { get; init; } = "";

    [JsonPropertyName("seconds")]
    public double Seconds { get; init; }

    /// <summary>What a screen reader reads in the sound list.</summary>
    public string Spoken => $"{Name}, {Category}, {Math.Round(Seconds, 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds. {Description}".TrimEnd();
}

/// <summary>
/// The real recordings that come with the program: typing, clicks, whooshes, pops, reactions and more,
/// all public domain (CC0), listed in <c>sounds.json</c> beside the files.
/// </summary>
public static class RecordedSounds
{
    public const string ManifestName = "sounds.json";

    /// <summary>Reads the list in <paramref name="folder"/>; entries whose file is missing are left out.</summary>
    public static IReadOnlyList<RecordedSound> Load(string folder)
    {
        var manifest = Path.Combine(folder, ManifestName);
        if (!File.Exists(manifest))
        {
            return [];
        }

        var all = Parse(File.ReadAllText(manifest));
        return all.Where(s => File.Exists(FullPath(folder, s))).ToList();
    }

    public static IReadOnlyList<RecordedSound> Parse(string json) =>
        JsonSerializer.Deserialize<List<RecordedSound>>(json) ?? [];

    public static string FullPath(string folder, RecordedSound sound) =>
        Path.GetFullPath(Path.Combine(folder, sound.File.Replace('/', Path.DirectorySeparatorChar)));
}
