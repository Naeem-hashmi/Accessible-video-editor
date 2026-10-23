using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>
/// Auto-ducking: makes music quieter wherever someone speaks. Speech is found by loudness on the other
/// layers; the music gets ordinary <see cref="VolumeDip"/>s, so the result can be heard, changed or removed.
/// </summary>
public static class Ducking
{
    /// <summary>Pauses in speech shorter than this keep the music down, so it doesn't pump between words.</summary>
    public const double BridgeGap = 0.8;

    /// <summary>
    /// Where speech plays on the timeline, from every unmuted layer except <paramref name="musicLayer"/>.
    /// Runs FFmpeg once per clip.
    /// </summary>
    public static IReadOnlyList<TimeStretch> FindSpeech(FfmpegTools tools, Project project, Layer musicLayer, CancellationToken token = default)
    {
        var stretches = new List<TimeStretch>();
        foreach (var layer in project.Layers.Where(l => l.Id != musicLayer.Id && !l.Muted))
        {
            var spans = Timeline.Spans(layer);
            for (var i = 0; i < layer.Items.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var item = layer.Items[i];
                if (!item.HasAudio || item.AudioMuted || item.Volume <= 0 || item.Kind is not (ItemKind.Video or ItemKind.Audio) ||
                    !File.Exists(item.Path))
                {
                    continue;
                }

                var analysis = SpeechDetector.Analyse(tools, item.Path, item.SourceIn, item.Duration, minimumPause: 0.4);
                stretches.AddRange(analysis.Sound.Select(s => new TimeStretch(spans[i].Start + s.Start, Math.Min(spans[i].End, spans[i].Start + s.End))));
            }
        }

        return stretches;
    }

    /// <summary>
    /// Replaces the dips on every item of <paramref name="musicLayer"/> with dips under
    /// <paramref name="speech"/> (timeline seconds). <paramref name="level"/> is the music volume while
    /// someone speaks (0.25 is 12 dB quieter).
    /// </summary>
    public static EditResult Apply(Layer musicLayer, IEnumerable<TimeStretch> speech, double level = 0.25, double ramp = 0.3)
    {
        if (musicLayer.Items.Count == 0)
        {
            return EditResult.Fail($"{musicLayer.Name} is empty.");
        }

        var merged = new List<TimeStretch>();
        foreach (var s in speech.Where(s => s.Duration > 0).OrderBy(s => s.Start))
        {
            if (merged.Count > 0 && s.Start - merged[^1].End < BridgeGap)
            {
                merged[^1] = new TimeStretch(merged[^1].Start, Math.Max(merged[^1].End, s.End));
            }
            else
            {
                merged.Add(s);
            }
        }

        var spans = Timeline.Spans(musicLayer);
        var dips = 0;
        for (var i = 0; i < musicLayer.Items.Count; i++)
        {
            var item = musicLayer.Items[i];
            item.VolumeDips.Clear();
            foreach (var s in merged)
            {
                var start = Math.Max(s.Start, spans[i].Start);
                var end = Math.Min(s.End, spans[i].End);
                if (end <= start)
                {
                    continue;
                }

                item.VolumeDips.Add(new VolumeDip
                {
                    Start = item.SourceIn + (start - spans[i].Start),
                    End = item.SourceIn + (end - spans[i].Start),
                    Level = level,
                    Ramp = ramp,
                });
                dips++;
            }
        }

        var db = 20 * Math.Log10(Math.Max(level, 1e-4));
        return dips == 0
            ? EditResult.Success($"No speech found, so {musicLayer.Name} stays at full volume.")
            : EditResult.Success($"{musicLayer.Name} lowered by {Math.Round(-db):0} decibels in {dips} {(dips == 1 ? "place" : "places")} where someone speaks.");
    }

    /// <summary>Removes all ducking from a layer.</summary>
    public static EditResult Clear(Layer musicLayer)
    {
        var count = musicLayer.Items.Sum(i => i.VolumeDips.Count);
        foreach (var item in musicLayer.Items)
        {
            item.VolumeDips.Clear();
        }

        return count == 0
            ? EditResult.Fail($"{musicLayer.Name} has no ducking.")
            : EditResult.Success($"Ducking removed from {musicLayer.Name}: full volume everywhere.");
    }
}
