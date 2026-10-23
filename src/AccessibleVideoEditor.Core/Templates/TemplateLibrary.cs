using System.Text.Json;
using System.Text.Json.Serialization;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Templates;

/// <summary>A template shown to the user: a built-in one, or a file in a template folder.</summary>
public sealed record TemplateInfo(string Name, string Category, string Description, string? FilePath, bool IncludesMedia, ProjectTemplate? BuiltIn)
{
    public bool IsBuiltIn => BuiltIn is not null;

    public string Spoken => $"{Name}, {Category}{(IncludesMedia ? ", with media" : "")}. {Description}";
}

/// <summary>
/// User templates kept as files in folders (each folder is a category) under one root, usually
/// Documents\ClipScribe\Templates. A template is a project saved without, or with, its media.
/// Text items and colour cards are always kept, because they need no files: a template can hold a
/// ready-made title sequence.
/// </summary>
public sealed class TemplateLibrary
{
    public const string FileExtension = ".avtemplate";
    public const string DefaultCategory = "My templates";
    public const string BuiltInCategory = "Built-in";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public TemplateLibrary(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public IReadOnlyList<string> Categories()
    {
        var list = new List<string> { DefaultCategory };
        if (Directory.Exists(Root))
        {
            list.AddRange(Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>()
                .Where(n => !string.Equals(n, DefaultCategory, StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase));
        }

        return list;
    }

    /// <summary>Built-in templates first, then the user's, grouped by folder.</summary>
    public IReadOnlyList<TemplateInfo> List()
    {
        var list = ProjectTemplates.All
            .Select(t => new TemplateInfo(t.Name, BuiltInCategory, t.Description, null, false, t))
            .ToList();
        if (!Directory.Exists(Root))
        {
            return list;
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*" + FileExtension, SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                var data = Read(file);
                var folder = Path.GetDirectoryName(file)!;
                var category = string.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                    ? DefaultCategory
                    : Path.GetFileName(folder);
                list.Add(new TemplateInfo(Path.GetFileNameWithoutExtension(file), category, data.Description, file, data.IncludesMedia, null));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException)
            {
                // A damaged template is skipped rather than breaking the whole list.
            }
        }

        return list;
    }

    /// <summary>Saves <paramref name="project"/> as a template and returns it.</summary>
    public TemplateInfo Save(Project project, string name, string category, string description, bool includeMedia)
    {
        var safeName = SafeName(name);
        var folder = FolderFor(category);
        Directory.CreateDirectory(folder);
        var copy = ProjectSerializer.Clone(project);
        copy.Title = safeName;
        copy.RangeStart = null;
        copy.RangeEnd = null;
        if (!includeMedia)
        {
            foreach (var layer in copy.Layers)
            {
                // Keep generated items (text, colour cards), drop items that point at files.
                layer.Items.RemoveAll(i => i.Kind is not (ItemKind.Text or ItemKind.Color));
                for (var i = 0; i < layer.Items.Count; i++)
                {
                    if (i == 0 && layer.Items[i].GapBefore < 0)
                    {
                        layer.Items[i].GapBefore = 0;
                    }
                }
            }

            copy.Markers.Clear();
            copy.Credits.Clear();
        }

        var path = Path.Combine(folder, safeName + FileExtension);
        var data = new TemplateFile { Description = description.Trim(), IncludesMedia = includeMedia, Project = copy };
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
        File.Move(temp, path, overwrite: true);
        return new TemplateInfo(safeName, string.IsNullOrWhiteSpace(category) ? DefaultCategory : category.Trim(), data.Description, path, includeMedia, null);
    }

    /// <summary>A new project made from a template, with fresh identities so it is independent.</summary>
    public Project Create(TemplateInfo template)
    {
        var project = template.BuiltIn is { } builtIn ? builtIn.Create() : ProjectSerializer.Clone(Read(template.FilePath!).Project);
        project.Title = template.Name;
        foreach (var layer in project.Layers)
        {
            layer.Id = Guid.NewGuid();
            foreach (var item in layer.Items)
            {
                item.Id = Guid.NewGuid();
            }
        }

        foreach (var marker in project.Markers)
        {
            marker.Id = Guid.NewGuid();
        }

        return project;
    }

    public TemplateInfo Rename(TemplateInfo template, string newName) => MoveTo(template, newName, template.Category);

    public TemplateInfo Move(TemplateInfo template, string category) => MoveTo(template, template.Name, category);

    public void Delete(TemplateInfo template)
    {
        RequireUserTemplate(template);
        File.Delete(template.FilePath!);
    }

    /// <summary>Copies a template file (for example one shared by a friend) into a folder.</summary>
    public TemplateInfo Import(string file, string category)
    {
        var data = Read(file);
        var folder = FolderFor(category);
        Directory.CreateDirectory(folder);
        var name = SafeName(Path.GetFileNameWithoutExtension(file));
        var target = UniquePath(folder, name);
        File.Copy(file, target);
        return new TemplateInfo(Path.GetFileNameWithoutExtension(target), category, data.Description, target, data.IncludesMedia, null);
    }

    public void CreateCategory(string name) => Directory.CreateDirectory(FolderFor(name));

    public string FolderFor(string category) =>
        string.IsNullOrWhiteSpace(category) || string.Equals(category.Trim(), DefaultCategory, StringComparison.OrdinalIgnoreCase)
            ? Root
            : Path.Combine(Root, SafeName(category));

    private TemplateInfo MoveTo(TemplateInfo template, string newName, string category)
    {
        RequireUserTemplate(template);
        var folder = FolderFor(category);
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, SafeName(newName) + FileExtension);
        if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(template.FilePath!), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(target))
            {
                throw new IOException($"A template called {SafeName(newName)} already exists in {category}.");
            }

