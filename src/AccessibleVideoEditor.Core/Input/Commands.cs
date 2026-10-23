namespace AccessibleVideoEditor.Core.Input;

/// <summary>Every action the editor can perform from the keyboard or a menu.</summary>
public enum CommandId
{
    // File
    NewProject,
    SaveAsTemplate,
    TemplateManager,
    OpenProject,
    SaveProject,
    SaveProjectAs,
    Export,
    ProjectSettings,
    Preferences,

    // Edit
    Undo,
    Redo,
    Copy,
    Cut,
    Paste,
    Duplicate,
    Delete,
    DeleteKeepTiming,
    DeleteRange,
    ToggleRipple,
    ItemProperties,
    EditTextOrRename,

    // Add
    ImportToEnd,
    ImportAtPlayhead,
    AddSoundEffect,
    AddColorCard,
    AddText,
    OnlineMusic,
    NewVideoLayer,
    NewAudioLayer,
    DeleteLayer,
    LayerProperties,

    // Navigate
    PreviousItem,
    NextItem,
    FirstItem,
    LastItem,
    PreviousLayer,
    NextLayer,
    Layer1,
    Layer2,
    Layer3,
    Layer4,
    Layer5,
    Layer6,
    Layer7,
    Layer8,
    Layer9,
    ListenAllLayers,
    GoToLayer,
    PreviousMarker,
    NextMarker,
    GoToTime,
    PreviousBeat,
    NextBeat,
    GoToStart,
    GoToEnd,
    SwitchPane,
    PreviousPane,
    FocusLibrary,
    FocusTimelineRegion,
    FocusProperties,
    FocusPreview,
    FocusLayers,

    // Arrange
    MoveItemEarlier,
    MoveItemLater,
    MoveItemFirst,
    MoveItemLast,
    SlideEarlier,
    SlideLater,
    SlideEarlierFine,
    SlideLaterFine,
    SlideLayerEarlier,
    SlideLayerLater,
    MoveLayerUp,
    MoveLayerDown,

    // Cut and markers
    Split,
    TrimStart,
    TrimEnd,
    SetRangeStart,
    SetRangeEnd,
    ClearRange,
    AddMarker,
    MarkerList,
    TransitionHere,

    // Playback
    PlayPause,
    Forward,
    Rewind,
    Stop,
    StepBack,
    StepForward,
    StepBackLong,
    StepForwardLong,
    FrameBack,
    FrameForward,
    ToggleMute,
    ToggleSolo,
    UnmuteAll,
    ItemVolumeUp,
    ItemVolumeDown,
    ToggleItemMute,
    LayerVolumeUp,
    LayerVolumeDown,

    // Picture
    PositionMode,
    DescribePicture,

    // Smart tools
    QuickVideo,
    Transcribe,
    TranscriptEditor,
    MakeCaptions,
    ShortenPauses,
    FindBeats,
    CutToBeats,
    AutoDuck,

    // Information
    SpeakTime,
    SpeakStatus,
    WhatsHere,
    DescribeItem,
    ProjectCheck,
    CycleVerbosity,
    KeyboardHelp,
}

public sealed record CommandInfo(CommandId Id, string Category, string Title, string Help, IReadOnlyList<string> DefaultKeys)
{
    /// <summary>
    /// True for keys without Ctrl or Alt (letters, arrows, Space, Delete...). These only work while the
    /// timeline or layer list has focus, never inside a text box.
    /// </summary>
    public bool IsTimelineOnly => DefaultKeys.Count > 0 && DefaultKeys.All(k =>
        KeyChord.TryParse(k, out var chord) && !chord.Ctrl && !chord.Alt && !IsFunctionKey(chord.Key));

    private static bool IsFunctionKey(string key) =>
        key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out var n) && n is >= 1 and <= 12;
}

