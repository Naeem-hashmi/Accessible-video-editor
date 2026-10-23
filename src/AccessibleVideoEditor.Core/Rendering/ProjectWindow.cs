using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>
/// Makes a copy of a project that starts at a given time, so a preview or a range export only has to
/// process the part that is needed instead of everything from the beginning.
/// </summary>
public static class ProjectWindow
{
    public static Project StartingAt(Project source, double start)
    {
        var project = ProjectSerializer.Clone(source);
        if (start <= Timeline.Epsilon)
        {
            return project;
        }

        foreach (var layer in project.Layers)
        {
            var spans = Timeline.Spans(layer);
            var first = -1;
            for (var i = 0; i < spans.Count; i++)
            {
                if (spans[i].End > start + Timeline.Epsilon)
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
            {
                layer.Items.Clear();
                continue;
            }

            layer.Items.RemoveRange(0, first);
            var item = layer.Items[0];
            var span = spans[first];
            if (span.Start < start)
            {
                var cut = start - span.Start;
                if (item.Kind is ItemKind.Video or ItemKind.Audio)
                {
                    item.SourceIn = item.ToSource(cut);
                }

                item.Duration -= cut;
                item.FadeIn = Math.Max(0, item.FadeIn - cut);
                item.FadeOut = Math.Min(item.FadeOut, item.Duration);
                item.GapBefore = 0;
            }
            else
            {
                item.GapBefore = span.Start - start;
            }

            item.TransitionIn = new TransitionSpec();
        }

        project.Markers.Clear();
        project.RangeStart = null;
        project.RangeEnd = null;
        return project;
    }
}
