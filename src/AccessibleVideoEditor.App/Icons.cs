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
    public const string Microphone = "\uE720";
    public const string Captions = "\uE7F0";
    public const string Volume = "\uE767";
    public const string Stopwatch = "\uE916";
    public const string Lightning = "\uE945";
    public const string Transition = "\uE8AB";
    public const string Animation = "\uE7F4";
    public const string Cut = "\uE8C6";
    public const string Copy = "\uE8C8";
    public const string Paste = "\uE77F";
    public const string Add = "\uE710";
    public const string Settings = "\uE713";
    public const string Previous = "\uE892";
    public const string Next = "\uE893";
    public const string Pause = "\uE769";
    public const string Edit = "\uE70F";
    public const string Layers = "\uE81E";
    public const string Check = "\uE73E";
    public const string Search = "\uE721";

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
