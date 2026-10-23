using System.Globalization;
using System.Text;
using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Speech;

/// <summary>How edits in the transcript are used.</summary>
public enum TranscriptMode
{
    /// <summary>Deleting words cuts that part out of the video. Other changes are ignored.</summary>
    Cut,

    /// <summary>Changes only correct the words (for captions). The video is not touched.</summary>
    FixText,
}

/// <summary>One word as shown in a layer's transcript, and the clip it belongs to.</summary>
public sealed record TranscriptEntry(int ItemIndex, TranscriptWord Word);

/// <summary>A layer's transcript as plain text, one sentence per line, with each word's place.</summary>
public sealed record TranscriptDocument(string Text, IReadOnlyList<TranscriptEntry> Entries)
{
    public bool IsEmpty => Entries.Count == 0;
}

/// <summary>
/// Editing video by editing its transcript. The text is a normal document: read and select it with the
/// screen reader's usual keys, delete what should go, and the program works out which words were removed
/// (by comparing old and new words), turns them into cuts in file time and removes those stretches.
/// </summary>
public static class TranscriptEditing
{
    /// <summary>Most of a pause kept on each side of a cut, so speech doesn't run together.</summary>
    public const double KeepAroundCut = 0.12;

    public static TranscriptDocument Build(Project project, Layer layer)
    {
        var entries = new List<TranscriptEntry>();
        var text = new StringBuilder();
        for (var i = 0; i < layer.Items.Count; i++)
        {
            var item = layer.Items[i];
            if (item.Kind is not (ItemKind.Video or ItemKind.Audio) || project.TranscriptFor(item.Path) is not { } transcript)
            {
                continue;
            }

            foreach (var word in transcript.WordsBetween(item.SourceIn, item.SourceIn + item.Duration))
            {
                if (text.Length > 0 && text[^1] != '\n')
                {
                    text.Append(' ');
                }

                text.Append(word.Text);
                entries.Add(new TranscriptEntry(i, word));
                if (EndsSentence(word.Text))
                {
                    text.Append('\n');
                }
            }
        }

        return new TranscriptDocument(text.ToString().TrimEnd('\n'), entries);
    }

    /// <summary>
    /// Applies <paramref name="edited"/> text to the layer. In <see cref="TranscriptMode.Cut"/> mode deleted
    /// words are cut out of their clips; in <see cref="TranscriptMode.FixText"/> mode changed words replace
    /// the old ones in the transcript only.
    /// </summary>
    public static EditResult Apply(Project project, Layer layer, TranscriptDocument before, string edited, TranscriptMode mode)
    {
        var oldWords = before.Entries.Select(e => Normalize(e.Word.Text)).ToList();
        var newTokens = Tokenize(edited);
        var newWords = newTokens.Select(Normalize).ToList();
        var matches = Lcs(oldWords, newWords);

        if (mode == TranscriptMode.FixText)
        {
            return FixText(project, before, newTokens, matches);
        }

        // Between matched words, old words with nothing in their place were deleted. Old words with new
        // words in their place were changed (a spelling fix), which never cuts the video.
        var deleted = new List<int>();
        var changed = 0;
        var oldCursor = 0;
        var newCursor = 0;
        foreach (var (o, n) in matches.Append((oldWords.Count, newWords.Count)))
        {
            if (n == newCursor)
            {
                deleted.AddRange(Enumerable.Range(oldCursor, o - oldCursor));
            }
            else
            {
                changed += Math.Max(o - oldCursor, n - newCursor);
            }

            oldCursor = o + 1;
            newCursor = n + 1;
        }

        if (deleted.Count == 0)
        {
            return EditResult.Fail(changed > 0
                ? "In cut mode only deleting words changes the video. To correct spelling, switch to Fix text mode."
                : "No words were deleted.");
        }

        // Cuts per clip, in file time.
        var cutsByItem = new Dictionary<int, List<TimeStretch>>();
        var i = 0;
        while (i < deleted.Count)
        {
            var first = deleted[i];
            var last = first;
            var itemIndex = before.Entries[first].ItemIndex;
            while (i + 1 < deleted.Count && deleted[i + 1] == last + 1 && before.Entries[deleted[i + 1]].ItemIndex == itemIndex)
            {
                last = deleted[++i];
            }

            i++;
            var item = layer.Items[itemIndex];
            var previous = first > 0 && before.Entries[first - 1].ItemIndex == itemIndex ? before.Entries[first - 1].Word : null;
            var next = last + 1 < before.Entries.Count && before.Entries[last + 1].ItemIndex == itemIndex ? before.Entries[last + 1].Word : null;
            var firstWord = before.Entries[first].Word;
            var lastWord = before.Entries[last].Word;

            // Deleting the first (or last) words of a clip trims it from its start (or to its end).
            var start = previous is null
                ? item.SourceIn
                : previous.End + Math.Min(KeepAroundCut, Math.Max(0, firstWord.Start - previous.End) / 2);
            var end = next is null
                ? item.SourceIn + item.Duration
                : next.Start - Math.Min(KeepAroundCut, Math.Max(0, next.Start - lastWord.End) / 2);

            if (!cutsByItem.TryGetValue(itemIndex, out var list))
            {
                cutsByItem[itemIndex] = list = new List<TimeStretch>();
            }

            list.Add(new TimeStretch(start, end));
        }

        // Cut from the last clip backwards so earlier indexes stay valid.
        var lengthBefore = layer.Items.Sum(x => x.Duration);
        var focus = (Guid?)null;
        foreach (var (itemIndex, cuts) in cutsByItem.OrderByDescending(p => p.Key))
        {
            var item = layer.Items[itemIndex];
            var whole = cuts.Sum(c => c.Duration) >= item.Duration - CutEditing.MinimumPiece;
            if (whole)
            {
                layer.Items.RemoveAt(itemIndex);
                continue;
            }

            var result = CutEditing.RemoveSourceRanges(layer, itemIndex, cuts, project.FrameRate, "parts");
            if (result.Ok)
            {
                focus = result.FocusItemId;
            }
        }

        var removedSeconds = lengthBefore - layer.Items.Sum(x => x.Duration);

        var words = deleted.Count;
        var note = changed > 0 ? " Changed words were not used: use Fix text mode to correct spelling." : "";
        return EditResult.Success(
            $"Cut {words} {(words == 1 ? "word" : "words")} from {layer.Name}, {Text.TimeFormat.Words(removedSeconds)} shorter.{note}", focus);
    }

