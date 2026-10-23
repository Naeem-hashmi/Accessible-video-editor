using System.Text;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Speech;

/// <summary>A suggestion to cut words <see cref="First"/> to <see cref="Last"/> of a transcript document, and why.</summary>
public sealed record CutSuggestion(int First, int Last, string Reason)
{
    public int Count => Last - First + 1;

    public string Words(TranscriptDocument document) =>
        string.Join(" ", document.Entries.Skip(First).Take(Count).Select(e => e.Word.Text));

    /// <summary>Where it is on the timeline: the start of the first word.</summary>
    public double TimelineStart(Project project, Layer layer, TranscriptDocument document)
    {
        var entry = document.Entries[First];
        var item = layer.Items[entry.ItemIndex];
        return Timeline.Span(layer, entry.ItemIndex).Start + Math.Max(0, item.ToTimeline(entry.Word.Start));
    }

    /// <summary>"um, at 1 minute 3 seconds: filler word".</summary>
    public string Spoken(Project project, Layer layer, TranscriptDocument document) =>
        $"\"{Words(document)}\", at {TimeFormat.Words(TimelineStart(project, layer, document))}: {Reason}";
}

/// <summary>
/// Finding what to cut from a transcript without any online service: filler sounds, a word said twice,
/// a phrase started again ("I went to the, I went to the shop"), and stray symbols.
/// </summary>
public static partial class TranscriptCleanup
{
    /// <summary>Sounds that are never words (in any of the supported languages).</summary>
    public static IReadOnlySet<string> Fillers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "um", "umm", "ummm", "uh", "uhh", "uhm", "er", "erm", "ehm", "ah", "ahh", "hmm", "hm", "mm", "mmm", "mhm",
        "अं", "उम्म", "हम्म", "اوں", "ام",
    };

    /// <summary>All the suggestions, in order, never overlapping.</summary>
    public static IReadOnlyList<CutSuggestion> Find(TranscriptDocument document, bool fillers = true, bool repeats = true, bool symbols = true)
    {
        var words = document.Entries.Select(e => TranscriptEditing.Normalize(e.Word.Text)).ToList();
        var found = new List<CutSuggestion>();
        var i = 0;
        while (i < words.Count)
        {
            var raw = document.Entries[i].Word.Text;
            if (symbols && IsSymbolOnly(raw))
            {
                found.Add(new CutSuggestion(i, i, "symbol, not a word"));
                i++;
                continue;
            }

            if (fillers && Fillers.Contains(words[i]))
            {
                found.Add(new CutSuggestion(i, i, "filler sound"));
                i++;
                continue;
            }

            if (repeats && RepeatAt(words, i) is { } length)
            {
                // Keep the last time it was said: the speaker usually gets it right on the second go.
                found.Add(new CutSuggestion(i, i + length - 1, length == 1 ? "word said twice" : "phrase said again"));
                i += length;
                continue;
            }

            i++;
        }

        return found;
    }

    /// <summary>
    /// The length of the longest phrase (1 to 8 words) starting at <paramref name="i"/> that is said again
    /// straight after (allowing one filler in between), or null. A single repeated word counts only when it
    /// is not a word people really say twice ("very very", "no no").
    /// </summary>
    private static int? RepeatAt(IReadOnlyList<string> words, int i)
    {
        for (var length = Math.Min(8, (words.Count - i) / 2); length >= 1; length--)
        {
            var next = i + length;
            if (next < words.Count && Fillers.Contains(words[next]))
            {
                next++;
            }

            if (next + length > words.Count)
            {
                continue;
            }

            var same = true;
            for (var k = 0; k < length && same; k++)
            {
                same = words[i + k].Length > 0 && words[i + k] == words[next + k];
            }

            if (same && (length > 1 || !DoubledOnPurpose.Contains(words[i])))
            {
                return length;
            }
        }

        return null;
    }

    private static readonly HashSet<string> DoubledOnPurpose = new(StringComparer.OrdinalIgnoreCase)
    {
        "very", "no", "yes", "bye", "really", "so", "ha", "haha", "knock", "had", "that", "bas", "chalo", "haan", "nahi",
    };

    private static bool IsSymbolOnly(string text) => text.Length > 0 && !text.Any(char.IsLetterOrDigit);

    // ------------------------------------------------------------------ files

    /// <summary>The transcript as plain text, one sentence per line: easy to edit in any editor or AI chat.</summary>
    public static string ExportText(TranscriptDocument document) => document.Text.ReplaceLineEndings("\r\n") + "\r\n";

    /// <summary>The words as SubRip (.srt) or WebVTT (.vtt) captions, in timeline time.</summary>
    public static string ExportCaptions(Project project, Layer layer, bool webVtt)
    {
        var lines = Captions.Lines(Captions.WordsOnTimeline(project, layer));
        var text = new StringBuilder();
        if (webVtt)
        {
            text.Append("WEBVTT\r\n\r\n");
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (!webVtt)
            {
                text.Append(i + 1).Append("\r\n");
            }

            text.Append(Stamp(lines[i].Start, webVtt)).Append(" --> ").Append(Stamp(lines[i].End, webVtt)).Append("\r\n");
            text.Append(lines[i].Text).Append("\r\n\r\n");
        }

        return text.ToString();
    }

    private static string Stamp(double seconds, bool webVtt)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{(webVtt ? '.' : ',')}{t.Milliseconds:000}";
    }

    /// <summary>
    /// Reads an edited transcript back: plain text as it is, or captions (.srt/.vtt) without their numbers
    /// and times. The result can be compared with the transcript to find what was deleted or corrected.
    /// </summary>
    public static string ImportText(string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        if (!lines.Any(l => l.Contains("-->", StringComparison.Ordinal)))
        {
            return content.Trim();
        }

        var text = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line == "WEBVTT" || line.Contains("-->", StringComparison.Ordinal) || CueNumber().IsMatch(line) ||
                line.StartsWith("NOTE", StringComparison.Ordinal))
            {
                continue;
            }

            text.Append(TagPattern().Replace(line, "")).Append('\n');
        }

        return text.ToString().Trim();
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex CueNumber();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagPattern();
}
