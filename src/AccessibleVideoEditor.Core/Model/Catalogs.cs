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
