namespace AccessibleVideoEditor.Core.Model;

/// <summary>A choice shown to the user: an FFmpeg value with a plain-language name and description.</summary>
public sealed record CatalogEntry(string Value, string Name, string Description);

/// <summary>
/// Transitions the user can choose. Values are FFmpeg <c>xfade</c> transitions and <c>acrossfade</c> curves;
/// all of them exist in FFmpeg 6.1 and later.
/// </summary>
public static class TransitionCatalog
{
    public static IReadOnlyList<CatalogEntry> Video { get; } = new CatalogEntry[]
    {
        new(TransitionSpec.None, "No transition (cut)", "The next item starts straight away."),
        new("fade", "Cross fade", "The pictures blend smoothly into each other."),
        new("fadeblack", "Fade through black", "Fades to black, then up into the next item."),
        new("fadewhite", "Fade through white", "Fades to white, then into the next item. Good for flashbacks."),
        new("fadegrays", "Fade through grey", "Colour drains out, then the next item fades in."),
        new("dissolve", "Dissolve", "The next item appears dot by dot."),
        new("fadeslow", "Slow fade", "A cross fade that starts slowly."),
        new("fadefast", "Quick fade", "A cross fade that finishes quickly."),
        new("wipeleft", "Wipe left", "The next item wipes in from the right edge to the left."),
        new("wiperight", "Wipe right", "The next item wipes in from the left edge to the right."),
        new("wipeup", "Wipe up", "The next item wipes in from the bottom up."),
        new("wipedown", "Wipe down", "The next item wipes in from the top down."),
        new("slideleft", "Slide left", "The next item slides in from the right, pushing the old one out."),
        new("slideright", "Slide right", "The next item slides in from the left."),
        new("slideup", "Slide up", "The next item slides in from the bottom."),
        new("slidedown", "Slide down", "The next item slides in from the top."),
        new("coverleft", "Cover left", "The next item slides over the old one from the right."),
        new("coverright", "Cover right", "The next item slides over the old one from the left."),
        new("revealleft", "Reveal left", "The old item slides away to the left, revealing the next."),
        new("revealright", "Reveal right", "The old item slides away to the right, revealing the next."),
        new("smoothleft", "Smooth wipe left", "A soft-edged wipe from right to left."),
        new("smoothright", "Smooth wipe right", "A soft-edged wipe from left to right."),
        new("circleopen", "Circle open", "A growing circle in the centre reveals the next item."),
        new("circleclose", "Circle close", "A shrinking circle closes onto the next item."),
        new("circlecrop", "Circle crop", "Closes to a circle, then opens on the next item."),
        new("rectcrop", "Box crop", "Closes to a rectangle, then opens on the next item."),
        new("radial", "Clock wipe", "The next item sweeps in like a clock hand."),
        new("horzopen", "Barn doors open", "Opens from the middle outwards, side to side."),
        new("horzclose", "Barn doors close", "Closes from the sides into the middle."),
        new("vertopen", "Vertical open", "Opens from the middle outwards, top and bottom."),
        new("vertclose", "Vertical close", "Closes from top and bottom into the middle."),
        new("diagtl", "Diagonal from top left", "A diagonal wipe from the top left corner."),
        new("diagbr", "Diagonal from bottom right", "A diagonal wipe from the bottom right corner."),
        new("pixelize", "Pixelate", "The picture breaks into blocks, then sharpens on the next item."),
        new("hblur", "Blur", "A horizontal blur between the two items."),
        new("zoomin", "Zoom in", "Zooms into the old item while the next appears."),
        new("squeezeh", "Squeeze horizontal", "The old item squeezes away sideways."),
        new("squeezev", "Squeeze vertical", "The old item squeezes away up and down."),
        new("hlslice", "Slices left", "Horizontal slices wipe from right to left."),
        new("vuslice", "Slices up", "Vertical slices wipe upwards."),
        new("hlwind", "Wind left", "A ragged, windy wipe to the left."),
    };

