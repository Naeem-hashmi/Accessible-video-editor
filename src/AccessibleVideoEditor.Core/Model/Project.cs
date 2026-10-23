using System.Text.Json.Serialization;

namespace AccessibleVideoEditor.Core.Model;

/// <summary>What a layer holds. Video layers stack picture (lowest layer at the bottom); audio layers are mixed.</summary>
public enum LayerKind
{
    Video,
    Audio,
}

/// <summary>What an item on a layer is made of.</summary>
public enum ItemKind
{
    /// <summary>A video file (its picture and, if present, its sound).</summary>
    Video,

    /// <summary>A still picture shown for <see cref="Item.Duration"/> seconds.</summary>
    Image,

    /// <summary>An audio file.</summary>
    Audio,

    /// <summary>A solid colour card, generated, no file needed.</summary>
    Color,

    /// <summary>Words on screen: a title, caption or credit, drawn on a transparent background.</summary>
    Text,
}

/// <summary>How text arrives on screen and how it leaves.</summary>
public enum TextAnimation
{
    None,
    Fade,
    SlideFromLeft,
    SlideFromRight,
    SlideFromTop,
    SlideFromBottom,
    Rise,
    Typewriter,
}

/// <summary>The backing behind text, for readability over busy pictures.</summary>
public enum TextBackground
{
    None,
    DarkBox,
    LightBox,
}

/// <summary>Where a picture sits in the frame. Plain-language presets instead of pixel dragging.</summary>
public enum PositionPreset
{
    FullFrame,
    Center,
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    Custom,
}

public enum NoiseReduction
{
    Off,
    Light,
    Medium,
    Strong,
}

public enum HumFilter
{
    Off,
    Hz50,
    Hz60,
}

public enum VoicePreset
{
    None,
    Clear,
    Warm,
    Broadcast,
}

/// <summary>An editing project: a canvas, layers (index 0 is layer 1, the bottom), markers and a start/end range.</summary>
public sealed class Project
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Title { get; set; } = "Untitled project";

    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public double FrameRate { get; set; } = 30;

    public List<Layer> Layers { get; set; } = new();

    /// <summary>Named markers, kept sorted by time.</summary>
    public List<Marker> Markers { get; set; } = new();

    /// <summary>Range start, set with the left bracket key.</summary>
    public double? RangeStart { get; set; }

    /// <summary>Range end, set with the right bracket key.</summary>
    public double? RangeEnd { get; set; }

    /// <summary>Credits for music and sounds that need attribution (for example CC BY), written next to exports.</summary>
    public List<ProjectCredit> Credits { get; set; } = new();

    /// <summary>
    /// When set, exports level the whole mix to this loudness in LUFS (for example -14, what YouTube,
    /// Instagram and TikTok play at). Live preview skips it.
    /// </summary>
    public double? MasterLoudness { get; set; }

    /// <summary>Transcripts of the media files, one per file, in file time.</summary>
    public List<Speech.Transcript> Transcripts { get; set; } = new();

    public Speech.Transcript? TranscriptFor(string mediaPath) =>
        Transcripts.FirstOrDefault(t => string.Equals(t.MediaPath, mediaPath, StringComparison.OrdinalIgnoreCase));

    public Layer? FindLayer(Guid id) => Layers.FirstOrDefault(l => l.Id == id);

    public int LayerIndexOf(Guid id) => Layers.FindIndex(l => l.Id == id);
}

public sealed class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    public LayerKind Kind { get; set; }

    public bool Muted { get; set; }

    /// <summary>Layer volume, 1.0 = 100 percent.</summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>Items play one after another, in this order.</summary>
    public List<Item> Items { get; set; } = new();

    /// <summary>Settings given to new items added to this layer (set by templates or the user).</summary>
    public LayerDefaults Defaults { get; set; } = new();

    public int IndexOf(Guid itemId) => Items.FindIndex(i => i.Id == itemId);
}

public sealed class LayerDefaults
{
    public TransitionSpec Transition { get; set; } = new();

    public double ImageDuration { get; set; } = 5;

    public Placement Placement { get; set; } = new();

    public double Volume { get; set; } = 1.0;

    public AudioCleanup Audio { get; set; } = new();

