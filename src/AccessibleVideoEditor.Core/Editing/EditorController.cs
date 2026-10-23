using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>What the playback engine should do after a command.</summary>
public enum PlaybackRequest
{
    None,

    /// <summary>Play if paused, pause if playing.</summary>
    Toggle,

    /// <summary>Play, or play faster if already playing.</summary>
    Faster,

    /// <summary>The playhead jumped; if playing, continue from the new place.</summary>
    Seek,

    Stop,

    /// <summary>What should be heard changed (layer, mute, edit); if playing, restart from the playhead.</summary>
    Refresh,
}

/// <summary>The result of a command, for the window to act on: what to say, which sound, what to play.</summary>
public sealed record Feedback
{
    public static Feedback Silent { get; } = new();

    public string Speech { get; init; } = "";

    public EarconEvent? Earcon { get; init; }

    public PlaybackRequest Playback { get; init; }

    /// <summary>The project changed (views need refreshing).</summary>
    public bool ProjectChanged { get; init; }

    public bool IsError => Earcon is EarconEvent.Error or EarconEvent.Edge;

    /// <summary>
    /// Where the focused picture now sits (centre, 0 to 1 across and down), so the window can play a tone
    /// panned left or right and pitched by height: a sound picture of the position.
    /// </summary>
    public (double X, double Y)? Position { get; init; }
}

/// <summary>
/// The editor's behaviour, independent of any window: which layer and item have focus, where the playhead
/// is, what is being listened to, and what every keyboard command does. The window only moves focus,
/// speaks, plays sounds and shows dialogs.
/// </summary>
public sealed partial class EditorController
{
    private Item? _clipboard;
    private LayerKind _clipboardLayerKind;

    public EditorController(ProjectSession session, AppSettings settings)
    {
        Session = session;
        Settings = settings;
        Describer = new Describer(settings.Verbosity, settings.TimeStyle);
        EnsureFocus();
    }

    public ProjectSession Session { get; private set; }

    public Project Project => Session.Project;

    public AppSettings Settings { get; }

    public Describer Describer { get; }

    public Guid? FocusedLayerId { get; private set; }

    public Guid? FocusedItemId { get; private set; }

    public double Playhead { get; private set; }

    /// <summary>True: hear every unmuted layer. False: hear only the focused layer.</summary>
    public bool ListenAll { get; private set; } = true;

    public Layer? FocusedLayer => FocusedLayerId is { } id ? Project.FindLayer(id) : null;

    public int FocusedLayerIndex => FocusedLayerId is { } id ? Project.LayerIndexOf(id) : -1;

    public int FocusedItemIndex => FocusedLayer is { } layer && FocusedItemId is { } id ? layer.IndexOf(id) : -1;

    public Item? FocusedItem => FocusedItemIndex is var i and >= 0 ? FocusedLayer!.Items[i] : null;

    public RenderScope CurrentScope => ListenAll || FocusedLayerId is null
        ? RenderScope.AllLayers
        : RenderScope.Layer(FocusedLayerId.Value);

    public double ProjectDuration => Timeline.ProjectDuration(Project);

    public string ListeningText => ListenAll || FocusedLayer is null
        ? "listening to all layers"
        : $"listening to layer {FocusedLayerIndex + 1} only";

    // ------------------------------------------------------------------ project lifecycle

    public void ReplaceSession(ProjectSession session)
    {
        Session = session;
        ResetPositionMode();
        Playhead = 0;
        ListenAll = true;
        FocusedLayerId = null;
        FocusedItemId = null;
        EnsureFocus();
    }

    /// <summary>Called by the playback engine while playing.</summary>
    public void SetPlayheadFromPlayback(double time) => Playhead = Math.Clamp(time, 0, Math.Max(0, ProjectDuration));

    /// <summary>The window reports the user moved focus with the mouse or the layer list.</summary>
    public void FocusLayer(Guid layerId)
    {
        if (Project.FindLayer(layerId) is { } layer)
        {
            FocusedLayerId = layer.Id;
            FocusedItemId = NearestItemId(layer);
        }
    }

    public void FocusItem(Guid itemId)
    {
        if (FocusedLayer is { } layer && layer.IndexOf(itemId) is var index and >= 0)
        {
            FocusedItemId = itemId;
            Playhead = Timeline.Span(layer, index).Start;
        }
    }

    // ------------------------------------------------------------------ keyboard commands