    public static IReadOnlyList<CatalogEntry> Audio { get; } = new CatalogEntry[]
    {
        new(TransitionSpec.None, "No audio transition (cut)", "The sound changes straight away."),
        new("tri", "Linear crossfade", "Even, straight-line fade. A safe default."),
        new("qsin", "Smooth crossfade", "Quarter sine curve. Keeps loudness steady; good for music."),
        new("hsin", "Gentle crossfade", "Half sine curve. Soft start and end."),
        new("esin", "Exponential sine crossfade", "Slow start, quick finish."),
        new("log", "Logarithmic crossfade", "The old sound drops quickly at first."),
        new("exp", "Exponential crossfade", "The old sound lingers, then drops away."),
        new("par", "Parabolic crossfade", "A rounded curve, slightly faster than linear."),
        new("qua", "Quadratic crossfade", "A curve that holds the old sound a little longer."),
        new("cub", "Cubic crossfade", "Holds the old sound longer than quadratic."),
        new("squ", "Square root crossfade", "The new sound comes in quickly."),
        new("cbr", "Cube root crossfade", "The new sound comes in very quickly."),
        new("losi", "Logistic crossfade", "S-shaped: slow, fast, then slow again."),
        new("nofade", "Hard switch", "No fade; switches at the end of the overlap."),
    };

    public static CatalogEntry FindVideo(string value) =>
        Video.FirstOrDefault(e => e.Value == value) ?? Video[0];

    public static CatalogEntry FindAudio(string value) =>
        Audio.FirstOrDefault(e => e.Value == value) ?? Audio[0];

    public static bool IsKnownVideo(string value) => Video.Any(e => e.Value == value);

    public static bool IsKnownAudio(string value) => Audio.Any(e => e.Value == value);
}

/// <summary>Named colours for colour cards. Names are spoken; hex values go to FFmpeg.</summary>
public static class ColorCatalog
{
    public static IReadOnlyList<(string Name, string Hex)> Colors { get; } = new[]
    {
        ("Black", "000000"),
        ("White", "FFFFFF"),
        ("Dark grey", "333333"),
        ("Light grey", "CCCCCC"),
        ("Red", "D32F2F"),
        ("Dark red", "7F0000"),
        ("Orange", "F57C00"),
        ("Yellow", "FBC02D"),
        ("Green", "388E3C"),
        ("Dark green", "1B5E20"),
        ("Teal", "00796B"),
        ("Light blue", "4FC3F7"),
        ("Blue", "1976D2"),
        ("Navy", "0D1B4C"),
        ("Purple", "7B1FA2"),
        ("Pink", "EC407A"),
        ("Brown", "5D4037"),
        ("Cream", "FFF8E1"),
    };

    public static string HexFor(string name) =>
        Colors.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).Hex ?? "000000";

    public static bool IsKnown(string name) =>
        Colors.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Fonts for text items. All of them ship with Windows 10 and 11. Elsewhere (development and tests) a
/// common free font is used instead.
/// </summary>
public static class FontCatalog
{
    public sealed record FontEntry(string Name, string Description, string File, string BoldFile);

    public static IReadOnlyList<FontEntry> Fonts { get; } = new FontEntry[]
    {
        new("Segoe UI", "Clean and modern, the Windows font. Good default.", "segoeui.ttf", "segoeuib.ttf"),
        new("Arial", "Plain and very readable.", "arial.ttf", "arialbd.ttf"),
        new("Calibri", "Soft and rounded.", "calibri.ttf", "calibrib.ttf"),
        new("Verdana", "Wide letters, readable at small sizes.", "verdana.ttf", "verdanab.ttf"),
        new("Trebuchet MS", "Friendly and informal.", "trebuc.ttf", "trebucbd.ttf"),
        new("Georgia", "Classic serif, like a book.", "georgia.ttf", "georgiab.ttf"),
        new("Times New Roman", "Traditional newspaper serif.", "times.ttf", "timesbd.ttf"),
        new("Impact", "Tall and heavy, for bold titles and memes.", "impact.ttf", "impact.ttf"),
        new("Comic Sans MS", "Playful handwriting style.", "comic.ttf", "comicbd.ttf"),
        new("Consolas", "Typewriter-like, every letter the same width.", "consola.ttf", "consolab.ttf"),
        new("Segoe Script", "Joined-up handwriting.", "segoesc.ttf", "segoescb.ttf"),
        new("Nirmala UI", "Supports Hindi and other Indian scripts.", "Nirmala.ttc", "NirmalaB.ttc"),
        new("Noto Naskh Arabic", "Urdu, Arabic and Persian, clear and joined. Comes with ClipScribe.", "NotoNaskhArabic-Regular.ttf", "NotoNaskhArabic-Bold.ttf"),
    };

