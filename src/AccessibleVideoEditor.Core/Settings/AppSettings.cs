using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Settings;

public enum SpeechOutput
{
    /// <summary>Talk to NVDA or JAWS directly when running, otherwise use Windows UI Automation.</summary>
    Automatic,

    /// <summary>Always use Windows UI Automation notifications (Narrator and others).</summary>
    UiAutomation,
}

/// <summary>User preferences, saved as JSON in the user's application data folder.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public Verbosity Verbosity { get; set; } = Verbosity.Normal;

    public TimeStyle TimeStyle { get; set; } = TimeStyle.Words;

    public SpeechOutput SpeechOutput { get; set; } = SpeechOutput.Automatic;

    public string EarconTheme { get; set; } = "Soft";

    /// <summary>Earcon loudness, 0 to 100.</summary>
    public int EarconVolume { get; set; } = 60;

    public bool Ripple { get; set; } = true;

    /// <summary>How far Alt+J and Alt+L slide an item, in seconds.</summary>
    public double SlideStep { get; set; } = 1.0;

    public double FineSlideStep { get; set; } = 0.1;

    /// <summary>How far J jumps back, in seconds.</summary>
    public double JumpStep { get; set; } = 5.0;

    /// <summary>How many seconds each preview renders ahead.</summary>
    public double PreviewSeconds { get; set; } = 60;

    public string ExportProfileId { get; set; } = "youtube-1080p";

    public string LastFolder { get; set; } = "";

    /// <summary>Folder with ffmpeg.exe and ffprobe.exe; empty to use the bundled copy.</summary>
    public string FfmpegFolder { get; set; } = "";

    public List<string> RecentProjects { get; set; } = new();

    public bool AutoSave { get; set; } = true;

    /// <summary>The language last chosen for speech recognition: a Whisper code, or auto.</summary>
    public string SpeechLanguage { get; set; } = "auto";

    /// <summary>Whisper processor threads; 0 is automatic. Set by the speed test.</summary>
    public int WhisperThreads { get; set; }

    /// <summary>Extra careful recognition (beam search of 5): about three times slower.</summary>
    public bool WhisperCareful { get; set; }

    /// <summary>Find speech first and skip silence and music.</summary>
    public bool WhisperSkipSilence { get; set; } = true;

    /// <summary>Use the graphics chip for speech recognition when possible.</summary>
    public bool WhisperGpu { get; set; } = true;

    /// <summary>The AI assistant service, an <see cref="Ai.AiProvider"/> id.</summary>
    public string AiProvider { get; set; } = "openrouter";

    /// <summary>The service address (ending in /v1). Empty uses the provider's own.</summary>
    public string AiBaseUrl { get; set; } = "";

    /// <summary>The model name. Empty uses the provider's suggestion.</summary>
    public string AiModel { get; set; } = "";

    /// <summary>The API key, encrypted for this Windows user (never stored as plain text).</summary>
    public string AiKeyProtected { get; set; } = "";

    /// <summary>Transcribe a quick draft with the Fast model first, then improve it in the background.</summary>
    public bool WhisperDraftFirst { get; set; } = true;

    /// <summary>Colours: System (follow Windows), Light or Dark. High Contrast always wins.</summary>
    public string Theme { get; set; } = "System";

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipScribe");

    public static string DefaultPath => Path.Combine(DefaultFolder, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
                settings.Clamp();
                return settings;
            }
        }
        catch (JsonException)
        {
            // A damaged settings file must never stop the program from starting.
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public void AddRecent(string projectPath)
    {
        RecentProjects.RemoveAll(p => string.Equals(p, projectPath, StringComparison.OrdinalIgnoreCase));
        RecentProjects.Insert(0, projectPath);
        if (RecentProjects.Count > 10)
        {
            RecentProjects.RemoveRange(10, RecentProjects.Count - 10);
        }
    }

    public void Clamp()
    {
        EarconVolume = Math.Clamp(EarconVolume, 0, 100);
        SlideStep = Math.Clamp(SlideStep, 0.01, 600);
        FineSlideStep = Math.Clamp(FineSlideStep, 0.01, 60);
        JumpStep = Math.Clamp(JumpStep, 0.1, 600);
        PreviewSeconds = Math.Clamp(PreviewSeconds, 10, 1800);
        RecentProjects ??= new();
        EarconTheme ??= "Soft";
        ExportProfileId ??= "youtube-1080p";
        LastFolder ??= "";
        FfmpegFolder ??= "";
        WhisperThreads = Math.Clamp(WhisperThreads, 0, 64);
        Theme ??= "System";
        SpeechLanguage ??= "auto";
    }

    public Speech.WhisperOptions WhisperOptions => new(WhisperThreads, WhisperCareful ? 5 : 1, WhisperSkipSilence, WhisperGpu);
}
