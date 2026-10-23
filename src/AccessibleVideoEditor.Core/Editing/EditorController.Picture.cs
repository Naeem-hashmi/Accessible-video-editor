using System.Globalization;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Editing;

/// <summary>Keys available while in position mode.</summary>
public enum PositionAction
{
    Left,
    Right,
    Up,
    Down,
    Bigger,
    Smaller,

    /// <summary>Snap to a place on a 3 by 3 grid laid out like a number pad: 7 top left, 5 centre, 3 bottom right.</summary>
    Snap,

    /// <summary>Make the picture fill the frame ("maximise").</summary>
    FullFrame,

    /// <summary>Full frame only: switch between showing the whole picture and filling the frame.</summary>
    ToggleFill,

    CropLeft,
    CropRight,
    CropTop,
    CropBottom,
    UncropLeft,
    UncropRight,
    UncropTop,
    UncropBottom,
    ResetCrop,
    OpacityUp,
    OpacityDown,
    Describe,
    Done,
    Cancel,
}

public sealed partial class EditorController
{
    private int _positionSteps;

    /// <summary>True while arrow keys move the focused picture instead of moving around the timeline.</summary>
    public bool InPositionMode { get; private set; }

    public static string PositionModeHelp =>
        "Arrows move it, Shift with arrows moves a little. Plus and minus change the size. " +
        "Number keys 1 to 9 snap it to a place like a number pad: 7 top left, 8 top centre, 9 top right, 5 centre, 1 bottom left, 3 bottom right. " +
        "0 makes it full frame, F switches between fit and fill. Control arrows crop that edge, Control Shift arrows uncrop, R removes the crop. " +
        "O and Shift O change opacity. D describes. Enter keeps the changes, Escape undoes them.";

    private Feedback? ExecuteMore(CommandId command) => command switch
    {
        CommandId.ItemVolumeUp => ChangeItemVolume(0.1),
        CommandId.ItemVolumeDown => ChangeItemVolume(-0.1),
        CommandId.ToggleItemMute => ToggleItemMute(),
        CommandId.LayerVolumeUp => ChangeLayerVolume(0.1),
        CommandId.LayerVolumeDown => ChangeLayerVolume(-0.1),
        CommandId.PositionMode => EnterPositionMode(),
        CommandId.DescribePicture => Say(FrameDescriber.Describe(Project, Playhead)),
        _ => null,
    };

    // ------------------------------------------------------------------ sound