    public static FontEntry Find(string name) =>
        Fonts.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Fonts[0];

    /// <summary>
    /// The font file to draw with, or null if no usable font exists. Looks in the Windows font folders,
    /// then AVE_FONT_DIR, then common Linux and macOS font folders.
    /// </summary>
    public static string? Resolve(string name, bool bold)
    {
        var entry = Find(name);
        var file = bold ? entry.BoldFile : entry.File;
        // Fonts that come with the program first, then the Windows font folders.
        var folders = new List<string> { Path.Combine(AppContext.BaseDirectory, "Fonts") };
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(windows))
        {
            folders.Add(windows);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
        {
            folders.Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
        }

        foreach (var folder in folders)
        {
            var path = Path.Combine(folder, file);
            if (File.Exists(path))
            {
                return path;
            }

            var regular = Path.Combine(folder, entry.File);
            if (File.Exists(regular))
            {
                return regular;
            }
        }

        var fallbacks = new List<string>();
        var custom = Environment.GetEnvironmentVariable("AVE_FONT_DIR");
        if (!string.IsNullOrWhiteSpace(custom))
        {
            fallbacks.Add(custom);
        }

        fallbacks.AddRange(["/usr/share/fonts", "/usr/local/share/fonts", "/Library/Fonts", "/System/Library/Fonts"]);
        foreach (var folder in fallbacks.Where(Directory.Exists))
        {
            var candidates = Directory.EnumerateFiles(folder, "*.ttf", SearchOption.AllDirectories).ToList();
            var preferred = bold ? "DejaVuSans-Bold.ttf" : "DejaVuSans.ttf";
            var match = candidates.FirstOrDefault(c => Path.GetFileName(c).Equals(preferred, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault(c => Path.GetFileName(c).Equals(file, StringComparison.OrdinalIgnoreCase))
                        ?? candidates.FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}

/// <summary>Plain-language names for text animations.</summary>
public static class TextAnimationCatalog
{
    public static string InName(TextAnimation a) => a switch
    {
        TextAnimation.None => "Appear at once",
        TextAnimation.Fade => "Fade in",
        TextAnimation.SlideFromLeft => "Slide in from the left",
        TextAnimation.SlideFromRight => "Slide in from the right",
        TextAnimation.SlideFromTop => "Drop in from the top",
        TextAnimation.SlideFromBottom => "Rise in from the bottom",
        TextAnimation.Rise => "Float up gently while fading in",
        TextAnimation.Typewriter => "Typewriter, letter by letter",
        _ => a.ToString(),
    };

    public static string OutName(TextAnimation a) => a switch
    {
        TextAnimation.None => "Disappear at once",
        TextAnimation.Fade => "Fade out",
        TextAnimation.SlideFromLeft => "Slide out to the left",
        TextAnimation.SlideFromRight => "Slide out to the right",
        TextAnimation.SlideFromTop => "Fly out to the top",
        TextAnimation.SlideFromBottom => "Drop out to the bottom",
        TextAnimation.Rise => "Float up gently while fading out",
        TextAnimation.Typewriter => "Fade out",
        _ => a.ToString(),
    };

    public static IReadOnlyList<TextAnimation> All { get; } = Enum.GetValues<TextAnimation>();

    public static string BackgroundName(TextBackground b) => b switch
    {
        TextBackground.DarkBox => "Dark box behind the text",
        TextBackground.LightBox => "Light box behind the text",
        _ => "No box",
    };
}