    private static EditResult FixText(Project project, TranscriptDocument before, IReadOnlyList<string> newTokens, IReadOnlyList<(int Old, int New)> matches)
    {
        var changes = 0;

        // Matched words may differ in punctuation or capitals: take the new spelling.
        foreach (var (o, n) in matches)
        {
            var word = before.Entries[o].Word;
            if (word.Text != newTokens[n])
            {
                word.Text = newTokens[n];
                changes++;
            }
        }

        // Between matched words, give the new words to the old word slots, keeping their times.
        var oldCursor = 0;
        var newCursor = 0;
        foreach (var (o, n) in matches.Append((before.Entries.Count, newTokens.Count)))
        {
            var slots = Enumerable.Range(oldCursor, o - oldCursor).ToList();
            var words = newTokens.Skip(newCursor).Take(n - newCursor).ToList();
            if (slots.Count > 0)
            {
                for (var k = 0; k < slots.Count; k++)
                {
                    var from = (int)Math.Round(k * words.Count / (double)slots.Count);
                    var to = (int)Math.Round((k + 1) * words.Count / (double)slots.Count);
                    var text = string.Join(" ", words.Skip(from).Take(to - from));
                    var word = before.Entries[slots[k]].Word;
                    if (word.Text != text)
                    {
                        word.Text = text;
                        changes++;
                    }
                }
            }
            else if (words.Count > 0)
            {
                // Added words have no time of their own: they join the word before (or after) them.
                var added = string.Join(" ", words);
                if (oldCursor > 0)
                {
                    before.Entries[oldCursor - 1].Word.Text += " " + added;
                    changes++;
                }
                else if (o < before.Entries.Count)
                {
                    before.Entries[o].Word.Text = added + " " + before.Entries[o].Word.Text;
                    changes++;
                }
            }

            oldCursor = o + 1;
            newCursor = n + 1;
        }

        // Words emptied by the fix (for example a removed "um") leave the transcript.
        foreach (var transcript in project.Transcripts)
        {
            transcript.Words.RemoveAll(w => w.Text.Length == 0);
        }

        return changes == 0
            ? EditResult.Fail("Nothing changed in the transcript.")
            : EditResult.Success($"Corrected {changes} {(changes == 1 ? "word" : "words")} in the transcript. The video is unchanged; update the captions to use them.");
    }

    public static IReadOnlyList<string> Tokenize(string text) =>
        text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Compares words without case and punctuation; keeps the marks of Urdu and Hindi letters.</summary>
    public static string Normalize(string word)
    {
        var builder = new StringBuilder();
        foreach (var c in word.Normalize(NormalizationForm.FormC))
        {
            var category = char.GetUnicodeCategory(c);
            if (char.IsLetterOrDigit(c) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>Longest common subsequence of two word lists, as matched index pairs in order.</summary>
    internal static IReadOnlyList<(int Old, int New)> Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        // Edits are usually in one place: match the identical start and end directly, and compare only
        // the middle, which keeps long transcripts fast and small.
        var prefix = 0;
        while (prefix < a.Count && prefix < b.Count && a[prefix] == b[prefix] && a[prefix].Length > 0)
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Count - prefix && suffix < b.Count - prefix && a[a.Count - 1 - suffix] == b[b.Count - 1 - suffix] &&
               a[a.Count - 1 - suffix].Length > 0)
        {
            suffix++;
        }

        var result = new List<(int, int)>();
        for (var k = 0; k < prefix; k++)
        {
            result.Add((k, k));
        }

        var middleA = a.Skip(prefix).Take(a.Count - prefix - suffix).ToList();
        var middleB = b.Skip(prefix).Take(b.Count - prefix - suffix).ToList();
        result.AddRange(LcsCore(middleA, middleB).Select(p => (p.Old + prefix, p.New + prefix)));
        for (var k = suffix; k > 0; k--)
        {
            result.Add((a.Count - k, b.Count - k));
        }

        return result;
    }

    private static IReadOnlyList<(int Old, int New)> LcsCore(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = a.Count;
        var m = b.Count;
        var table = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                table[i, j] = a[i] == b[j] && a[i].Length > 0 ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        var pairs = new List<(int, int)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y] && a[x].Length > 0)
            {
                pairs.Add((x++, y++));
            }
            else if (table[x + 1, y] >= table[x, y + 1])
            {
                x++;
            }
            else
            {
                y++;
            }
        }

        return pairs;
    }

    private static bool EndsSentence(string word) => word.Length > 0 && ".?!।۔؟".Contains(word[^1]);
}