    private Feedback ChangeItemVolume(double delta)
    {
        if (FocusedItem is not { } item)
        {
            return Error(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        if (!item.HasAudio)
        {
            return Error($"{item.Name} has no sound.");
        }

        var target = Math.Round(Math.Clamp(item.Volume + delta, 0, 4), 2);
        if (Math.Abs(target - item.Volume) < 0.001)
        {
            return Edge(delta > 0 ? "Already at the loudest, 400 percent." : "Already silent, 0 percent.");
        }

        var layerId = FocusedLayerId!.Value;
        var index = FocusedItemIndex;
        var result = Session.Execute("item volume", p =>
        {
            var it = p.FindLayer(layerId)!.Items[index];
            it.Volume = target;
            if (target > 0)
            {
                it.AudioMuted = false;
            }

            return EditResult.Success($"Volume {Describer.Percent(target)}.");
        });
        return ToFeedback(result, delta > 0 ? EarconEvent.Unmuted : EarconEvent.Muted);
    }

    private Feedback ToggleItemMute()
    {
        if (FocusedItem is not { } item)
        {
            return Error(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        if (!item.HasAudio)
        {
            return Error($"{item.Name} has no sound.");
        }

        var layerId = FocusedLayerId!.Value;
        var index = FocusedItemIndex;
        var muted = !item.AudioMuted;
        var result = Session.Execute("mute item", p =>
        {
            p.FindLayer(layerId)!.Items[index].AudioMuted = muted;
            return EditResult.Success(muted ? $"{item.Name} sound muted." : $"{item.Name} sound on, volume {Describer.Percent(item.Volume)}.");
        });
        return ToFeedback(result, muted ? EarconEvent.Muted : EarconEvent.Unmuted);
    }

    private Feedback ChangeLayerVolume(double delta)
    {
        if (FocusedLayer is not { } layer)
        {
            return Error("No layers.");
        }

        var target = Math.Round(Math.Clamp(layer.Volume + delta, 0, 4), 2);
        if (Math.Abs(target - layer.Volume) < 0.001)
        {
            return Edge(delta > 0 ? "Layer already at the loudest, 400 percent." : "Layer already silent.");
        }

        var id = layer.Id;
        var result = Session.Execute("layer volume", p =>
        {
            p.FindLayer(id)!.Volume = target;
            return EditResult.Success($"{layer.Name} volume {Describer.Percent(target)}.");
        });
        return ToFeedback(result, delta > 0 ? EarconEvent.Unmuted : EarconEvent.Muted);
    }

    // ------------------------------------------------------------------ text

    /// <summary>
    /// Adds text at the playhead on a video layer above the main one where there is room, or on a new
    /// "Text" layer at the top.
    /// </summary>
    public Feedback AddText(TextStyle style, double duration, Placement placement)
    {
        if (string.IsNullOrWhiteSpace(style.Content))
        {
            return Error("Type some text first.");
        }

        Guid? itemId = null;
        Guid? layerId = null;
        var time = Playhead;
        var result = Session.Execute("add text", project =>
        {
            var item = MediaProbe.CreateTextItem(ProjectEditor.ClampText(style), duration, new LayerDefaults(), ProjectEditor.ClampPlacement(placement));
            var lowestVideo = project.Layers.FindIndex(l => l.Kind == LayerKind.Video);
            // Reuse a layer that already holds text (or is named for it), never a logo or picture layer.
            static bool IsTextLayer(Layer l) => l.Items.Any(i => i.Kind == ItemKind.Text) ||
                                                 l.Name.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                                                 l.Name.Contains("title", StringComparison.OrdinalIgnoreCase);
            var layer = project.Layers
                .Where((l, i) => l.Kind == LayerKind.Video && i > lowestVideo && IsTextLayer(l) &&
                                 l.Items.All(x => x.Kind == ItemKind.Text) && ProjectEditor.HasRoomAt(l, time, item.Duration))
                .LastOrDefault();
            if (layer is null)
            {
                layer = ProjectEditor.AddLayer(project, LayerKind.Video, UniqueName(project, "Text"));
                layer.Defaults.Placement = new Placement { Position = PositionPreset.BottomCenter };
            }

            itemId = item.Id;
            layerId = layer.Id;
            var added = ProjectEditor.InsertAtTime(layer, item, time);
            return added.Ok
                ? EditResult.Success($"Added {FrameDescriber.Name(item)} on layer {project.Layers.IndexOf(layer) + 1} at {Describer.Time(time)}, {FrameDescriber.Placement(item, project.Width, project.Height)}.")
                : added;
        });

        if (!result.Ok)
        {
            return Error(result.Message);
        }

        FocusedLayerId = layerId;
        FocusedItemId = itemId;
        return Changed(result.Message, EarconEvent.Done);
    }

    private static string UniqueName(Project project, string name)
    {
        var candidate = name;
        var n = 2;
        while (project.Layers.Any(l => l.Name == candidate))
        {
            candidate = $"{name} {n++}";
        }

        return candidate;
    }

    // ------------------------------------------------------------------ position mode

    private Feedback EnterPositionMode()
    {
        if (FocusedItem is not { } item || FocusedLayer is not { } layer)
        {
            return Error(FocusedLayer is null ? "No layers." : "This layer is empty.");
        }

        if (layer.Kind != LayerKind.Video || (!item.HasVideo && item.Kind is not (ItemKind.Text or ItemKind.Color)))
        {
            return Error("Position mode is for pictures, videos and text on a video layer.");
        }

        InPositionMode = true;
        _positionSteps = 0;
        return new Feedback
        {
            Speech = $"Position mode for {FrameDescriber.Name(item)}. Now {PlacementNow(item)}. {PositionModeHelp}",
            Earcon = EarconEvent.LayerChange,
            Position = Centre(item),
        };
    }

    /// <summary>Runs one position-mode key. <paramref name="snap"/> is the number key for <see cref="PositionAction.Snap"/>.</summary>
    public Feedback Position(PositionAction action, bool fine = false, int snap = 0)
    {
        if (!InPositionMode || FocusedItem is not { } item)
        {
            InPositionMode = false;
            return Error("Not in position mode. Press P on a picture or text first.");
        }

        switch (action)
        {
            case PositionAction.Done:
                InPositionMode = false;
                return new Feedback { Speech = $"Position kept. {PlacementNow(item)}.", Earcon = EarconEvent.Done, Playback = PlaybackRequest.Refresh };
            case PositionAction.Cancel:
                InPositionMode = false;
                var undone = 0;
                for (; _positionSteps > 0; _positionSteps--)
                {
                    if (Session.Undo().Ok)
                    {
                        undone++;
                    }
                }

                EnsureFocus();
                return new Feedback
                {
                    Speech = undone == 0 ? "Position mode closed, nothing changed." : "Position mode cancelled. The picture is back where it was.",
                    Earcon = EarconEvent.Undo,
                    ProjectChanged = undone > 0,
                };
            case PositionAction.Describe:
                return new Feedback { Speech = $"{PlacementNow(item)}. {FrameDescriber.Describe(Project, Playhead)}", Position = Centre(item) };
        }

        var placement = item.Placement.Clone();
        var text = item.Text.Clone();
        var isText = item.Kind == ItemKind.Text;
        var step = fine ? 1.0 : 5.0;
        var w = Project.Width;
        var h = Project.Height;
        var rect = Geometry.RectOf(item, w, h);
        string? problem = null;

        void ToCustom()
        {
            if (placement.Position != PositionPreset.Custom)
            {
                placement.CustomXPercent = rect.X / w * 100;
                placement.CustomYPercent = rect.Y / h * 100;
                placement.Position = PositionPreset.Custom;
            }
        }

        void Move(double dx, double dy)
        {
            if (!isText && placement.Position == PositionPreset.FullFrame)
            {
                problem = "A full frame picture can't move. Press minus to make it smaller first, or a number key to put it in a corner.";
                return;
            }

            ToCustom();
            var maxX = Math.Max(0, 100 - rect.Width / w * 100);
            var maxY = Math.Max(0, 100 - rect.Height / h * 100);
            var nx = Math.Clamp(placement.CustomXPercent + dx, 0, maxX);
            var ny = Math.Clamp(placement.CustomYPercent + dy, 0, maxY);
            if (Math.Abs(nx - placement.CustomXPercent) < 0.01 && Math.Abs(ny - placement.CustomYPercent) < 0.01)
            {
                problem = dx < 0 ? "Already at the left edge." : dx > 0 ? "Already at the right edge." : dy < 0 ? "Already at the top edge." : "Already at the bottom edge.";
                return;
            }

            placement.CustomXPercent = nx;
            placement.CustomYPercent = ny;
        }

        void Resize(double delta)
        {
            if (isText)
            {
                var size = Math.Clamp(text.SizePercent + delta / 5 * (fine ? 0.5 : 1), 1, 50);
                if (Math.Abs(size - text.SizePercent) < 0.001)
                {
                    problem = delta > 0 ? "The text is already as big as it can be." : "The text is already as small as it can be.";
                }

                text.SizePercent = size;
                return;
            }

            if (placement.Position == PositionPreset.FullFrame)
            {
                if (delta > 0)
                {
                    problem = "Already full frame.";
                    return;
                }

                placement.Position = PositionPreset.Center;
                placement.SizePercent = 100 + delta;
                return;
            }

            var newSize = Math.Clamp(placement.SizePercent + delta, 5, 100);
            if (Math.Abs(newSize - placement.SizePercent) < 0.001)
            {
                problem = delta > 0 ? "Already at the largest size. Press 0 for full frame." : "Already at the smallest size, 5 percent.";
                return;
            }

            if (placement.Position == PositionPreset.Custom)
            {
                // Grow and shrink around the centre rather than the top left corner.
                var ratio = newSize / placement.SizePercent;
                var cx = (rect.X + rect.Width / 2) / w * 100;
                var cy = (rect.Y + rect.Height / 2) / h * 100;
                placement.CustomXPercent = Math.Max(0, cx - rect.Width * ratio / w * 50);
                placement.CustomYPercent = Math.Max(0, cy - rect.Height * ratio / h * 50);
            }

            placement.SizePercent = newSize;
        }

        void Crop(ref double edge, double other, double delta, string name)
        {
            if (isText)
            {
                problem = "Text can't be cropped.";
                return;
            }

            var value = Math.Clamp(edge + delta, 0, 90 - other);
            if (Math.Abs(value - edge) < 0.001)
            {
                problem = delta > 0 ? $"Can't crop more from the {name}." : $"Nothing is cropped from the {name}.";
                return;
            }

            edge = value;
        }

        double cropStep = 5;
        double left = placement.CropLeft, right = placement.CropRight, top = placement.CropTop, bottom = placement.CropBottom;
        switch (action)
        {
            case PositionAction.Left: Move(-step, 0); break;
            case PositionAction.Right: Move(step, 0); break;
            case PositionAction.Up: Move(0, -step); break;
            case PositionAction.Down: Move(0, step); break;
            case PositionAction.Bigger: Resize(step); break;
            case PositionAction.Smaller: Resize(-step); break;
            case PositionAction.Snap:
                var preset = snap switch
                {
                    7 => PositionPreset.TopLeft,
                    8 => PositionPreset.TopCenter,
                    9 => PositionPreset.TopRight,
                    4 => PositionPreset.MiddleLeft,
                    5 => PositionPreset.Center,
                    6 => PositionPreset.MiddleRight,
                    1 => PositionPreset.BottomLeft,
                    2 => PositionPreset.BottomCenter,
                    3 => PositionPreset.BottomRight,
                    _ => (PositionPreset?)null,
                };
                if (preset is null)
                {
                    problem = "Use number keys 1 to 9 to snap, or 0 for full frame.";
                    break;
                }

                if (placement.Position == PositionPreset.FullFrame && !isText)
                {
                    placement.SizePercent = 30;
                }

                placement.Position = preset.Value;
                break;
            case PositionAction.FullFrame:
                if (isText)
                {
                    placement.Position = PositionPreset.Center;
                }
                else if (placement.Position == PositionPreset.FullFrame)
                {
                    problem = "Already full frame.";
                }
                else
                {
                    placement.Position = PositionPreset.FullFrame;
                    placement.SizePercent = 100;
                }

                break;
            case PositionAction.ToggleFill:
                if (isText || placement.Position != PositionPreset.FullFrame)
                {
                    problem = "Fit and fill are for full frame pictures. Press 0 first.";
                }
                else
                {
                    placement.Fill = !placement.Fill;
                }

                break;
            case PositionAction.CropLeft: Crop(ref left, right, cropStep, "left"); break;
            case PositionAction.CropRight: Crop(ref right, left, cropStep, "right"); break;
            case PositionAction.CropTop: Crop(ref top, bottom, cropStep, "top"); break;
            case PositionAction.CropBottom: Crop(ref bottom, top, cropStep, "bottom"); break;
            case PositionAction.UncropLeft: Crop(ref left, right, -cropStep, "left"); break;
            case PositionAction.UncropRight: Crop(ref right, left, -cropStep, "right"); break;
            case PositionAction.UncropTop: Crop(ref top, bottom, -cropStep, "top"); break;
            case PositionAction.UncropBottom: Crop(ref bottom, top, -cropStep, "bottom"); break;
            case PositionAction.ResetCrop:
                if (!placement.HasCrop)
                {
                    problem = "Nothing is cropped.";
                }

                left = right = top = bottom = 0;
                break;
            case PositionAction.OpacityUp:
            case PositionAction.OpacityDown:
                var opacity = Math.Clamp(placement.OpacityPercent + (action == PositionAction.OpacityUp ? 10 : -10), 0, 100);
                if (Math.Abs(opacity - placement.OpacityPercent) < 0.001)
                {
                    problem = action == PositionAction.OpacityUp ? "Already fully solid." : "Already invisible.";
                }

                placement.OpacityPercent = opacity;
                break;
        }

        if (problem is not null)
        {
            return new Feedback { Speech = problem, Earcon = EarconEvent.Edge, Position = Centre(item) };
        }

        placement.CropLeft = left;
        placement.CropRight = right;
        placement.CropTop = top;
        placement.CropBottom = bottom;

        var layerId = FocusedLayerId!.Value;
        var index = FocusedItemIndex;
        var result = Session.Execute("position", p =>
        {
            var target = p.FindLayer(layerId)!.Items[index];
            target.Placement = ProjectEditor.ClampPlacement(placement);
            target.Text = ProjectEditor.ClampText(text);
            return EditResult.Success("");
        });
        if (!result.Ok)
        {
            return Error(result.Message);
        }

        _positionSteps++;
        var updated = FocusedItem!;
        return new Feedback
        {
            Speech = StepSpeech(action, updated),
            ProjectChanged = true,
            Position = Centre(updated),
        };
    }

    private string StepSpeech(PositionAction action, Item item)
    {
        var p = item.Placement;
        var rect = Geometry.RectOf(item, Project.Width, Project.Height);
        string Pct(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture) + " percent";
        return action switch
        {
            PositionAction.Left or PositionAction.Right or PositionAction.Up or PositionAction.Down =>
                $"{Pct(rect.X / Project.Width * 100)} across, {Pct(rect.Y / Project.Height * 100)} down{EdgesNote(rect)}.",
            PositionAction.Bigger or PositionAction.Smaller when item.Kind == ItemKind.Text =>
                $"Text size {item.Text.SizePercent.ToString("0.#", CultureInfo.InvariantCulture)} percent of the frame height.",
            PositionAction.Bigger or PositionAction.Smaller =>
                $"Size {Pct(p.SizePercent)}, {Pct(rect.Width / Project.Width * 100)} wide and {Pct(rect.Height / Project.Height * 100)} high{EdgesNote(rect)}.",
            PositionAction.OpacityUp or PositionAction.OpacityDown => $"Opacity {Pct(p.OpacityPercent)}.",
            PositionAction.ToggleFill => p.Fill ? "Fill: covers the whole frame, edges cut off." : "Fit: the whole picture shows, with bars if the shape differs.",
            _ => PlacementNow(item) + ".",
        };
    }

    private string EdgesNote(FrameRect rect)
    {
        var w = Project.Width;
        var h = Project.Height;
        var tolerance = Math.Max(2, Math.Min(w, h) * 0.01);
        var edges = new List<string>();
        if (rect.X <= tolerance)
        {
            edges.Add("left");
        }

        if (rect.Right >= w - tolerance)
        {
            edges.Add("right");
        }

        if (rect.Y <= tolerance)
        {
            edges.Add("top");
        }

        if (rect.Bottom >= h - tolerance)
        {
            edges.Add("bottom");
        }

        return edges.Count == 0 ? "" : $", touching the {string.Join(" and ", edges)} edge{(edges.Count > 1 ? "s" : "")}";
    }

    private string PlacementNow(Item item) => FrameDescriber.Placement(item, Project.Width, Project.Height);

    private (double X, double Y) Centre(Item item)
    {
        var rect = Geometry.RectOf(item, Project.Width, Project.Height);
        return (rect.CenterX / Project.Width, rect.CenterY / Project.Height);
    }

    /// <summary>Leaves position mode without undoing (for example when the project is replaced).</summary>
    private void ResetPositionMode()
    {
        InPositionMode = false;
        _positionSteps = 0;
    }
}
