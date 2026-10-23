using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Checks;

public enum Severity
{
    Problem,
    Warning,
    Note,
}

/// <summary>One finding. <see cref="Time"/> lets the user jump straight to it.</summary>
public sealed record CheckFinding(Severity Severity, string Message, double? Time = null, Guid? LayerId = null, Guid? ItemId = null)
{
    public string Spoken => $"{Severity}: {Message}";
}

/// <summary>
/// Looks for things a sighted editor would notice on screen: black gaps, missing files, muted layers,
/// transitions that can't play and so on.
/// </summary>
public static class ProjectChecker
{
    public static IReadOnlyList<CheckFinding> Check(Project project)
    {
        var findings = new List<CheckFinding>();
        if (project.Layers.Count == 0 || project.Layers.All(l => l.Items.Count == 0))
        {
            findings.Add(new CheckFinding(Severity.Problem, "The project is empty. Add files with Control I."));
            return findings;
        }

        var total = Timeline.ProjectDuration(project);
        for (var li = 0; li < project.Layers.Count; li++)
        {
            var layer = project.Layers[li];
            var name = $"Layer {li + 1}, {layer.Name}";
            if (layer.Muted)
            {
                findings.Add(new CheckFinding(Severity.Warning, $"{name} is muted and will not be in the export.", LayerId: layer.Id));
            }

            var spans = Timeline.Spans(layer);
            for (var i = 0; i < layer.Items.Count; i++)
            {
                var item = layer.Items[i];
                if (item.Kind != ItemKind.Color && !File.Exists(item.Path))
                {
                    findings.Add(new CheckFinding(Severity.Problem, $"{name}: the file for {item.Name} is missing: {item.Path}.",
                        spans[i].Start, layer.Id, item.Id));
                }

                if (i > 0 && item.GapBefore > Timeline.Epsilon && Timeline.TransitionIsActive(layer.Kind, item.TransitionIn))
                {
                    findings.Add(new CheckFinding(Severity.Warning,
                        $"{name}: the transition into {item.Name} will not play because there is a gap of {TimeFormat.Words(item.GapBefore)} before it.",
                        spans[i].Start, layer.Id, item.Id));
                }

                if (item.IsTimeLimited && item.SourceIn + item.Duration > item.SourceDuration + 0.05)
                {
                    findings.Add(new CheckFinding(Severity.Warning,
                        $"{name}: {item.Name} is set longer than its file; the last picture will be held.",
                        spans[i].Start, layer.Id, item.Id));
                }
            }

            // Black gaps only matter on the bottom video layer: higher layers are transparent where empty.
            if (layer.Kind == LayerKind.Video && !layer.Muted && project.Layers.FindIndex(l => l.Kind == LayerKind.Video && !l.Muted) == li)
            {
                var previousEnd = 0.0;
                foreach (var span in spans)
                {
                    if (span.Start - previousEnd > 0.04)
                    {
                        findings.Add(new CheckFinding(Severity.Warning,
                            $"{name}: black picture for {TimeFormat.Words(span.Start - previousEnd)} at {TimeFormat.Words(previousEnd)}.",
                            previousEnd, layer.Id));
                    }

                    previousEnd = Math.Max(previousEnd, span.End);
                }

                if (total - previousEnd > 0.04)
                {
                    findings.Add(new CheckFinding(Severity.Warning,
                        $"{name}: the picture ends at {TimeFormat.Words(previousEnd)} but sound continues to {TimeFormat.Words(total)}. The end will be black.",
                        previousEnd, layer.Id));
                }
            }
        }

        if (!project.Layers.Any(l => l.Kind == LayerKind.Video && l.Items.Count > 0))
        {
            findings.Add(new CheckFinding(Severity.Note, "There are no pictures, so a video export will be black. Choose an audio-only format, or add a picture."));
        }

        if (project.RangeStart is { } rs && project.RangeEnd is { } re && re - rs < 0.1)
        {
            findings.Add(new CheckFinding(Severity.Note, "Range start and range end are at the same place.", rs));
        }

        var credits = Editing.Credits.InUse(project);
        if (credits.Count > 0)
        {
            findings.Add(new CheckFinding(Severity.Note,
                $"This project uses {(credits.Count == 1 ? "1 sound that needs" : $"{credits.Count} sounds that need")} credit. A credits file is written next to the export: {string.Join(" ", credits)}"));
        }

        if (findings.Count == 0)
        {
            findings.Add(new CheckFinding(Severity.Note, $"No problems found. The project is {TimeFormat.Words(total)} long."));
        }

        return findings;
    }
}
