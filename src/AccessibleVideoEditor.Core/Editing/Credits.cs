using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>Credits for openly licensed music and sounds used in a project.</summary>
public static class Credits
{
    /// <summary>Adds a credit for a media file, once.</summary>
    public static bool Add(Project project, string text, string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            project.Credits.Any(c => c.Text == text.Trim() && string.Equals(c.MediaPath, mediaPath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        project.Credits.Add(new ProjectCredit { Text = text.Trim(), MediaPath = mediaPath ?? "" });
        return true;
    }

    /// <summary>Credits still needed: those whose media is still used, plus credits not tied to a file.</summary>
    public static IReadOnlyList<string> InUse(Project project)
    {
        var used = project.Layers.SelectMany(l => l.Items).Select(i => i.Path).Where(p => !string.IsNullOrEmpty(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return project.Credits
            .Where(c => string.IsNullOrEmpty(c.MediaPath) || used.Contains(c.MediaPath))
            .Select(c => c.Text)
            .Distinct()
            .ToList();
    }

    /// <summary>Writes "name credits.txt" next to an export when credits are needed; returns its path or null.</summary>
    public static string? WriteFor(Project project, string exportPath)
    {
        var credits = InUse(project);
        if (credits.Count == 0)
        {
            return null;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(exportPath)) ?? ".";
        var path = Path.Combine(folder, Path.GetFileNameWithoutExtension(exportPath) + " credits.txt");
        var lines = new List<string>
        {
            $"Credits for {Path.GetFileName(exportPath)}",
            "Copy these lines into your video description. The licences ask for this credit.",
            "",
        };
        lines.AddRange(credits);
        File.WriteAllLines(path, lines);
        return path;
    }

    /// <summary>Reads the credit file written next to a downloaded sound, if any.</summary>
    public static string? ForMediaFile(string mediaPath)
    {
        var file = mediaPath + ".credit.txt";
        if (!File.Exists(file))
        {
            return null;
        }

        var lines = File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        return lines.Count == 0 ? null : lines[0];
    }
}
