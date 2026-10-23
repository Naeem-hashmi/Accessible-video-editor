using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

public enum QuickStyle
{
    /// <summary>Vertical 9:16, quick cuts on the beat, whoosh on cuts, captions.</summary>
    Reel,

    /// <summary>Landscape 16:9, short crossfades, title and end card.</summary>
    YouTube,

    /// <summary>Landscape photos with slow crossfades, timed to the music.</summary>
    Slideshow,

    /// <summary>A person speaking: pauses shortened, music low under the voice, captions.</summary>
    Talking,
}

/// <summary>What the user chose in the three steps, plus what the program measured.</summary>
public sealed class QuickVideoRequest
{
    public required IReadOnlyList<MediaInfo> Files { get; init; }

    public QuickStyle Style { get; init; }

    public string Title { get; init; } = "";

    public MediaInfo? Music { get; init; }

    /// <summary>Beats of the music, found by <see cref="BeatDetector"/>.</summary>
    public BeatAnalysis? MusicBeats { get; init; }

    /// <summary>Speech found in each clip with sound (file path to analysis, file time).</summary>
    public IReadOnlyDictionary<string, SpeechAnalysis> Speech { get; init; } = new Dictionary<string, SpeechAnalysis>();

    /// <summary>Transcripts, for captions.</summary>
    public IReadOnlyList<Transcript> Transcripts { get; init; } = [];

    /// <summary>A whoosh recording to play on cuts in Reel style, if available.</summary>
    public string? WhooshPath { get; init; }

    public double WhooshLength { get; init; } = 0.6;

    public bool AddCaptions { get; init; } = true;
}

/// <summary>The settings each style uses. Fixed rules, so the result is predictable and can be explained.</summary>
public sealed record QuickStyleRules(
    QuickStyle Style,
    string Name,
    int Width,
    int Height,
    double PictureSeconds,
    string Transition,
    double TransitionSeconds,
    int BeatsPerPicture,
    double TitleSeconds,
    bool EndCard,
    double MusicVolume,
    bool ShortenPauses,
    bool Whoosh)
{
    public static QuickStyleRules For(QuickStyle style) => style switch
    {
        QuickStyle.Reel => new(style, "Reel", 1080, 1920, 1.5, TransitionSpec.None, 0, 2, 2, false, 0.7, true, true),
        QuickStyle.YouTube => new(style, "YouTube", 1920, 1080, 4, "fade", 0.5, 4, 3, true, 0.5, false, false),
        QuickStyle.Slideshow => new(style, "Slideshow", 1920, 1080, 4, "fade", 1.0, 8, 3, false, 0.8, false, false),
        _ => new(style, "Talking video", 1920, 1080, 4, TransitionSpec.None, 0, 0, 2.5, false, 0.35, true, false),
    };

    /// <summary>What the style does, in plain words, for the wizard to read out.</summary>
    public string Spoken => Style switch
    {
        QuickStyle.Reel => "Reel: vertical for Reels, Shorts and TikTok. Quick cuts on the beat with a whoosh, captions, and the title at the start.",
        QuickStyle.YouTube => "YouTube: landscape. Short crossfades between clips, a title at the start and a thank-you card at the end.",
        QuickStyle.Slideshow => "Slideshow: landscape. Photos change on the music's beat with slow crossfades.",
        _ => "Talking video: long pauses shortened, music kept low under the voice, and captions.",
    };
}

/// <summary>
/// Makes a finished, ordinary project from files in one go: order, cleanup, timing, transitions, title,
/// music, ducking, beat sync, captions and loudness. Every choice is a normal item the user can change.
/// </summary>
public static class QuickVideo
{
    public static (Project Project, string Summary) Build(QuickVideoRequest request)
    {
        var rules = QuickStyleRules.For(request.Style);
        var project = new Project
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Quick video" : request.Title.Trim(),
            Width = rules.Width,
            Height = rules.Height,
            FrameRate = 30,
            MasterLoudness = -14,
        };
        var notes = new List<string>();

