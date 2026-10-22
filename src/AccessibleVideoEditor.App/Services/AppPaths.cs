using System.IO;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;

namespace AccessibleVideoEditor.App.Services;

/// <summary>Where the program keeps its files.</summary>
public static class AppPaths
{
    public static string AppData => AppSettings.DefaultFolder;

    public static string KeymapFile => Path.Combine(AppData, "keymap.json");

    public static string AutosaveFile => Path.Combine(AppData, "autosave.avproj");

    public static string LogFile => Path.Combine(AppData, "log.txt");

    /// <summary>Documents\Accessible Video Editor.</summary>
    public static string Documents =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Accessible Video Editor");

    public static string Projects => Path.Combine(Documents, "Projects");

    /// <summary>Users put their own sound effects and music here.</summary>
    public static string UserSoundEffects => Path.Combine(Documents, "Sound Effects");

    /// <summary>Each sub-folder is a user earcon theme of WAV files named like the built-in ones.</summary>
    public static string UserSoundThemes => Path.Combine(Documents, "Sound Themes");

    public static string PreviewFolder => Path.Combine(Path.GetTempPath(), "AccessibleVideoEditor");

    /// <summary>
    /// The built-in sound effects: installed next to the program, or generated once into local app data
    /// when running from a build folder.
    /// </summary>
    public static string BuiltInSoundEffects
    {
        get
        {
            var installed = Path.Combine(AppContext.BaseDirectory, "Sounds", "Effects");
            if (Directory.Exists(installed) && Directory.EnumerateFiles(installed, "*.wav").Any())
            {
                return installed;
            }

            var generated = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccessibleVideoEditor", "Sound Effects");
            SoundEffects.WriteAll(generated);
            return generated;
        }
    }

    public static void EnsureFolders()
    {
        foreach (var folder in new[] { AppData, Projects, UserSoundEffects, UserSoundThemes, PreviewFolder })
        {
            Directory.CreateDirectory(folder);
        }

        var readme = Path.Combine(UserSoundEffects, "Read me.txt");
        if (!File.Exists(readme))
        {
            File.WriteAllText(readme,
                "Put your own sound effects and music in this folder (WAV, MP3, M4A, FLAC or OGG).\r\n" +
                "They appear in the Add sound effect list (Control Shift E) under \"My sounds\".\r\n" +
                "Sub-folders become categories.\r\n");
        }

        var themeReadme = Path.Combine(UserSoundThemes, "Read me.txt");
        if (!File.Exists(themeReadme))
        {
            File.WriteAllText(themeReadme,
                "To make your own sound theme, create a folder here and put WAV files in it named:\r\n" +
                string.Join("\r\n", Enum.GetNames<EarconEvent>().Select(n => "  " + n + ".wav")) + "\r\n" +
                "Use Tools, Export sound theme to get a copy of a built-in theme to start from.\r\n" +
                "Missing files are simply silent. Choose your theme in Preferences.\r\n");
        }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppData);
            File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