    public LayerDefaults Clone() => new()
    {
        Transition = Transition.Clone(),
        ImageDuration = ImageDuration,
        Placement = Placement.Clone(),
        Volume = Volume,
        Audio = Audio.Clone(),
    };
}

public sealed class Item
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ItemKind Kind { get; set; }

    /// <summary>Media file path. Empty for colour cards.</summary>
    public string Path { get; set; } = "";

    /// <summary>Spoken name, defaults to the file name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Colour card colour, a name from <see cref="ColorCatalog"/>.</summary>
    public string ColorName { get; set; } = "Black";

    /// <summary>Where in the source file this item starts, in seconds.</summary>
    public double SourceIn { get; set; }

    /// <summary>How long the item plays on the timeline, in seconds.</summary>
    public double Duration { get; set; }

    /// <summary>Full length of the source file, 0 when unlimited (images, colour cards).</summary>
    public double SourceDuration { get; set; }

    public bool HasVideo { get; set; }

    public bool HasAudio { get; set; }

    public int SourceWidth { get; set; }

    public int SourceHeight { get; set; }

    /// <summary>How much later the file's sound starts than its picture (from <see cref="Media.MediaInfo.AudioOffset"/>).</summary>
    public double AudioOffset { get; set; }

    /// <summary>Silence (black or transparent) before this item starts, in seconds.</summary>
    public double GapBefore { get; set; }

    /// <summary>Item volume, 1.0 = 100 percent.</summary>
    public double Volume { get; set; } = 1.0;

    public bool AudioMuted { get; set; }

    public double FadeIn { get; set; }

    public double FadeOut { get; set; }

    /// <summary>
    /// A very short sound-only fade at both ends (a few milliseconds), so joins made by cutting out pauses
    /// or words don't click. The picture is not faded.
    /// </summary>
    public double SoundJoinFade { get; set; }

    /// <summary>Transition from the previous item into this one.</summary>
    public TransitionSpec TransitionIn { get; set; } = new();

    public Placement Placement { get; set; } = new();

    public AudioCleanup Audio { get; set; } = new();

    /// <summary>Words and style for text items.</summary>
    public TextStyle Text { get; set; } = new();

    /// <summary>Beat times found in the source file, in source seconds (so trimming never moves them).</summary>
    public List<double> Beats { get; set; } = new();

    /// <summary>Tempo of the source music, or 0 when unknown.</summary>
    public double Bpm { get; set; }

    /// <summary>Places where this item's sound is lowered, for example under speech (auto-ducking).</summary>
    public List<VolumeDip> VolumeDips { get; set; } = new();

    /// <summary>The maximum duration the source allows from <see cref="SourceIn"/>, or infinity.</summary>
    [JsonIgnore]
    public double AvailableDuration =>
        SourceDuration > 0 ? Math.Max(0, SourceDuration - SourceIn) : double.PositiveInfinity;

    [JsonIgnore]
    public bool IsTimeLimited => Kind is ItemKind.Video or ItemKind.Audio && SourceDuration > 0;

    public Item Clone(bool newId = true)
    {
        var copy = (Item)MemberwiseClone();
        if (newId)
        {
            copy.Id = Guid.NewGuid();
        }

        copy.TransitionIn = TransitionIn.Clone();
        copy.Placement = Placement.Clone();
        copy.Audio = Audio.Clone();
        copy.Text = Text.Clone();
        copy.Beats = [.. Beats];
        copy.VolumeDips = VolumeDips.Select(d => d.Clone()).ToList();
        return copy;
    }
}

/// <summary>
/// A stretch where an item plays quieter, in source seconds. The level ramps down over <see cref="Ramp"/>
/// seconds before <see cref="Start"/> and back up after <see cref="End"/>.
/// </summary>
public sealed class VolumeDip
{
    public double Start { get; set; }

    public double End { get; set; }

    /// <summary>Volume during the dip, 0.25 = a quarter (12 dB quieter).</summary>
    public double Level { get; set; } = 0.25;

    public double Ramp { get; set; } = 0.3;

    public VolumeDip Clone() => (VolumeDip)MemberwiseClone();
}