    /// <summary>Runs a command that needs no dialog. Returns null for commands the window must handle.</summary>
    public Feedback? Execute(CommandId command)
    {
        switch (command)
        {
            case CommandId.Undo: return UndoRedo(Session.Undo());
            case CommandId.Redo: return UndoRedo(Session.Redo());
            case CommandId.Copy: return Copy();
            case CommandId.Cut: return CutItem();
            case CommandId.Paste: return Paste();
            case CommandId.Duplicate: return Duplicate();
            case CommandId.Delete: return DeleteItem(Settings.Ripple);
            case CommandId.DeleteKeepTiming: return DeleteItem(false);
            case CommandId.DeleteRange: return DeleteRange();
            case CommandId.ToggleRipple:
                Settings.Ripple = !Settings.Ripple;
                return Say(Settings.Ripple
                    ? "Ripple on. Later items move with your edits."
                    : "Ripple off. Later items keep their time.", EarconEvent.Done);

            case CommandId.NewVideoLayer: return NewLayer(LayerKind.Video);
            case CommandId.NewAudioLayer: return NewLayer(LayerKind.Audio);
            case CommandId.DeleteLayer: return DeleteLayer();

            case CommandId.PreviousItem: return StepItem(-1);
            case CommandId.NextItem: return StepItem(1);
            case CommandId.FirstItem: return JumpItem(first: true);
            case CommandId.LastItem: return JumpItem(first: false);
            case CommandId.PreviousLayer: return StepLayer(-1);
            case CommandId.NextLayer: return StepLayer(1);
            case >= CommandId.Layer1 and <= CommandId.Layer9: return GoToLayer(command - CommandId.Layer1 + 1, listenOnly: true);
            case CommandId.ListenAllLayers: return ToggleListenAll();
            case CommandId.PreviousMarker: return StepMarker(-1);
            case CommandId.NextMarker: return StepMarker(1);
            case CommandId.PreviousBeat: return StepBeat(-1);
            case CommandId.NextBeat: return StepBeat(1);
            case CommandId.GoToStart: return MovePlayheadTo(0);
            case CommandId.GoToEnd: return MovePlayheadTo(ProjectDuration);

            case CommandId.MoveItemEarlier: return ItemEdit("move", (layer, i) => ProjectEditor.MoveItem(layer, i, -1));
            case CommandId.MoveItemLater: return ItemEdit("move", (layer, i) => ProjectEditor.MoveItem(layer, i, 1));
            case CommandId.MoveItemFirst: return ItemEdit("move", (layer, i) => ProjectEditor.MoveItemTo(layer, i, 0));
            case CommandId.MoveItemLast: return ItemEdit("move", (layer, i) => ProjectEditor.MoveItemTo(layer, i, layer.Items.Count - 1));
            case CommandId.SlideEarlier: return Slide(-Settings.SlideStep);
            case CommandId.SlideLater: return Slide(Settings.SlideStep);
            case CommandId.SlideEarlierFine: return Slide(-Settings.FineSlideStep);
            case CommandId.SlideLaterFine: return Slide(Settings.FineSlideStep);
            case CommandId.SlideLayerEarlier: return LayerEdit("slide layer", layer => ProjectEditor.SlideLayer(layer, -Settings.SlideStep));
            case CommandId.SlideLayerLater: return LayerEdit("slide layer", layer => ProjectEditor.SlideLayer(layer, Settings.SlideStep));
            case CommandId.MoveLayerUp: return LayerEdit("move layer", layer => ProjectEditor.MoveLayer(Project, layer.Id, -1));
            case CommandId.MoveLayerDown: return LayerEdit("move layer", layer => ProjectEditor.MoveLayer(Project, layer.Id, 1));

            case CommandId.Split: return AtPlayhead("split", (layer, i) => ProjectEditor.SplitItem(layer, i, Playhead));
            case CommandId.TrimStart: return AtPlayhead("trim start", (layer, i) => ProjectEditor.TrimStart(layer, i, Playhead, Settings.Ripple));
            case CommandId.TrimEnd: return AtPlayhead("trim end", (layer, i) => ProjectEditor.TrimEnd(layer, i, Playhead, Settings.Ripple));
            case CommandId.SetRangeStart: return Edit("range start", p => ProjectEditor.SetRangeStart(p, Playhead), EarconEvent.MarkerAdded, playback: false);
            case CommandId.SetRangeEnd: return Edit("range end", p => ProjectEditor.SetRangeEnd(p, Playhead), EarconEvent.MarkerAdded, playback: false);
            case CommandId.ClearRange: return Edit("clear range", ProjectEditor.ClearRange, EarconEvent.Deleted, playback: false);
            case CommandId.AddMarker: return Edit("add marker", p => ProjectEditor.AddMarker(p, Playhead), EarconEvent.MarkerAdded, playback: false);

            case CommandId.PlayPause: return new Feedback { Playback = PlaybackRequest.Toggle };
            case CommandId.Forward: return new Feedback { Playback = PlaybackRequest.Faster };
            case CommandId.Stop: return new Feedback { Playback = PlaybackRequest.Stop };
            case CommandId.Rewind: return Step(-Settings.JumpStep);
            case CommandId.StepBack: return Step(-1);
            case CommandId.StepForward: return Step(1);
            case CommandId.StepBackLong: return Step(-10);
            case CommandId.StepForwardLong: return Step(10);
            case CommandId.FrameBack: return Step(-1 / Project.FrameRate);
            case CommandId.FrameForward: return Step(1 / Project.FrameRate);
            case CommandId.ToggleMute: return MuteEdit(p => ProjectEditor.ToggleMute(p, FocusedLayerId!.Value));
            case CommandId.ToggleSolo: return MuteEdit(p => ProjectEditor.ToggleSolo(p, FocusedLayerId!.Value));
            case CommandId.UnmuteAll: return MuteEdit(ProjectEditor.UnmuteAll);

            case CommandId.SpeakTime: return Say(PlayheadText());
            case CommandId.SpeakStatus: return Say(StatusText());
            case CommandId.WhatsHere: return Say(Describer.WhatsHere(Project, Playhead));
            case CommandId.DescribeItem: return DescribeFocused();
            case CommandId.CycleVerbosity:
                Settings.Verbosity = (Verbosity)(((int)Settings.Verbosity + 1) % 3);
                Describer.Verbosity = Settings.Verbosity;
                return Say($"Verbosity {Settings.Verbosity.ToString().ToLowerInvariant()}.", EarconEvent.Done);
            default:
                return ExecuteMore(command);
        }
    }