/// <summary>The command table: names, help text and default keys. Key names are Windows key names.</summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandInfo> All { get; } = Build();

    public static CommandInfo Get(CommandId id) => All.First(c => c.Id == id);

    public static IReadOnlyList<string> Categories => All.Select(c => c.Category).Distinct().ToList();

    private static List<CommandInfo> Build()
    {
        var list = new List<CommandInfo>();

        void Add(CommandId id, string category, string title, string help, params string[] keys) =>
            list.Add(new CommandInfo(id, category, title, help, keys));

        const string file = "File";
        Add(CommandId.NewProject, file, "New project from template", "Start a new project from a built-in or saved template.", "Ctrl+N");
        Add(CommandId.SaveAsTemplate, file, "Save as template", "Save this project as a template in a folder of your choice, with or without its media.", "Ctrl+Shift+N");
        Add(CommandId.TemplateManager, file, "Manage templates", "List templates by folder: create a project, rename, move, delete, import or open the folder.", "Ctrl+Shift+K");
        Add(CommandId.OpenProject, file, "Open project", "Open a saved project.", "Ctrl+O");
        Add(CommandId.SaveProject, file, "Save project", "Save the project.", "Ctrl+S");
        Add(CommandId.SaveProjectAs, file, "Save project as", "Save the project under a new name.", "Ctrl+Shift+S");
        Add(CommandId.Export, file, "Export video", "Make the finished video or audio file.", "Ctrl+E");
        Add(CommandId.ProjectSettings, file, "Project settings", "Title, picture size and frame rate.", "Ctrl+Shift+P");
        Add(CommandId.Preferences, file, "Preferences", "Speech, sounds, step sizes and folders.", "Ctrl+Shift+OemComma");

        const string edit = "Edit";
        Add(CommandId.Undo, edit, "Undo", "Undo the last change. Says what was undone.", "Ctrl+Z");
        Add(CommandId.Redo, edit, "Redo", "Redo what you undid.", "Ctrl+Y", "Ctrl+Shift+Z");
        Add(CommandId.Copy, edit, "Copy item", "Copy the focused item.", "Ctrl+C");
        Add(CommandId.Cut, edit, "Cut item", "Copy the focused item and remove it.", "Ctrl+X");
        Add(CommandId.Paste, edit, "Paste item", "Paste the copied item after the focused item.", "Ctrl+V");
        Add(CommandId.Duplicate, edit, "Duplicate item", "Put a copy of the focused item right after it.", "Ctrl+D");
        Add(CommandId.Delete, edit, "Delete item", "Delete the focused item. With ripple on, later items move up to close the space.", "Delete");
        Add(CommandId.DeleteKeepTiming, edit, "Delete item, keep timing", "Delete the focused item and leave silence in its place, so later items do not move.", "Ctrl+Delete");
        Add(CommandId.DeleteRange, edit, "Delete range on this layer", "Remove everything between range start and range end on the focused layer.", "Shift+Delete");
        Add(CommandId.ToggleRipple, edit, "Ripple editing on or off", "With ripple on, deleting, trimming and sliding move later items too. With ripple off, later items keep their time.", "Ctrl+R");
        Add(CommandId.EditTextOrRename, edit, "Edit text or rename", "Change the words of a text item, or rename any other item.", "F2", "Shift+Enter");
        Add(CommandId.ItemProperties, edit, "Item options", "Open all options for the focused item: length, volume, fades, transition, position, sound clean-up and equaliser.", "Enter");

        const string add = "Add";
        Add(CommandId.ImportToEnd, add, "Add files to end of layer", "Choose video, picture or audio files and add them after the last item of the focused layer. Sound files go to an audio layer automatically.", "Ctrl+I");
        Add(CommandId.ImportAtPlayhead, add, "Add files at playhead", "Add files at the playhead time without moving anything; a free layer is used or created.", "Ctrl+Shift+I");
        Add(CommandId.AddSoundEffect, add, "Add sound effect or music", "Choose a built-in or your own sound and add it at the playhead.", "Ctrl+Shift+E");
        Add(CommandId.AddText, add, "Add text", "Add a title, caption or credit at the playhead, with style, position and animation.", "Ctrl+Shift+T");
        Add(CommandId.OnlineMusic, add, "Find free music online", "Search Openverse for music and sound effects you may use freely (public domain or credit required). Credits are added to the project.", "Ctrl+Shift+O");
        Add(CommandId.AddColorCard, add, "Add colour card", "Add a solid colour picture, for a title background or a pause.", "Ctrl+Shift+C");
        Add(CommandId.NewVideoLayer, add, "New video layer", "Add a video layer above the others.", "Ctrl+Shift+V");
        Add(CommandId.NewAudioLayer, add, "New audio layer", "Add an audio layer.", "Ctrl+Shift+A");
        Add(CommandId.DeleteLayer, add, "Delete layer", "Delete the focused layer and everything on it.", "Ctrl+Shift+Delete");
        Add(CommandId.LayerProperties, add, "Layer options", "Name, volume and defaults for new items on the focused layer.", "Alt+Enter");

        const string nav = "Move around";
        Add(CommandId.PreviousItem, nav, "Previous item", "Go to the previous item on this layer. The playhead moves to its start.", "Left");
        Add(CommandId.NextItem, nav, "Next item", "Go to the next item on this layer. The playhead moves to its start.", "Right");
        Add(CommandId.FirstItem, nav, "First item", "Go to the first item on this layer.", "Home", "Ctrl+Left");
        Add(CommandId.LastItem, nav, "Last item", "Go to the last item on this layer.", "End", "Ctrl+Right");
        Add(CommandId.PreviousLayer, nav, "Layer above in the list", "Go to the previous layer, to the item at the playhead. Works with any number of layers.", "Up");
        Add(CommandId.NextLayer, nav, "Layer below in the list", "Go to the next layer, to the item at the playhead. Works with any number of layers.", "Down");
        for (var n = 1; n <= 9; n++)
        {
            Add(CommandId.Layer1 + (n - 1), nav, $"Go to layer {n}", $"Focus layer {n} and listen to it on its own.", $"Alt+D{n}");
        }

        Add(CommandId.ListenAllLayers, nav, "Listen to all layers", "Play every layer together (muted layers stay silent). Press again to listen to the focused layer only.", "Alt+D0");
        Add(CommandId.GoToLayer, nav, "Go to layer number", "Type a layer number to jump to it. Useful with more than 9 layers.", "Ctrl+L");
        Add(CommandId.PreviousMarker, nav, "Previous marker", "Jump the playhead back to the previous marker, range start or range end.", "Alt+Left");
        Add(CommandId.NextMarker, nav, "Next marker", "Jump the playhead forward to the next marker, range start or range end.", "Alt+Right");
        Add(CommandId.GoToTime, nav, "Go to time", "Type a time to move the playhead, for example 1:30, 90, or +5.", "Ctrl+G");
        Add(CommandId.PreviousBeat, nav, "Previous beat", "Move the playhead to the previous beat of the music (after Find beats).", "Shift+Left");
        Add(CommandId.NextBeat, nav, "Next beat", "Move the playhead to the next beat of the music (after Find beats).", "Shift+Right");
        Add(CommandId.GoToStart, nav, "Go to start", "Move the playhead to the start of the project.", "Ctrl+Home", "Ctrl+Shift+Left");
        Add(CommandId.GoToEnd, nav, "Go to end", "Move the playhead to the end of the project.", "Ctrl+End", "Ctrl+Shift+Right");
        Add(CommandId.SwitchPane, nav, "Next area", "Move to the next area of the window: library, timeline, layers, properties, preview.", "F6", "Alt+OemPeriod");
        Add(CommandId.PreviousPane, nav, "Previous area", "Move to the previous area of the window.", "Shift+F6", "Alt+OemComma");
        Add(CommandId.FocusLibrary, nav, "Go to library", "The left area: media, sounds, online music, text styles, templates, import and export.", "Ctrl+D1");
        Add(CommandId.FocusTimelineRegion, nav, "Go to timeline", "The items of the focused layer.", "Ctrl+D2");
        Add(CommandId.FocusProperties, nav, "Go to properties", "The right area: quick settings for the focused item.", "Ctrl+D3");
        Add(CommandId.FocusPreview, nav, "Go to preview", "The picture area. It describes what is on screen at the playhead.", "Ctrl+D4");
        Add(CommandId.FocusLayers, nav, "Go to layer list", "The list of all layers.", "Ctrl+D5");

        const string arrange = "Arrange";
        Add(CommandId.MoveItemEarlier, arrange, "Move item earlier in order", "Swap the focused item with the one before it.", "Alt+Shift+Left");
        Add(CommandId.MoveItemLater, arrange, "Move item later in order", "Swap the focused item with the one after it.", "Alt+Shift+Right");
        Add(CommandId.MoveItemFirst, arrange, "Move item to the start", "Make the focused item the first on its layer.", "Alt+Shift+Home", "Alt+Shift+OemComma");
        Add(CommandId.MoveItemLast, arrange, "Move item to the end", "Make the focused item the last on its layer.", "Alt+Shift+End", "Alt+Shift+OemPeriod");
        Add(CommandId.SlideEarlier, arrange, "Slide item earlier", "Slide the focused item earlier by the slide step (1 second unless changed in preferences).", "Alt+J");
        Add(CommandId.SlideLater, arrange, "Slide item later", "Slide the focused item later by the slide step.", "Alt+L");
        Add(CommandId.SlideEarlierFine, arrange, "Slide item earlier a little", "Slide the focused item earlier by the fine step (a tenth of a second).", "Alt+Shift+J");
        Add(CommandId.SlideLaterFine, arrange, "Slide item later a little", "Slide the focused item later by the fine step.", "Alt+Shift+L");
        Add(CommandId.SlideLayerEarlier, arrange, "Slide whole layer earlier", "Move every item on the focused layer earlier by the slide step.", "Ctrl+Shift+J");
        Add(CommandId.SlideLayerLater, arrange, "Slide whole layer later", "Move every item on the focused layer later by the slide step.", "Ctrl+Shift+L");
        Add(CommandId.MoveLayerUp, arrange, "Move layer up the list", "Give the focused layer a lower number. For video layers this puts its picture further back.", "Alt+Shift+Up");
        Add(CommandId.MoveLayerDown, arrange, "Move layer down the list", "Give the focused layer a higher number. For video layers this brings its picture to the front.", "Alt+Shift+Down");

        const string cut = "Cut and mark";
        Add(CommandId.Split, cut, "Split at playhead", "Cut the item at the playhead into two items.", "S");
        Add(CommandId.TrimStart, cut, "Trim start to playhead", "Remove the part of the item before the playhead.", "Shift+OemOpenBrackets", "Shift+I");
        Add(CommandId.TrimEnd, cut, "Trim end to playhead", "Remove the part of the item after the playhead.", "Shift+OemCloseBrackets", "Shift+O");
        Add(CommandId.SetRangeStart, cut, "Set range start", "Mark the playhead as the start of a range. I (in) works on every keyboard.", "OemOpenBrackets", "I");
        Add(CommandId.SetRangeEnd, cut, "Set range end", "Mark the playhead as the end of a range. O (out) works on every keyboard.", "OemCloseBrackets", "O");
        Add(CommandId.ClearRange, cut, "Clear range", "Remove the range start and end marks.", "Ctrl+Shift+R");
        Add(CommandId.AddMarker, cut, "Add marker", "Put a named marker at the playhead.", "B");
        Add(CommandId.MarkerList, cut, "Markers and their options", "List all markers to jump, rename, move, delete, split or add a transition there.", "Shift+B");
        Add(CommandId.TransitionHere, cut, "Transition at playhead", "Choose the transition at the join nearest the playhead on the focused layer.", "Ctrl+T");

        const string play = "Play and listen";
        Add(CommandId.PlayPause, play, "Play or pause", "Play from the playhead, or pause.", "K", "Space");
        Add(CommandId.Forward, play, "Play forward, faster", "Play; press again for double and quadruple speed.", "L");
        Add(CommandId.Rewind, play, "Jump back", "Jump the playhead back by the jump step (5 seconds).", "J");
        Add(CommandId.Stop, play, "Stop", "Stop playing. The playhead stays where it stopped.", "Escape");
        Add(CommandId.StepBack, play, "Back 1 second", "Move the playhead back 1 second.", "OemComma");
        Add(CommandId.StepForward, play, "Forward 1 second", "Move the playhead forward 1 second.", "OemPeriod");
        Add(CommandId.StepBackLong, play, "Back 10 seconds", "Move the playhead back 10 seconds.", "Shift+OemComma");
        Add(CommandId.StepForwardLong, play, "Forward 10 seconds", "Move the playhead forward 10 seconds.", "Shift+OemPeriod");
        Add(CommandId.FrameBack, play, "Back 1 frame", "Move the playhead back one frame.", "Ctrl+OemComma");
        Add(CommandId.FrameForward, play, "Forward 1 frame", "Move the playhead forward one frame.", "Ctrl+OemPeriod");
        Add(CommandId.ToggleMute, play, "Mute or unmute layer", "Mute the focused layer. Muted layers are left out of playback and export.", "M");
        Add(CommandId.ToggleSolo, play, "Solo layer", "Mute all other layers. Press again to unmute them.", "Shift+M");
        Add(CommandId.UnmuteAll, play, "Unmute all layers", "Unmute every layer.", "Ctrl+Shift+M");
        Add(CommandId.ItemVolumeUp, play, "Item louder", "Raise the focused item's volume by 10 percent.", "Ctrl+Up");
        Add(CommandId.ItemVolumeDown, play, "Item quieter", "Lower the focused item's volume by 10 percent.", "Ctrl+Down");
        Add(CommandId.ToggleItemMute, play, "Mute or unmute item", "Mute or unmute only the focused item's sound.", "Ctrl+M");
        Add(CommandId.LayerVolumeUp, play, "Layer louder", "Raise the focused layer's volume by 10 percent.", "Ctrl+Shift+Up");
        Add(CommandId.LayerVolumeDown, play, "Layer quieter", "Lower the focused layer's volume by 10 percent.", "Ctrl+Shift+Down");

        const string picture = "Picture";
        Add(CommandId.PositionMode, picture, "Position mode", "Move, resize, crop, fill and fade the focused picture or text with the keyboard. Arrows move, plus and minus resize, number keys 1 to 9 snap to a place, 0 fills the frame, Control arrows crop, F fills, O changes opacity, D describes, Enter keeps, Escape undoes.", "P");
        Add(CommandId.DescribePicture, picture, "Describe the picture", "Say what is visible at the playhead: every picture and text, where it is, its size and what it covers.", "Shift+W");

        const string smart = "Smart tools";
        Add(CommandId.QuickVideo, smart, "Quick video", "Make a finished video in three steps: choose files, a style (Reel, YouTube, Slideshow or Talking video) and a title. Silences, timing, transitions, music, captions and loudness are done for you, and everything can still be changed.", "Ctrl+Shift+Q");
        Add(CommandId.Transcribe, smart, "Transcribe speech", "Write down the words spoken on the focused layer, on this computer with no upload. Works in English, Urdu, Hindi and many more languages.", "Ctrl+Shift+W");
        Add(CommandId.TranscriptEditor, smart, "Edit by transcript", "Open the words of the focused layer as a document. Delete words or sentences to cut them out of the video, or correct the words for captions.", "Ctrl+Shift+X");
        Add(CommandId.MakeCaptions, smart, "Make captions", "Turn the transcript of the focused layer into captions on a Captions layer at the top.", "Ctrl+Shift+G");
        Add(CommandId.ShortenPauses, smart, "Shorten pauses", "Find long pauses in the focused clip and shorten or remove them. Says how many and how long before changing anything.", "Ctrl+Shift+H");
        Add(CommandId.FindBeats, smart, "Find beats", "Find the tempo and beats of the focused music. Then Shift Left and Shift Right jump from beat to beat.", "Ctrl+Shift+B");
        Add(CommandId.CutToBeats, smart, "Cut to the beat", "Make every picture and clip on the focused layer end on a beat of the music.", "Ctrl+Shift+U");
        Add(CommandId.AutoDuck, smart, "Lower music under speech", "Make the focused music layer quieter wherever someone speaks on the other layers (auto-ducking), or remove that.", "Ctrl+Shift+D");

        const string info = "Information";
        Add(CommandId.SpeakTime, info, "Say playhead time", "Say where the playhead is.", "T");
        Add(CommandId.SpeakStatus, info, "Say project status", "Say project length, layer, listening mode, ripple and range.", "Shift+T");
        Add(CommandId.WhatsHere, info, "What's at the playhead", "Say what every layer has at the playhead, and where pictures are placed.", "W");
        Add(CommandId.DescribeItem, info, "Describe item in detail", "Say every setting of the focused item.", "D");
        Add(CommandId.ProjectCheck, info, "Check project", "List possible problems: missing files, black gaps, muted layers and transitions that cannot play.", "F7", "Ctrl+Shift+F");
        Add(CommandId.CycleVerbosity, info, "Change how much is spoken", "Switch between brief, normal and detailed descriptions.", "Shift+V");
        Add(CommandId.KeyboardHelp, info, "Keyboard help", "List every command and its keys. Type to search.", "F1", "Ctrl+H");

        return list;
    }
}