        // 1. Pictures and clips in file-name order (camera and phone names count up), numbers compared as numbers.
        var files = request.Files.Where(f => f.Kind is ItemKind.Video or ItemKind.Image).OrderBy(f => Path.GetFileName(f.Path), NaturalComparer.Instance).ToList();
        var main = ProjectEditor.AddLayer(project, LayerKind.Video, "Video");
        var shortened = 0.0;
        foreach (var file in files)
        {
            var item = MediaProbe.CreateItem(file, main.Defaults, LayerKind.Video);
            if (item.Kind == ItemKind.Image)
            {
                item.Duration = rules.PictureSeconds;
            }

            // Fill the frame (cropping the edges) so vertical reels have no black bars.
            item.Placement = new Placement { Position = PositionPreset.FullFrame, Fill = true };

            if (rules.Transition != TransitionSpec.None && main.Items.Count > 0)
            {
                item.TransitionIn = new TransitionSpec { Video = rules.Transition, Audio = "tri", Duration = rules.TransitionSeconds };
            }

            main.Items.Add(item);

            // 2. Speech clips: trim silence at both ends, and shorten long pauses for talking styles.
            if (item.Kind == ItemKind.Video && request.Speech.TryGetValue(file.Path, out var speech) && speech.Sound.Count > 0)
            {
                var before = main.Items.Sum(i => i.Duration);
                var index = main.Items.Count - 1;
                var cuts = rules.ShortenPauses
                    ? CutEditing.PauseCuts(speech, keep: 0.35, longerThan: 0.8)
                    : CutEditing.PauseCuts(speech, keep: 0.3, longerThan: 0.8).Where(c => c.Start <= 1e-6 || c.End >= speech.Length - 1e-6).ToList();
                if (cuts.Count > 0)
                {
                    var transition = item.TransitionIn;
                    if (CutEditing.RemoveSourceRanges(main, index, cuts, project.FrameRate, "pauses").Ok)
                    {
                        main.Items[index].TransitionIn = transition;
                    }
                }

                shortened += before - main.Items.Sum(i => i.Duration);
            }
        }

        if (main.Items.Count == 0)
        {
            return (project, "No pictures or videos were chosen, so there is nothing to make.");
        }

        if (shortened > 0.5)
        {
            notes.Add($"removed {TimeFormat.Words(shortened)} of silence");
        }

        // 3. Music: under everything, repeated to the end if it is short, faded out.
        Layer? music = null;
        if (request.Music is { } song)
        {
            music = ProjectEditor.AddLayer(project, LayerKind.Audio, "Music");
            var item = MediaProbe.CreateItem(song, music.Defaults, LayerKind.Audio);
            item.Volume = rules.MusicVolume;
            music.Items.Add(item);
            // Talking videos never snap to beats: that could cut off the end of a sentence.
            if (request.MusicBeats is { HasSteadyBeat: true } beats && rules.BeatsPerPicture > 0)
            {
                // Start the music on its first beat, so the beat grid begins with the video.
                if (beats.Beats[0] < Math.Min(2.0, item.Duration / 2))
                {
                    item.SourceIn = beats.Beats[0];
                    item.Duration -= beats.Beats[0];
                }

                BeatEditing.SetBeats(music, 0, beats);

                // 4. Beat sync: each picture lasts the style's number of beats; clips end on the nearest beat.
                foreach (var picture in main.Items.Where(i => i.Kind == ItemKind.Image))
                {
                    picture.Duration = rules.BeatsPerPicture * 60 / beats.Bpm;
                }

                BeatEditing.CutToBeats(project, main, rules.BeatsPerPicture);
                notes.Add($"cut to the beat at {Math.Round(beats.Bpm):0} beats per minute");
            }
        }

        // 5. Title at the start, and an end card for YouTube.
        var titleLayer = ProjectEditor.AddLayer(project, LayerKind.Video, "Title");
        var titleStyle = new TextStyle
        {
            Content = project.Title,
            SizePercent = rules.Height > rules.Width ? 7 : 9,
            Bold = true,
            Outline = true,
            Background = TextBackground.None,
            AnimationIn = TextAnimation.Rise,
            AnimationOut = TextAnimation.Fade,
        };
        titleLayer.Items.Add(MediaProbe.CreateTextItem(titleStyle, rules.TitleSeconds, titleLayer.Defaults, new Placement { Position = PositionPreset.Center }));
        if (rules.EndCard)
        {
            var end = Timeline.LayerEnd(main);
            var card = titleStyle.Clone();
            card.Content = "Thanks for watching";
            card.AnimationIn = TextAnimation.Fade;
            var endItem = MediaProbe.CreateTextItem(card, Math.Min(3, end / 3), titleLayer.Defaults, new Placement { Position = PositionPreset.Center });
            endItem.GapBefore = Math.Max(0, end - endItem.Duration - Timeline.LayerEnd(titleLayer));
            titleLayer.Items.Add(endItem);
        }

        // 6. Whoosh on each cut (Reel).
        if (rules.Whoosh && request.WhooshPath is { } whoosh && main.Items.Count > 1)
        {
            var effects = ProjectEditor.AddLayer(project, LayerKind.Audio, "Effects");
            var spans = Timeline.Spans(main);
            var cursor = 0.0;
            for (var i = 1; i < spans.Count; i++)
            {
                // Centred on the cut, so the swish passes as the picture changes.
                var start = Math.Max(cursor, spans[i].Start - request.WhooshLength / 2);
                var effect = new Item
                {
                    Kind = ItemKind.Audio, Path = whoosh, Name = Path.GetFileNameWithoutExtension(whoosh), HasAudio = true,
                    Duration = request.WhooshLength, SourceDuration = request.WhooshLength, Volume = 0.6, GapBefore = start - cursor,
                };
                effects.Items.Add(effect);
                cursor = start + effect.Duration;
            }

            notes.Add("a whoosh on every cut");
        }

