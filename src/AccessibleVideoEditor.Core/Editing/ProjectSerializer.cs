using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>Reads and writes project files (readable, indented JSON).</summary>
public static class ProjectSerializer
{
    public const string FileExtension = ".avproj";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    public static string ToJson(Project project, bool indented = true) =>
        JsonSerializer.Serialize(project, indented ? Options : CompactOptions);

    public static Project FromJson(string json)
    {
        var project = JsonSerializer.Deserialize<Project>(json, Options)
                      ?? throw new InvalidDataException("The project file is empty.");
        if (project.FormatVersion > Project.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                "This project was saved by a newer version of ClipScribe. Please update the program.");
        }

        Normalize(project);
        return project;
    }

    public static Project Clone(Project project) => FromJson(ToJson(project, indented: false));

    public static void Save(Project project, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temporary file first so a crash never leaves a half-written project.
        var temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(project));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Loads a project. Media that has moved is looked for next to the project file, so a project folder
    /// copied to another computer still works.
    /// </summary>
    public static Project Load(string path)
    {
        var project = FromJson(File.ReadAllText(path));
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        foreach (var item in project.Layers.SelectMany(l => l.Items))
        {
            if (item.Kind == ItemKind.Color || string.IsNullOrEmpty(item.Path) || File.Exists(item.Path))
            {
                continue;
            }

            var fileName = Path.GetFileName(item.Path.Replace('\\', Path.DirectorySeparatorChar));
            var candidate = Path.Combine(folder, fileName);
            if (File.Exists(candidate))
            {
                item.Path = candidate;
            }
        }

        return project;
    }

    /// <summary>Fills in anything a hand-edited or older file may be missing.</summary>
    private static void Normalize(Project project)
    {
        project.Layers ??= new();
        project.Markers ??= new();
        project.Credits ??= new();
        if (project.Width <= 0 || project.Height <= 0)
        {
            project.Width = 1920;
            project.Height = 1080;
        }

        if (project.FrameRate <= 0 || double.IsNaN(project.FrameRate))
        {
            project.FrameRate = 30;
        }

        foreach (var layer in project.Layers)
        {
            layer.Items ??= new();
            layer.Defaults ??= new();
            foreach (var item in layer.Items)
            {
                item.TransitionIn ??= new();
                item.Placement ??= new();
                item.Audio ??= new();
                item.Text ??= new();
                item.Text.Content ??= "";
                item.Text.Font ??= "Segoe UI";
                item.Text.ColorName ??= "White";
                item.Name ??= "";
                item.Path ??= "";
                item.ColorName ??= "Black";
                if (item.Duration < Timeline.MinItemDuration || double.IsNaN(item.Duration))
                {
                    item.Duration = Timeline.MinItemDuration;
                }

                if (item.GapBefore < 0 || double.IsNaN(item.GapBefore))
                {
                    item.GapBefore = 0;
                }
            }
        }

        project.Markers.Sort((a, b) => a.Time.CompareTo(b.Time));
    }
}