/// <summary>A transition into an item. <see cref="Video"/> is an xfade name, <see cref="Audio"/> an acrossfade curve.</summary>
public sealed class TransitionSpec
{
    public const string None = "none";

    public string Video { get; set; } = None;

    public string Audio { get; set; } = None;

    public double Duration { get; set; } = 1.0;

    public TransitionSpec Clone() => (TransitionSpec)MemberwiseClone();
}

public sealed class Placement
{
    public PositionPreset Position { get; set; } = PositionPreset.FullFrame;

    /// <summary>Size as a percentage of the frame (ignored for full frame).</summary>
    public double SizePercent { get; set; } = 100;

    public double OpacityPercent { get; set; } = 100;

    /// <summary>Custom left edge, percent of frame width.</summary>
    public double CustomXPercent { get; set; }

    /// <summary>Custom top edge, percent of frame height.</summary>
    public double CustomYPercent { get; set; }

    /// <summary>Full frame only: fill the frame and cut off what spills over, instead of leaving black bars.</summary>
    public bool Fill { get; set; }

    /// <summary>Percent cut off the left edge of the source picture before placing it.</summary>
    public double CropLeft { get; set; }

    public double CropRight { get; set; }

    public double CropTop { get; set; }

    public double CropBottom { get; set; }

    /// <summary>Space between the picture and the frame edge for corner and side positions, percent of the shorter side.</summary>
    public double MarginPercent { get; set; } = 4;

    /// <summary>Rounded corners: radius as a percent of the picture's shorter side (0 square, 50 fully round).</summary>
    public double CornerRadiusPercent { get; set; }

    [JsonIgnore]
    public bool HasCrop => CropLeft > 0.01 || CropRight > 0.01 || CropTop > 0.01 || CropBottom > 0.01;

    public Placement Clone() => (Placement)MemberwiseClone();
}

public sealed class AudioCleanup
{
    public NoiseReduction NoiseReduction { get; set; }

    public HumFilter Hum { get; set; }

    /// <summary>Removes rumble and handling noise below 80 Hz.</summary>
    public bool RemoveRumble { get; set; }

    /// <summary>Bass gain in decibels, -12 to 12.</summary>
    public int LowDb { get; set; }

    /// <summary>Middle gain in decibels, -12 to 12.</summary>
    public int MidDb { get; set; }

    /// <summary>Treble gain in decibels, -12 to 12.</summary>
    public int HighDb { get; set; }

    public VoicePreset Voice { get; set; }

    /// <summary>Even out loudness to a standard level.</summary>
    public bool Normalize { get; set; }

    [JsonIgnore]
    public bool IsDefault =>
        NoiseReduction == NoiseReduction.Off && Hum == HumFilter.Off && !RemoveRumble &&
        LowDb == 0 && MidDb == 0 && HighDb == 0 && Voice == VoicePreset.None && !Normalize;

    public AudioCleanup Clone() => (AudioCleanup)MemberwiseClone();
}

public sealed class Marker
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public double Time { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>The words of a text item and how they look.</summary>
public sealed class TextStyle
{
    public string Content { get; set; } = "";

    /// <summary>A font name from <see cref="FontCatalog"/>.</summary>
    public string Font { get; set; } = "Segoe UI";

    /// <summary>Letter height as a percentage of the frame height.</summary>
    public double SizePercent { get; set; } = 8;

    public string ColorName { get; set; } = "White";

    public bool Bold { get; set; }

    /// <summary>A dark outline around the letters, readable over any picture.</summary>
    public bool Outline { get; set; } = true;

    public TextBackground Background { get; set; }

    public TextAnimation AnimationIn { get; set; } = TextAnimation.Fade;

    public TextAnimation AnimationOut { get; set; } = TextAnimation.Fade;

    /// <summary>Seconds each animation takes.</summary>
    public double AnimationDuration { get; set; } = 0.5;

    public TextStyle Clone() => (TextStyle)MemberwiseClone();
}

/// <summary>A credit line owed for a piece of media (for example a CC BY song).</summary>
public sealed class ProjectCredit
{
    public string Text { get; set; } = "";

    /// <summary>The media file it belongs to; the credit is only needed while that file is used.</summary>
    public string MediaPath { get; set; } = "";
}