        // The music lasts exactly as long as the video, repeating if needed, with a 2 second fade out.
        var length = Timeline.LayerEnd(main);
        if (music is not null)
        {
            var first = music.Items[0];
            var available = first.AvailableDuration;
            var remaining = length;
            music.Items.Clear();
            while (remaining > 0.05 && music.Items.Count < 50)
            {
                var piece = first.Clone();
                piece.Duration = Math.Min(remaining, double.IsInfinity(available) ? remaining : available);
                music.Items.Add(piece);
                remaining -= piece.Duration;
            }

            music.Items[^1].FadeOut = Math.Min(2, music.Items[^1].Duration / 2);

            // 7. Ducking: music lower wherever someone speaks.
            var speech = new List<TimeStretch>();
            var spans = Timeline.Spans(main);
            for (var i = 0; i < main.Items.Count; i++)
            {
                var item = main.Items[i];
                if (request.Speech.TryGetValue(item.Path, out var analysis))
                {
                    speech.AddRange(analysis.Sound
                        .Select(s => new TimeStretch(Math.Max(s.Start, item.SourceIn), Math.Min(s.End, item.SourceIn + item.Duration)))
                        .Where(s => s.End > s.Start)
                        .Select(s => new TimeStretch(spans[i].Start + s.Start - item.SourceIn, spans[i].Start + s.End - item.SourceIn)));
                }
            }

            if (speech.Count > 0)
            {
                Ducking.Apply(music, speech, level: 0.25);
                notes.Add("music lowered under speech");
            }
        }

        // 8. Captions from the transcripts.
        project.Transcripts.AddRange(request.Transcripts.Select(t => t.Clone()));
        if (request.AddCaptions && request.Transcripts.Count > 0 && Captions.Create(project, main).Ok)
        {
            notes.Add("captions");
        }

        var count = main.Items.Count;
        var summary = $"Made a {TimeFormat.Words(length)} {rules.Name.ToLowerInvariant()} from {files.Count} {(files.Count == 1 ? "file" : "files")}, " +
                      $"{project.Width} by {project.Height}" +
                      (notes.Count > 0 ? ", with " + string.Join(", ", notes) : "") +
                      $". It plays at -14 LUFS, the loudness social sites expect. Everything can still be changed: {count} {(count == 1 ? "item" : "items")} on the Video layer.";
        return (project, summary);
    }

    /// <summary>Measures the files first: speech in clips with sound and beats in the music.</summary>
    public static QuickVideoRequest Analyse(FfmpegTools tools, QuickVideoRequest request, CancellationToken token = default)
    {
        var speech = new Dictionary<string, SpeechAnalysis>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in request.Files.Where(f => f.Kind == ItemKind.Video && f.HasAudio))
        {
            token.ThrowIfCancellationRequested();
            speech[file.Path] = SpeechDetector.Analyse(tools, file.Path, 0, file.Duration, audioOffset: file.AudioOffset);
        }

        var beats = request.Music is { } music ? BeatDetector.Analyse(tools, music.Path, 0, Math.Min(600, music.Duration)) : null;
        return new QuickVideoRequest
        {
            Files = request.Files,
            Style = request.Style,
            Title = request.Title,
            Music = request.Music,
            MusicBeats = beats,
            Speech = speech,
            Transcripts = request.Transcripts,
            WhooshPath = request.WhooshPath,
            WhooshLength = request.WhooshLength,
            AddCaptions = request.AddCaptions,
        };
    }

    /// <summary>Compares file names so "clip 2" comes before "clip 10".</summary>
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            x ??= "";
            y ??= "";
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    var si = i;
                    var sj = j;
                    while (i < x.Length && char.IsDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && char.IsDigit(y[j]))
                    {
                        j++;
                    }

                    var a = x[si..i].TrimStart('0');
                    var b = y[sj..j].TrimStart('0');
                    var byLength = a.Length.CompareTo(b.Length);
                    if (byLength != 0)
                    {
                        return byLength;
                    }

                    var byDigits = string.CompareOrdinal(a, b);
                    if (byDigits != 0)
                    {
                        return byDigits;
                    }
                }
                else
                {
                    var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                    if (c != 0)
                    {
                        return c;
                    }

                    i++;
                    j++;
                }
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }
    }
}
