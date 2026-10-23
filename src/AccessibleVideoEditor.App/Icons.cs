using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.App;

/// <summary>Glyphs from the Segoe Fluent Icons / Segoe MDL2 Assets fonts built into Windows.</summary>
internal static class Icons
{
    public const string Play = "";
    public const string Stop = "";
    public const string Import = "";
    public const string Text = "";
    public const string Music = "";
    public const string Video = "";
    public const string Picture = "";
    public const string Split = "";
    public const string Delete = "";
    public const string Undo = "";
    public const string Redo = "";
    public const string Move = "";
    public const string Describe = "";
    public const string Export = "";
    public const string Save = "";
    public const string Help = "";
    public const string Marker = "";
    public const string Mute = "";
    public const string ColorCard = "";
    public const string Templates = "";

    public static string ForItem(ItemKind kind, LayerKind layer) => kind switch
    {
        ItemKind.Video when layer == LayerKind.Audio => Music,
        ItemKind.Video => Video,
        ItemKind.Image => Picture,
        ItemKind.Audio => Music,
        ItemKind.Text => Text,
        ItemKind.Color => ColorCard,
        _ => Video,
    };

    public static string ForLayer(LayerKind kind, bool muted) => muted ? Mute : kind == LayerKind.Video ? Video : Music;
}