    // ------------------------------------------------------------------ operations behind dialogs

    /// <summary>Adds probed files: to the end of the right layer, or at the playhead in free space.</summary>
    public Feedback AddMedia(IReadOnlyList<MediaInfo> files, bool atPlayhead)
    {
        if (files.Count == 0)
        {
            return Feedback.Silent;
        }

        Guid? firstItem = null;
        Guid? firstLayer = null;
        var placed = new List<string>();
        var result = Session.Execute(files.Count == 1 ? "add file" : "add files", project =>
        {
            var time = Playhead;
            foreach (var info in files)
            {
                var kind = MediaProbe.PreferredLayer(info);
                Layer layer;
                if (atPlayhead)
                {
                    var probe = MediaProbe.CreateItem(info, new LayerDefaults(), kind);
                    layer = CandidateLayers(project, kind).FirstOrDefault(l => ProjectEditor.HasRoomAt(l, time, probe.Duration))
                            ?? ProjectEditor.AddLayer(project, kind);
                }
                else
                {
                    layer = FocusedLayerId is { } id && project.FindLayer(id) is { } focused && focused.Kind == kind
                        ? focused
                        : project.Layers.FirstOrDefault(l => l.Kind == kind) ?? ProjectEditor.AddLayer(project, kind);
                }

                var item = MediaProbe.CreateItem(info, layer.Defaults, layer.Kind);
                var added = atPlayhead ? ProjectEditor.InsertAtTime(layer, item, time) : ProjectEditor.AppendItem(layer, item);
                if (!added.Ok)
                {
                    return added;
                }

                if (atPlayhead)
                {
                    time += item.Duration;
                }

                if (Credits.ForMediaFile(info.Path) is { } credit)
                {
                    Credits.Add(project, credit, info.Path);
                }

                firstItem ??= item.Id;
                firstLayer ??= layer.Id;
                placed.Add($"{item.Name} on layer {project.Layers.IndexOf(layer) + 1}");
            }

            return EditResult.Success(files.Count == 1 ? $"Added {placed[0]}." : $"Added {files.Count} files: {string.Join(", ", placed)}.");
        });

        if (!result.Ok)
        {
            return Error(result.Message);
        }

        FocusedLayerId = firstLayer;
        FocusedItemId = firstItem;
        if (!atPlayhead && FocusedItemIndex >= 0)
        {
            Playhead = Timeline.Span(FocusedLayer!, FocusedItemIndex).Start;
        }

        return Changed(result.Message, EarconEvent.Done);
    }

