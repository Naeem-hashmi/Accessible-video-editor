using AccessibleVideoEditor.Core.Audio;

namespace AccessibleVideoEditor.Core.Speech;

/// <summary>
/// Makes Whisper's word times fit the sound. Whisper sometimes gives a word no length, or stretches a
/// word (often one ending a sentence) across the following pause. Cutting by words needs edges that sit
/// where the speech really starts and stops.
/// </summary>
public static class TranscriptTiming
{
    public static void Refine(List<TranscriptWord> words, SpeechAnalysis? analysis)
    {
        ShareZeroLengthWords(words);
        if (analysis is not null)
        {
            foreach (var word in words)
            {
                FitToSound(word, analysis.Pauses);
            }
        }

        // Keep words in order and never overlapping.
        for (var i = 1; i < words.Count; i++)
        {
            if (words[i].Start < words[i - 1].End)
            {
                words[i].Start = Math.Min(words[i].End, words[i - 1].End);
            }
        }
    }

    /// <summary>Words with (almost) no length share the time of the word before them, by number of letters.</summary>
    internal static void ShareZeroLengthWords(List<TranscriptWord> words)
    {
        var i = 0;
        while (i < words.Count)
        {
            var j = i + 1;
            while (j < words.Count && words[j].End - words[j].Start < 0.05)
            {
                j++;
            }

            if (j - i > 1)
            {
                // words[i] has a real length; words[i+1 .. j-1] have none. Share from words[i].Start to the
                // furthest end among them (or the next word's start).
                var from = words[i].Start;
                var to = Math.Max(words[i].End, words[j - 1].End);
                if (j < words.Count)
                {
                    to = Math.Max(to, Math.Min(words[j].Start, from + 0.4 * (j - i)));
                }

                var letters = words.Skip(i).Take(j - i).Sum(w => Math.Max(1, w.Text.Length));
                var cursor = from;
                for (var k = i; k < j; k++)
                {
                    var share = (to - from) * Math.Max(1, words[k].Text.Length) / letters;
                    words[k].Start = cursor;
                    words[k].End = cursor + share;
                    cursor += share;
                }
            }

            i = j;
        }
    }

    private static void FitToSound(TranscriptWord word, IReadOnlyList<TimeStretch> pauses)
    {
        foreach (var pause in pauses)
        {
            if (pause.End <= word.Start || pause.Start >= word.End)
            {
                continue;
            }

            if (pause.Start <= word.Start && pause.End >= word.End)
            {
                continue; // the whole word is "quiet": leave it, it may be a whisper
            }

            if (pause.Start <= word.Start)
            {
                word.Start = pause.End;
            }
            else if (pause.End >= word.End)
            {
                word.End = pause.Start;
            }
            else if (pause.Start - word.Start >= word.End - pause.End)
            {
                // A pause inside the word: keep the side with more sound.
                word.End = pause.Start;
            }
            else
            {
                word.Start = pause.End;
            }
        }
    }
}
