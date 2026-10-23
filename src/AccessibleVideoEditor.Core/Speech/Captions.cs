using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Speech;

/// <summary>A word placed on the project timeline.</summary>
public readonly record struct TimedWord(string Text, double Start, double End);

/// <summary>One caption: a short line of words and when it shows.</summary>
public readonly record struct CaptionLine(string Text, double Start, double End);

/// <summary>Turns transcripts into caption text items on their own layer.</summary>
public static class Captions
{
    public const string LayerName = "Captions";

    /// <summary>Most letters in one caption line: short lines are easy to read on phones.</summary>
    public const int MaxCharacters = 32;

    public const double MaxSeconds = 3.5;

    /// <summary>A pause at least this long starts a new caption.</summary>
    public const double PauseBreak = 0.6;

    /// <summary>The spoken words of a layer, placed on the timeline, in the order they play.</summary>
    public static IReadOnlyList<TimedWord> WordsOnTimeline(Project project, Layer layer)
    {
        var result = new List<TimedWord>();
        var spans = Timeline.Spans(layer);
        for (var i = 0; i < layer.Items.Count; i++)
        {
            var item = layer.Items[i];
            if (project.TranscriptFor(item.Path) is not { } transcript || item.Kind is not (ItemKind.Video or ItemKind.Audio))
            {
                continue;
            }

            foreach (var word in transcript.WordsBetween(item.SourceIn, item.SourceOut))
            {
                var start = spans[i].Start + Math.Max(0, item.ToTimeline(word.Start));
                var end = spans[i].Start + Math.Min(item.Duration, item.ToTimeline(word.End));
                result.Add(new TimedWord(word.Text, start, Math.Max(start, end)));
            }
        }

        return result;
    }

    /// <summary>Groups words into caption lines.</summary>
    public static IReadOnlyList<CaptionLine> Lines(IReadOnlyList<TimedWord> words, int maxCharacters = MaxCharacters, double maxSeconds = MaxSeconds)
    {
        var lines = new List<CaptionLine>();
        var current = new List<TimedWord>();

        void Flush()
        {
            if (current.Count > 0)
            {
                lines.Add(new CaptionLine(string.Join(" ", current.Select(w => w.Text)), current[0].Start, current[^1].End));
                current.Clear();
            }
        }

        foreach (var word in words)
        {
            if (current.Count > 0)
            {
                var length = current.Sum(w => w.Text.Length + 1) + word.Text.Length;
                var gap = word.Start - current[^1].End;
                var endsSentence = current[^1].Text.Length > 0 && ".?!।۔؟".Contains(current[^1].Text[^1]);
                if (length > maxCharacters || word.End - current[0].Start > maxSeconds || gap >= PauseBreak || endsSentence)
                {
                    Flush();
                }
            }

            current.Add(word);
        }

        Flush();

        // Show each line at least 0.8 s and 0.3 s past its last word, but never into the next line.
        for (var i = 0; i < lines.Count; i++)
        {
            var next = i + 1 < lines.Count ? lines[i + 1].Start : double.PositiveInfinity;
            var wanted = Math.Max(lines[i].End + 0.3, lines[i].Start + 0.8);
            lines[i] = lines[i] with { End = Math.Max(lines[i].End, Math.Min(wanted, next)) };
        }

        return lines;
    }

    /// <summary>A caption style that is easy to read: white, bold, outlined, on a dark box, at the bottom.</summary>
    public static TextStyle DefaultStyle(string language) => new()
    {
        Font = FontFor(language),
        SizePercent = 6,
        ColorName = "White",
        Bold = true,
        Outline = true,
        Background = TextBackground.DarkBox,
        AnimationIn = TextAnimation.None,
        AnimationOut = TextAnimation.None,
    };

    /// <summary>
    /// A font that has the letters of the language: Noto Naskh Arabic (comes with ClipScribe) for Urdu,
    /// Arabic and Persian, Nirmala UI (part of Windows) for Hindi and other Indian scripts, Segoe UI otherwise.
    /// FFmpeg's text drawing can't draw the Nastaliq style (tested: it shows empty boxes), so Urdu uses Naskh.
    /// </summary>
    public static string FontFor(string language) => language switch
    {
        "ur" or "ar" or "fa" => "Noto Naskh Arabic",
        "hi" or "bn" or "pa" => "Nirmala UI",
        _ => "Segoe UI",
    };

    /// <summary>
    /// Makes (or replaces) the Captions layer from the words spoken on <paramref name="speechLayer"/>.
    /// </summary>
    public static EditResult Create(Project project, Layer speechLayer, TextStyle? style = null)
    {
        var words = WordsOnTimeline(project, speechLayer);
        if (words.Count == 0)
        {
            return EditResult.Fail($"{speechLayer.Name} has no transcript yet. Transcribe it first.");
        }

        var language = speechLayer.Items.Select(i => project.TranscriptFor(i.Path)?.Language).FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "";
        style ??= DefaultStyle(language);
        var lines = Lines(words);
        var layer = project.Layers.FirstOrDefault(l => l.Name == LayerName && l.Kind == LayerKind.Video && l.Items.All(i => i.Kind == ItemKind.Text));
        if (layer is null)
        {
            layer = ProjectEditor.AddLayer(project, LayerKind.Video, LayerName);
        }
        else
        {
            // Captions go on top of everything.
            project.Layers.Remove(layer);
            project.Layers.Add(layer);
        }

        layer.Items.Clear();
        layer.Defaults.Placement = new Placement { Position = PositionPreset.BottomCenter };
        var placement = new Placement { Position = PositionPreset.BottomCenter };
        var end = 0.0;
        foreach (var line in lines)
        {
            var lineStyle = style.Clone();
            lineStyle.Content = line.Text;
            var item = MediaProbe.CreateTextItem(lineStyle, line.End - line.Start, layer.Defaults, placement);
            item.GapBefore = Math.Max(0, line.Start - end);
            layer.Items.Add(item);
            end = line.Start + item.Duration;
        }

        return EditResult.Success(
            $"Added {lines.Count} captions from {words.Count} words on a {LayerName} layer, in {Transcript.LanguageName(language)}. Check them with the transcript.",
            layer.Items[0].Id, layer.Id);
    }
}