    public Feedback AddColorCard(string colorName, double duration)
    {
        Guid? id = null;
        Guid? layerId = null;
        var result = Session.Execute("add colour card", project =>
        {
            var layer = FocusedLayerId is { } fid && project.FindLayer(fid) is { Kind: LayerKind.Video } focused
                ? focused
                : project.Layers.FirstOrDefault(l => l.Kind == LayerKind.Video) ?? ProjectEditor.AddLayer(project, LayerKind.Video);
            var card = MediaProbe.CreateColorCard(colorName, duration, layer.Defaults);
            id = card.Id;
            layerId = layer.Id;
            var after = layer.Id == FocusedLayerId ? FocusedItemIndex : layer.Items.Count - 1;
            return ProjectEditor.InsertAfter(layer, after, card);
        });
        if (!result.Ok)
        {
            return Error(result.Message);
        }

        FocusedLayerId = layerId;
        FocusedItemId = id;
        Playhead = Timeline.Span(FocusedLayer!, FocusedItemIndex).Start;
        return Changed(result.Message, EarconEvent.Done);
    }

    public Feedback UpdateFocusedItem(Item edited)
    {
        if (FocusedItemIndex < 0)
        {
            return Error("No item selected.");
        }

        var index = FocusedItemIndex;
        var layerId = FocusedLayerId!.Value;
        return ToFeedback(Session.Execute("item options", p => ProjectEditor.UpdateItem(p.FindLayer(layerId)!, index, edited)), EarconEvent.Done);
    }

    public Feedback UpdateFocusedLayer(string name, double volume, LayerDefaults defaults)
    {
        if (FocusedLayerId is not { } id)
        {
            return Error("There are no layers. Add one with Control Shift V or Control Shift A.");
        }

        return ToFeedback(Session.Execute("layer options", p =>
        {
            var layer = p.FindLayer(id)!;
            layer.Name = string.IsNullOrWhiteSpace(name) ? layer.Name : name.Trim();
            layer.Volume = Math.Clamp(volume, 0, 4);
            defaults.Placement = ProjectEditor.ClampPlacement(defaults.Placement);
            defaults.Audio = ProjectEditor.ClampAudio(defaults.Audio);
            defaults.ImageDuration = Math.Clamp(defaults.ImageDuration, Timeline.MinItemDuration, 3600);
            defaults.Volume = Math.Clamp(defaults.Volume, 0, 4);
            layer.Defaults = defaults;
            return EditResult.Success($"Updated layer {layer.Name}.");
        }), EarconEvent.Done);
    }

    public Feedback UpdateProjectSettings(string title, int width, int height, double frameRate)
    {
        return ToFeedback(Session.Execute("project settings", p =>
        {
            p.Title = string.IsNullOrWhiteSpace(title) ? p.Title : title.Trim();
            p.Width = Math.Clamp(width / 2 * 2, 16, 7680);
            p.Height = Math.Clamp(height / 2 * 2, 16, 4320);
            p.FrameRate = Math.Clamp(frameRate, 1, 120);
            return EditResult.Success($"Project is {p.Width} by {p.Height} at {p.FrameRate:0.##} frames per second.");
        }), EarconEvent.Done);
    }

    public Feedback SetTransitionAtPlayhead(TransitionSpec spec) => SetTransitionAt(Playhead, spec);

    public Feedback SetTransitionAt(double time, TransitionSpec spec)
    {
        if (FocusedLayerId is not { } id)
        {
            return Error("There are no layers.");
        }

        var result = Session.Execute("transition", p => ProjectEditor.SetTransitionAt(p.FindLayer(id)!, time, spec));
        if (result.Ok && result.FocusItemId is { } item)
        {
            FocusedItemId = item;
        }

        return ToFeedback(result, EarconEvent.Done);
    }

    public Feedback SplitAt(double time)
    {
        Playhead = Math.Max(0, time);
        return Execute(CommandId.Split)!;
    }

    public Feedback GoToTime(double time) => MovePlayheadTo(time);

    public Feedback GoToMarker(Guid markerId)
    {
        var marker = Project.Markers.FirstOrDefault(m => m.Id == markerId);
        return marker is null ? Error("That marker no longer exists.") : MarkerFeedback(marker.Time);
    }

    public Feedback UpdateMarker(Guid markerId, string name, double time) =>
        ToFeedback(Session.Execute("edit marker", p => ProjectEditor.UpdateMarker(p, markerId, name, time)), EarconEvent.Done, playback: false);

    public Feedback DeleteMarker(Guid markerId) =>
        ToFeedback(Session.Execute("delete marker", p => ProjectEditor.DeleteMarker(p, markerId)), EarconEvent.Deleted, playback: false);