            File.Move(template.FilePath!, target);
        }

        return template with { Name = SafeName(newName), Category = string.IsNullOrWhiteSpace(category) ? DefaultCategory : category, FilePath = target };
    }

    private static void RequireUserTemplate(TemplateInfo template)
    {
        if (template.IsBuiltIn || template.FilePath is null)
        {
            throw new InvalidOperationException("Built-in templates can't be changed. Save a copy with Save as template.");
        }
    }

    private static TemplateFile Read(string path)
    {
        var data = JsonSerializer.Deserialize<TemplateFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidDataException("The template file is empty.");
        if (data.Project is null)
        {
            throw new InvalidDataException("The template file has no project in it.");
        }

        // Run the project through the normal loader so damaged values are repaired.
        data.Project = ProjectSerializer.FromJson(ProjectSerializer.ToJson(data.Project));
        return data;
    }

    private static string UniquePath(string folder, string name)
    {
        var path = Path.Combine(folder, name + FileExtension);
        var n = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"{name} {n++}{FileExtension}");
        }

        return path;
    }

    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string((name ?? "").Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        return cleaned.Length == 0 ? "Untitled" : cleaned;
    }

    private sealed class TemplateFile
    {
        public int FormatVersion { get; set; } = 1;

        public string Description { get; set; } = "";

        public bool IncludesMedia { get; set; }

        public Project Project { get; set; } = new();
    }
}

/// <summary>Ready-made text styles: a starting point for titles, captions and credits.</summary>
public sealed record TextPreset(string Name, string Description, TextStyle Style, Placement Placement, double Duration);

public static class TextPresets
{
    public static IReadOnlyList<TextPreset> All { get; } = new TextPreset[]
    {
        new("Title", "Big bold words in the centre that fade in and out.",
            new TextStyle { SizePercent = 12, Bold = true, AnimationIn = TextAnimation.Fade, AnimationOut = TextAnimation.Fade, AnimationDuration = 0.7 },
            new Placement { Position = PositionPreset.Center }, 4),
        new("Lower third", "A name or label at the bottom left on a dark box, sliding in from the left.",
            new TextStyle { SizePercent = 6, Background = TextBackground.DarkBox, AnimationIn = TextAnimation.SlideFromLeft, AnimationOut = TextAnimation.SlideFromLeft },
            new Placement { Position = PositionPreset.BottomLeft }, 5),
        new("Caption", "Subtitle-style words at the bottom centre on a dark box.",
            new TextStyle { SizePercent = 5.5, Background = TextBackground.DarkBox, AnimationIn = TextAnimation.Fade, AnimationOut = TextAnimation.Fade, AnimationDuration = 0.2 },
            new Placement { Position = PositionPreset.BottomCenter }, 3),
        new("Top headline", "Bold words across the top, dropping in from above.",
            new TextStyle { SizePercent = 8, Bold = true, AnimationIn = TextAnimation.SlideFromTop, AnimationOut = TextAnimation.Fade },
            new Placement { Position = PositionPreset.TopCenter }, 4),
        new("Typewriter", "Words typed out letter by letter at the top left.",
            new TextStyle { Font = "Consolas", SizePercent = 6, AnimationIn = TextAnimation.Typewriter, AnimationOut = TextAnimation.Fade, AnimationDuration = 2 },
            new Placement { Position = PositionPreset.TopLeft }, 5),
        new("Credits", "Centred lines that float up gently, for thanks and credits at the end.",
            new TextStyle { SizePercent = 5, AnimationIn = TextAnimation.Rise, AnimationOut = TextAnimation.Rise, AnimationDuration = 1 },
            new Placement { Position = PositionPreset.Center }, 6),
        new("Call to action", "\"Subscribe\" style words at the bottom right on a light box, sliding in from the right.",
            new TextStyle { SizePercent = 6, Bold = true, ColorName = "Black", Background = TextBackground.LightBox, Outline = false, AnimationIn = TextAnimation.SlideFromRight, AnimationOut = TextAnimation.SlideFromRight },
            new Placement { Position = PositionPreset.BottomRight }, 4),
    };
}