    public Feedback GoToLayer(int number, bool listenOnly)
    {
        if (Project.Layers.Count == 0)
        {
            return Error("There are no layers. Add one with Control Shift V or Control Shift A.");
        }

        if (number < 1 || number > Project.Layers.Count)
        {
            return Error($"There is no layer {number}. The project has {Project.Layers.Count} layers.");
        }

        var layer = Project.Layers[number - 1];
        var sameLayer = layer.Id == FocusedLayerId;
        var wasListeningAll = ListenAll;
        FocusedLayerId = layer.Id;
        FocusedItemId = NearestItemId(layer);
        if (listenOnly)
        {
            ListenAll = false;
        }

        var listening = listenOnly ? ", listening to this layer only" : "";
        return new Feedback
        {
            Speech = Describer.Layer(Project, layer) + listening + ".",
            Earcon = EarconEvent.LayerChange,
            Playback = sameLayer && wasListeningAll == ListenAll ? PlaybackRequest.None : PlaybackRequest.Refresh,
        };
    }

    /// <summary>Opens a project or a template in place of the current one.</summary>
    public Feedback Load(ProjectSession session, string description)
    {
        ReplaceSession(session);
        var layers = Project.Layers.Count == 1 ? "1 layer" : $"{Project.Layers.Count} layers";
        return new Feedback
        {
            Speech = $"{description}. {Project.Title}, {layers}, {Describer.Time(ProjectDuration)} long.",
            Earcon = EarconEvent.Finished,
            ProjectChanged = true,
            Playback = PlaybackRequest.Stop,
        };
    }

    // ------------------------------------------------------------------ text

    public string PlayheadText()
    {
        var text = $"Playhead {Describer.Time(Playhead)} of {Describer.Time(ProjectDuration)}";
        if (FocusedLayer is { } layer && Timeline.ItemAt(layer, Playhead) is var index and >= 0)
        {
            var span = Timeline.Span(layer, index);
            text += $", {Describer.Time(Playhead - span.Start)} into {layer.Items[index].Name}";
        }

        return text + ".";
    }

    public string StatusText()
    {
        var parts = new List<string>
        {
            $"{Project.Title}, {Describer.Time(ProjectDuration)} long, {Project.Layers.Count} layers",
        };
        if (FocusedLayer is { } layer)
        {
            parts.Add($"on layer {FocusedLayerIndex + 1}, {layer.Name}");
        }

        parts.Add(ListeningText);
        parts.Add($"playhead {Describer.Time(Playhead)}");
        parts.Add(Settings.Ripple ? "ripple on" : "ripple off");
        if (Project.RangeStart is { } s && Project.RangeEnd is { } e)
        {
            parts.Add($"range {Describer.Time(s)} to {Describer.Time(e)}");
        }
        else if (Project.RangeStart is { } only)
        {
            parts.Add($"range start {Describer.Time(only)}, no range end");
        }

        var muted = Project.Layers.Count(l => l.Muted);
        if (muted > 0)
        {
            parts.Add(muted == 1 ? "1 layer muted" : $"{muted} layers muted");
        }

        if (Session.IsDirty)
        {
            parts.Add("not saved");
        }

        return string.Join(", ", parts) + ".";
    }

    public string FocusedItemText() => FocusedItemIndex >= 0
        ? Describer.Item(FocusedLayer!, FocusedItemIndex)
        : FocusedLayer is { } layer ? EmptyLayerText(layer) : "No layers.";

    public string EmptyLayerText(Layer layer) =>
        $"{Describer.Layer(Project, layer)}. Press Control I to add files.";

    // ------------------------------------------------------------------ implementation

    private Feedback StepItem(int direction)
    {
        if (FocusedLayer is not { } layer || layer.Items.Count == 0)
        {
            return Edge(FocusedLayer is null ? "No layers." : "This layer is empty. Press Control I to add files.");
        }

        var index = FocusedItemIndex;
        var target = index < 0 ? 0 : index + direction;
        if (target < 0)
        {
            return Edge("First item.");
        }

        if (target >= layer.Items.Count)
        {
            return Edge("Last item.");
        }

        FocusedItemId = layer.Items[target].Id;
        Playhead = Timeline.Span(layer, target).Start;
        return new Feedback { Earcon = EarconEvent.Navigate, Playback = PlaybackRequest.Seek };
    }

    private Feedback JumpItem(bool first)
    {
        if (FocusedLayer is not { } layer || layer.Items.Count == 0)
        {
            return Edge("This layer is empty.");
        }

        var target = first ? 0 : layer.Items.Count - 1;
        if (target == FocusedItemIndex)
        {
            return Edge(first ? "Already on the first item." : "Already on the last item.");
        }

        FocusedItemId = layer.Items[target].Id;
        Playhead = Timeline.Span(layer, target).Start;
        return new Feedback { Earcon = EarconEvent.Navigate, Playback = PlaybackRequest.Seek };
    }

    private Feedback StepLayer(int direction)
    {
        if (Project.Layers.Count == 0)
        {
            return Edge("No layers.");
        }

        var target = FocusedLayerIndex + direction;
        if (target < 0)
        {
            return Edge("Layer 1 is the first layer.");
        }

        if (target >= Project.Layers.Count)
        {
            return Edge($"Layer {Project.Layers.Count} is the last layer.");
        }

        return GoToLayer(target + 1, listenOnly: false);
    }

    private Feedback ToggleListenAll()
    {
        if (FocusedLayer is null)
        {
            return Error("There are no layers.");
        }

        ListenAll = !ListenAll;
        return new Feedback
        {
            Speech = ListenAll ? "Listening to all layers." : $"Listening to layer {FocusedLayerIndex + 1}, {FocusedLayer.Name}, only.",
            Earcon = EarconEvent.LayerChange,
            Playback = PlaybackRequest.Refresh,
        };
    }

    private Feedback StepMarker(int direction)
    {
        var time = ProjectEditor.NeighbourMarker(Project, Playhead, direction);
        if (time is null)
        {
            return Edge(Timeline.MarkerTimes(Project).Count == 0
                ? "There are no markers. Press B to add one, or the bracket keys for range start and end."
                : direction > 0 ? "No more markers after the playhead." : "No more markers before the playhead.");
        }

        return MarkerFeedback(time.Value);
    }

    private Feedback StepBeat(int direction)
    {
        var time = BeatEditing.NeighbourBeat(Project, Playhead, direction);
        if (time is null)
        {
            return Edge(BeatEditing.BeatTimes(Project).Count == 0
                ? "No beats yet. Select the music and use Find beats, Control Shift B."
                : direction > 0 ? "No more beats after the playhead." : "No more beats before the playhead.");
        }

        Playhead = time.Value;
        var beats = BeatEditing.BeatTimes(Project);
        var number = beats.Count(b => b <= time.Value + 1e-6);
        return new Feedback { Speech = $"Beat {number}, {Describer.Time(time.Value)}.", Earcon = EarconEvent.Navigate, Playback = PlaybackRequest.Seek };
    }

    private Feedback MarkerFeedback(double time)
    {
        Playhead = time;
        return new Feedback
        {
            Speech = $"{ProjectEditor.MarkerLabelAt(Project, time)}, {Describer.Time(time)}.",
            Earcon = EarconEvent.MarkerReached,
            Playback = PlaybackRequest.Seek,
        };
    }

    private Feedback Step(double seconds)
    {
        var target = Math.Clamp(Playhead + seconds, 0, Math.Max(0, ProjectDuration));
        if (Math.Abs(target - Playhead) < 1e-9)
        {
            return Edge(seconds < 0 ? "At the start." : "At the end.");
        }

        Playhead = target;
        return new Feedback { Speech = Describer.Time(Playhead), Playback = PlaybackRequest.Seek };
    }

    private Feedback MovePlayheadTo(double time)
    {
        Playhead = Math.Clamp(time, 0, Math.Max(0, ProjectDuration));
        return new Feedback { Speech = PlayheadText(), Earcon = EarconEvent.Navigate, Playback = PlaybackRequest.Seek };
    }

    private Feedback Slide(double seconds)
    {
        if (FocusedItemIndex < 0)
        {
            return Error("No item selected.");
        }

        var ripple = Settings.Ripple;
        var feedback = ItemEdit("slide", (layer, i) => ProjectEditor.SlideItem(layer, i, seconds, ripple));
        if (!feedback.IsError && FocusedItemIndex >= 0)
        {
            Playhead = Timeline.Span(FocusedLayer!, FocusedItemIndex).Start;
        }

        return feedback;
    }

    private Feedback DeleteItem(bool ripple)
    {
        if (FocusedItemIndex < 0)
        {
            return Error(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        var feedback = ItemEdit("delete item", (layer, i) => ProjectEditor.DeleteItem(layer, i, ripple), EarconEvent.Deleted);
        return feedback;
    }

    private Feedback DeleteRange()
    {
        if (Project.RangeStart is not { } from || Project.RangeEnd is not { } to)
        {
            return Error("Set range start with left bracket and range end with right bracket first.");
        }

        if (FocusedLayerId is not { } id)
        {
            return Error("No layers.");
        }

        var ripple = Settings.Ripple;
        var result = Session.Execute("delete range", p => ProjectEditor.DeleteRange(p.FindLayer(id)!, from, to, ripple));
        if (result.Ok)
        {
            FocusedItemId = result.FocusItemId;
            Playhead = from;
        }

        return ToFeedback(result, EarconEvent.Deleted);
    }

    private Feedback Copy()
    {
        if (FocusedItem is not { } item)
        {
            return Error("No item to copy.");
        }

        _clipboard = item.Clone();
        _clipboardLayerKind = FocusedLayer!.Kind;
        return Say($"Copied {item.Name}.", EarconEvent.Done);
    }

    private Feedback CutItem()
    {
        var copied = Copy();
        if (copied.IsError)
        {
            return copied;
        }

        var deleted = DeleteItem(Settings.Ripple);
        return deleted.IsError ? deleted : deleted with { Speech = "Cut. " + deleted.Speech };
    }

    private Feedback Paste()
    {
        if (_clipboard is null)
        {
            return Error("Nothing has been copied. Copy an item with Control C first.");
        }

        if (FocusedLayer is not { } target)
        {
            return Error("No layers.");
        }

        var copy = _clipboard.Clone();
        if (target.Kind == LayerKind.Audio && copy.Kind is ItemKind.Image or ItemKind.Color)
        {
            return Error("Pictures and colour cards can only be pasted on a video layer.");
        }

        if (target.Kind == LayerKind.Video && copy.Kind == ItemKind.Audio)
        {
            return Error("Audio can only be pasted on an audio layer.");
        }

        if (target.Kind == LayerKind.Video && copy.Kind == ItemKind.Video && _clipboardLayerKind == LayerKind.Audio)
        {
            copy.HasVideo = copy.SourceWidth > 0;
        }
        else if (target.Kind == LayerKind.Audio)
        {
            copy.HasVideo = false;
        }

        var index = FocusedItemIndex;
        var result = Session.Execute("paste", p => ProjectEditor.InsertAfter(p.FindLayer(target.Id)!, index, copy));
        return AfterItemEdit(result, EarconEvent.Done);
    }

    private Feedback Duplicate()
    {
        if (FocusedItem is not { } item)
        {
            return Error("No item to duplicate.");
        }

        var copy = item.Clone();
        copy.Name = item.Name;
        var index = FocusedItemIndex;
        var layerId = FocusedLayerId!.Value;
        var result = Session.Execute("duplicate", p => ProjectEditor.InsertAfter(p.FindLayer(layerId)!, index, copy));
        return AfterItemEdit(result, EarconEvent.Done);
    }

    private Feedback NewLayer(LayerKind kind)
    {
        Guid? id = null;
        var result = Session.Execute("new layer", p =>
        {
            var layer = ProjectEditor.AddLayer(p, kind);
            id = layer.Id;
            return EditResult.Success($"New layer {p.Layers.Count}, {Describer.LayerKindName(kind)}, {layer.Name}.");
        });
        FocusedLayerId = id;
        FocusedItemId = null;
        return ToFeedback(result, EarconEvent.LayerChange);
    }

    private Feedback DeleteLayer()
    {
        if (FocusedLayer is not { } layer)
        {
            return Error("No layers.");
        }

        var count = layer.Items.Count;
        var result = Session.Execute("delete layer", p => ProjectEditor.DeleteLayer(p, layer.Id));
        if (!result.Ok)
        {
            return Error(result.Message);
        }

        FocusedLayerId = result.FocusLayerId;
        FocusedItemId = FocusedLayer is { } next ? NearestItemId(next) : null;
        var items = count == 1 ? "1 item" : $"{count} items";
        return Changed($"{result.Message.TrimEnd('.')} with {items}. Control Z to undo.", EarconEvent.Deleted);
    }

    private Feedback DescribeFocused()
    {
        if (FocusedItemIndex < 0)
        {
            return Say(FocusedLayer is { } layer ? EmptyLayerText(layer) : "No layers.");
        }

        var detailed = new Describer(Verbosity.Detailed, Settings.TimeStyle);
        return Say(detailed.Item(FocusedLayer!, FocusedItemIndex) + ".");
    }

    /// <summary>Runs an edit on the focused item.</summary>
    private Feedback ItemEdit(string label, Func<Layer, int, EditResult> edit, EarconEvent earcon = EarconEvent.Done)
    {
        if (FocusedItemIndex < 0)
        {
            return Error(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        var layerId = FocusedLayerId!.Value;
        var index = FocusedItemIndex;
        var result = Session.Execute(label, p => edit(p.FindLayer(layerId)!, index));
        return AfterItemEdit(result, earcon);
    }

    /// <summary>Runs an edit on the item under the playhead on the focused layer.</summary>
    private Feedback AtPlayhead(string label, Func<Layer, int, EditResult> edit)
    {
        if (FocusedLayer is not { } layer)
        {
            return Error("No layers.");
        }

        var index = FocusedItemIndex >= 0 && Timeline.Span(layer, FocusedItemIndex).Contains(Playhead)
            ? FocusedItemIndex
            : Timeline.ItemAt(layer, Playhead);
        if (index < 0)
        {
            return Error($"Nothing on {layer.Name} at the playhead, {Describer.Time(Playhead)}.");
        }

        var layerId = layer.Id;
        FocusedItemId = layer.Items[index].Id;
        var result = Session.Execute(label, p => edit(p.FindLayer(layerId)!, index));
        return AfterItemEdit(result, EarconEvent.Done);
    }

    private Feedback LayerEdit(string label, Func<Layer, EditResult> edit)
    {
        if (FocusedLayerId is not { } id)
        {
            return Error("No layers.");
        }

        return ToFeedback(Session.Execute(label, p => edit(p.FindLayer(id)!)), EarconEvent.Done);
    }

    private Feedback MuteEdit(Func<Project, EditResult> edit)
    {
        if (FocusedLayerId is null)
        {
            return Error("No layers.");
        }

        var result = Session.Execute("mute", edit);
        if (!result.Ok)
        {
            return Error(result.Message);
        }

        var muted = FocusedLayer?.Muted == true;
        return Changed(result.Message, muted ? EarconEvent.Muted : EarconEvent.Unmuted);
    }

    private Feedback Edit(string label, Func<Project, EditResult> edit, EarconEvent earcon, bool playback = true) =>
        ToFeedback(Session.Execute(label, edit), earcon, playback);

    private Feedback AfterItemEdit(EditResult result, EarconEvent earcon)
    {
        if (result.Ok && result.FocusItemId is { } id)
        {
            FocusedItemId = id;
        }

        return ToFeedback(result, earcon);
    }

    private Feedback UndoRedo(EditResult result)
    {
        if (!result.Ok)
        {
            return Edge(result.Message);
        }

        EnsureFocus();
        return Changed(result.Message, EarconEvent.Undo);
    }

    /// <summary>
    /// Runs an edit from a tool (transcript, captions, pauses, beats, ducking) as one undoable step and
    /// reports it like any other edit.
    /// </summary>
    public Feedback Edit(string label, Func<Project, EditResult> edit, EarconEvent earcon = EarconEvent.Done)
    {
        var result = Session.Execute(label, edit);
        if (result.Ok && result.FocusItemId is { } item)
        {
            FocusedItemId = item;
        }

        return ToFeedback(result, earcon);
    }

    private Feedback ToFeedback(EditResult result, EarconEvent earcon, bool playback = true)
    {
        if (!result.Ok)
        {
            return Error(result.Message);
        }

        if (result.FocusLayerId is { } layer)
        {
            FocusedLayerId = layer;
        }

        EnsureFocus();
        return Changed(result.Message, earcon, playback);
    }

    private Feedback Changed(string message, EarconEvent earcon, bool playback = true)
    {
        EnsureFocus();
        Playhead = Math.Clamp(Playhead, 0, Math.Max(0, ProjectDuration));
        return new Feedback
        {
            Speech = message,
            Earcon = earcon,
            ProjectChanged = true,
            Playback = playback ? PlaybackRequest.Refresh : PlaybackRequest.None,
        };
    }

    private void EnsureFocus()
    {
        if (FocusedLayer is null)
        {
            FocusedLayerId = Project.Layers.FirstOrDefault()?.Id;
            FocusedItemId = null;
        }

        if (FocusedLayer is { } layer && (FocusedItemId is null || layer.IndexOf(FocusedItemId.Value) < 0))
        {
            FocusedItemId = NearestItemId(layer);
        }

        if (FocusedLayer is null)
        {
            FocusedItemId = null;
        }
    }

    private Guid? NearestItemId(Layer layer)
    {
        var index = Timeline.NearestItem(layer, Playhead);
        return index < 0 ? null : layer.Items[index].Id;
    }

    private IEnumerable<Layer> CandidateLayers(Project project, LayerKind kind)
    {
        if (FocusedLayerId is { } id && project.FindLayer(id) is { } focused && focused.Kind == kind)
        {
            yield return focused;
        }

        foreach (var layer in project.Layers.Where(l => l.Kind == kind && l.Id != FocusedLayerId))
        {
            yield return layer;
        }
    }

    private static Feedback Say(string text, EarconEvent? earcon = null) => new() { Speech = text, Earcon = earcon };

    private static Feedback Error(string text) => new() { Speech = text, Earcon = EarconEvent.Error };

    private static Feedback Edge(string text) => new() { Speech = text, Earcon = EarconEvent.Edge };
}
