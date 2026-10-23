using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AccessibleVideoEditor.App.Dialogs;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App;

/// <summary>The areas of the window, in F6 order: as they sit on screen, top left to bottom right.</summary>
public enum Region
{
    Ribbon,
    Library,
    Preview,
    Layers,
    Timeline,
    Properties,
}

public partial class MainWindow
{
    private Guid? _propertiesItemId;
    private Guid? _propertiesLayerId;
    private FieldBuilder? _propertiesFields;
    private Action? _applyProperties;

    // ------------------------------------------------------------------ regions

    internal Region CurrentRegion
    {
        get
        {
            if (Ribbon.IsKeyboardFocusWithin)
            {
                return Region.Ribbon;
            }

            if (LibraryRegion.IsKeyboardFocusWithin)
            {
                return Region.Library;
            }

            if (Transport.IsKeyboardFocusWithin)
            {
                return Region.Preview;
            }

            if (LayersList.IsKeyboardFocusWithin)
            {
                return Region.Layers;
            }

            if (PropertiesRegion.IsKeyboardFocusWithin)
            {
                return Region.Properties;
            }

            if (PreviewRegion.IsKeyboardFocusWithin)
            {
                return Region.Preview;
            }

            return Region.Timeline;
        }
    }

    internal void FocusRegion(Region region)
    {
        switch (region)
        {
            case Region.Ribbon:
                if (Ribbon.SelectedItem is TabItem tab)
                {
                    tab.Focus();
                }

                Say($"Ribbon, {(Ribbon.SelectedItem as TabItem)?.GetValue(AutomationProperties.NameProperty)} tab. Left and Right arrows change tab; Tab goes to its buttons.", interrupt: false);
                break;
            case Region.Library:
                FocusLibrary(null);
                break;
            case Region.Timeline:
                Refresh();
                FocusTimeline();
                if (_editor.FocusedItemId is null && _editor.FocusedLayer is { } layer)
                {
                    Say(_editor.EmptyLayerText(layer), interrupt: false);
                }

                break;
            case Region.Layers:
                FocusLayerList();
                break;
            case Region.Properties:
                RebuildProperties(force: true);
                var first = _propertiesFields?.Controls.FirstOrDefault();
                if (first is not null)
                {
                    first.Focus();
                }
                else
                {
                    PropertiesRegion.Focus();
                }

                Say($"Inspector. {PropertiesTitle.Text}", interrupt: false);
                break;
            case Region.Preview:
                AutomationProperties.SetName(PreviewRegion, "Preview. " + FrameDescriber.Describe(_editor.Project, _editor.Playhead));
                PreviewRegion.Focus();
                break;
        }
    }

    private void CycleRegion(int direction)
    {
        var count = Enum.GetValues<Region>().Length;
        var next = (Region)(((int)CurrentRegion + direction + count) % count);
        FocusRegion(next);
    }

    // ------------------------------------------------------------------ position mode keys

    /// <summary>Handles a key while in position mode. Every key is taken so nothing else happens by accident.</summary>
    private void HandlePositionKey(Key key, ModifierKeys modifiers)
    {
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        (PositionAction Action, int Snap)? action = key switch
        {
            Key.Left when ctrl => (shift ? PositionAction.UncropLeft : PositionAction.CropLeft, 0),
            Key.Right when ctrl => (shift ? PositionAction.UncropRight : PositionAction.CropRight, 0),
            Key.Up when ctrl => (shift ? PositionAction.UncropTop : PositionAction.CropTop, 0),
            Key.Down when ctrl => (shift ? PositionAction.UncropBottom : PositionAction.CropBottom, 0),
            Key.Left => (PositionAction.Left, 0),
            Key.Right => (PositionAction.Right, 0),
            Key.Up => (PositionAction.Up, 0),
            Key.Down => (PositionAction.Down, 0),
            Key.OemPlus or Key.Add => (PositionAction.Bigger, 0),
            Key.OemMinus or Key.Subtract => (PositionAction.Smaller, 0),
            >= Key.D1 and <= Key.D9 => (PositionAction.Snap, key - Key.D0),
            >= Key.NumPad1 and <= Key.NumPad9 => (PositionAction.Snap, key - Key.NumPad0),
            Key.D0 or Key.NumPad0 => (PositionAction.FullFrame, 0),
            Key.F => (PositionAction.ToggleFill, 0),
            Key.R => (PositionAction.ResetCrop, 0),
            Key.O => (shift ? PositionAction.OpacityDown : PositionAction.OpacityUp, 0),
            Key.D or Key.W or Key.T => (PositionAction.Describe, 0),
            Key.Enter or Key.Return => (PositionAction.Done, 0),
            Key.Escape => (PositionAction.Cancel, 0),
            _ => null,
        };

        if (key == Key.F1)
        {
            Say(EditorController.PositionModeHelp);
            return;
        }

        if (action is null)
        {
            _earcons.Play(EarconEvent.Edge);
            Say("In position mode. Press Enter to keep the changes or Escape to undo them. F1 lists the keys.");
            return;
        }

        var (what, snap) = action.Value;
        var fine = shift && what is PositionAction.Left or PositionAction.Right or PositionAction.Up or PositionAction.Down
            or PositionAction.Bigger or PositionAction.Smaller;
        var feedback = _editor.Position(what, fine && !ctrl, snap);
        _ = ApplyAsync(feedback);
    }

    // ------------------------------------------------------------------ properties area

    /// <summary>
    /// Rebuilds the inspector for what is selected, unless the user is typing in it. A selected layer (focus
    /// in the Layers list, or an empty layer) shows layer settings; a selected item shows the options for its
    /// kind: text style for text, placement and corners for pictures, sound for audio.
    /// </summary>
    private void RebuildProperties(bool force = false)
    {
        if (!force && PropertiesRegion.IsKeyboardFocusWithin)
        {
            return;
        }

        var layer = _editor.FocusedLayer;
        var showLayer = LayersList.IsKeyboardFocusWithin || _editor.FocusedItem is null;
        var item = showLayer ? null : _editor.FocusedItem;
        if (!force && _propertiesItemId == item?.Id && _propertiesLayerId == layer?.Id && _propertiesFields is not null)
        {
            return;
        }

        _propertiesItemId = item?.Id;
        _propertiesLayerId = layer?.Id;
        PropertiesPanel.Children.Clear();
        var fields = new FieldBuilder(PropertiesPanel);
        _propertiesFields = fields;
        _applyProperties = null;

        if (layer is null)
        {
            PropertiesTitle.Text = "Inspector: no layers";
            PropertiesIcon.Text = Icons.Layers;
            fields.AddInfo("Add a layer with Control Shift V or Control Shift A, or make a Quick video with Control Shift Q.");
            return;
        }

        if (item is null)
        {
            BuildLayerInspector(fields, layer);
        }
        else
        {
            BuildItemInspector(fields, layer, item);
        }
    }

    /// <summary>For the self-test: rebuild the inspector for the current selection.</summary>
    internal void RebuildInspector() => RebuildProperties(force: true);

    private void BuildLayerInspector(FieldBuilder fields, Layer layer)
    {
        var layerId = layer.Id;
        PropertiesIcon.Text = Icons.ForLayer(layer.Kind, layer.Muted);
        PropertiesTitle.Text = $"Layer {_editor.FocusedLayerIndex + 1}: {layer.Name}";
        fields.AddInfo(_editor.Describer.Layer(_editor.Project, layer));
        fields.AddHeading("Layer", Icons.Layers);
        var name = fields.AddText("Layer name", layer.Name);
        var volume = fields.AddNumber("Layer volume, percent", Math.Round(layer.Volume * 100), 0, 400);
        var muted = fields.AddCheck("Mute this layer", layer.Muted);

        _applyProperties = () =>
        {
            if (Invalid(fields))
            {
                return;
            }

            var feedback = _editor.Edit("layer settings", project =>
            {
                var target = project.FindLayer(layerId);
                if (target is null)
                {
                    return EditResult.Fail("That layer no longer exists.");
                }

                target.Name = string.IsNullOrWhiteSpace(name()) ? target.Name : name().Trim();
                target.Volume = volume() / 100;
                target.Muted = muted();
                return EditResult.Success($"{target.Name}: volume {volume():0} percent{(target.Muted ? ", muted" : "")}.");
            });
            _ = ApplyAsync(feedback);
            RebuildProperties(force: true);
        };
        fields.AddButton("Apply changes", () => _applyProperties?.Invoke(), "Or press Enter in any field.");

        if (layer.Kind == LayerKind.Audio)
        {
            fields.AddHeading("Smart sound", Icons.Volume);
            var dips = layer.Items.Sum(i => i.VolumeDips.Count);
            fields.AddInfo(dips == 0 ? "Full volume everywhere." : $"Lowered under speech in {dips} places.");
            fields.AddButton("Lower under speech...", () => _ = RunAsync(CommandId.AutoDuck));
        }
        else
        {
            fields.AddHeading("Timing", Icons.Music);
            fields.AddButton("Cut this layer to the beat...", () => _ = RunAsync(CommandId.CutToBeats));
        }

        if (layer.Items.Any(i => _editor.Project.TranscriptFor(i.Path) is not null))
        {
            fields.AddHeading("Speech", Icons.Captions);
            fields.AddButton("Edit by transcript...", () => _ = RunAsync(CommandId.TranscriptEditor));
            fields.AddButton("Make captions", () => _ = RunAsync(CommandId.MakeCaptions));
        }
        else if (layer.Items.Any(i => i.HasAudio))
        {
            fields.AddHeading("Speech", Icons.Captions);
            fields.AddButton("Transcribe speech...", () => _ = RunAsync(CommandId.Transcribe));
        }

        fields.AddButton("All layer options...", () => _ = RunAsync(CommandId.LayerProperties));
    }

    private void BuildItemInspector(FieldBuilder fields, Layer layer, Item item)
    {
        var isText = item.Kind == ItemKind.Text;
        var picture = layer.Kind == LayerKind.Video && (item.HasVideo || item.Kind is ItemKind.Text or ItemKind.Color);
        var sound = item.HasAudio && item.Kind is ItemKind.Video or ItemKind.Audio;
        PropertiesIcon.Text = Icons.ForItem(item.Kind, layer.Kind);
        var kind = item.Kind switch
        {
            ItemKind.Text => "Text",
            ItemKind.Image => "Picture",
            ItemKind.Color => "Colour card",
            ItemKind.Audio => "Sound",
            _ when layer.Kind == LayerKind.Audio => "Sound",
            _ => "Video",
        };
        PropertiesTitle.Text = $"{kind}: {FrameDescriber.Name(item)}, layer {_editor.FocusedLayerIndex + 1}";
        fields.AddInfo(_editor.Describer.Item(layer, _editor.FocusedItemIndex));
        var colorNames = ColorCatalog.Colors.Select(c => c.Name).ToList();

        // Content and timing.
        fields.AddHeading(isText ? "Words" : "Clip", isText ? Icons.Text : Icons.ForItem(item.Kind, layer.Kind));
        var content = isText ? fields.AddText("Text", item.Text.Content, multiline: true) : fields.AddText("Name", item.Name);
        var length = fields.AddTime("Length", item.Duration, Timeline.MinItemDuration);
        var fadeIn = fields.AddTime("Fade in", item.FadeIn);
        var fadeOut = fields.AddTime("Fade out", item.FadeOut);

        // Text style.
        Func<int>? font = null, color = null, background = null, animIn = null, animOut = null;
        Func<double>? textSize = null;
        Func<bool>? bold = null, outline = null;
        if (isText)
        {
            fields.AddHeading("Text style", Icons.Text);
            font = fields.AddChoice("Font", FontCatalog.Fonts.Select(f => $"{f.Name}. {f.Description}").ToList(),
                FontCatalog.Fonts.ToList().FindIndex(f => f.Name == FontCatalog.Find(item.Text.Font).Name));
            textSize = fields.AddNumber("Text size, percent of the frame height", item.Text.SizePercent, 1, 50, "8 is a good title, 5 a caption.");
            bold = fields.AddCheck("Bold", item.Text.Bold);
            color = fields.AddChoice("Text colour", colorNames, Math.Max(0, colorNames.FindIndex(c => c == item.Text.ColorName)));
            outline = fields.AddCheck("Dark outline around the letters", item.Text.Outline);
            background = fields.AddChoice("Background behind the text", Enum.GetValues<TextBackground>().Select(TextAnimationCatalog.BackgroundName).ToList(), (int)item.Text.Background);
            fields.AddHeading("Animation", Icons.Animation);
            animIn = fields.AddChoice("Comes in by", TextAnimationCatalog.All.Select(TextAnimationCatalog.InName).ToList(), (int)item.Text.AnimationIn);
            animOut = fields.AddChoice("Goes out by", TextAnimationCatalog.All.Select(TextAnimationCatalog.OutName).ToList(), (int)item.Text.AnimationOut);
        }

        // Placement on screen.
        Func<int>? position = null;
        Func<double>? size = null, opacity = null, margin = null, radius = null, cropLeft = null, cropRight = null, cropTop = null, cropBottom = null;
        Func<bool>? fill = null;
        if (picture)
        {
            fields.AddHeading("Position on screen", Icons.Move);
            position = fields.AddChoice("Position", Enum.GetValues<PositionPreset>().Select(Describer.PositionName).ToList(), (int)item.Placement.Position,
                "For exact placement press P in the timeline for position mode.");
            if (!isText)
            {
                size = fields.AddNumber("Size, percent of the frame", item.Placement.SizePercent, 5, 100, "Not used for full frame.");
                fill = fields.AddCheck("Fill the frame (full frame only), cutting off the edges", item.Placement.Fill);
            }

            margin = fields.AddNumber("Margin from the frame edge, percent", item.Placement.MarginPercent, 0, 40, "For corner and side positions. 4 is the usual.");
            opacity = fields.AddNumber("Opacity, percent", item.Placement.OpacityPercent, 0, 100);
            if (!isText)
            {
                radius = fields.AddNumber("Rounded corners, percent", item.Placement.CornerRadiusPercent, 0, 50, "0 is square, 50 makes a circle or pill.");
                fields.AddHeading("Crop", Icons.Cut);
                cropLeft = fields.AddNumber("Crop from the left, percent", item.Placement.CropLeft, 0, 90);
                cropRight = fields.AddNumber("Crop from the right, percent", item.Placement.CropRight, 0, 90);
                cropTop = fields.AddNumber("Crop from the top, percent", item.Placement.CropTop, 0, 90);
                cropBottom = fields.AddNumber("Crop from the bottom, percent", item.Placement.CropBottom, 0, 90);
            }
        }

        // Sound.
        Func<double>? volume = null;
        Func<bool>? mute = null, normalize = null;
        Func<int>? noise = null, voice = null;
        if (sound)
        {
            fields.AddHeading("Sound", Icons.Volume);
            volume = fields.AddNumber("Volume, percent", Math.Round(item.Volume * 100), 0, 400, "100 is unchanged.");
            mute = fields.AddCheck("Mute this item's sound", item.AudioMuted);
            noise = fields.AddChoice("Noise reduction", Enum.GetValues<NoiseReduction>().Select(AudioFilters.NoiseReductionName).ToList(), (int)item.Audio.NoiseReduction);
            voice = fields.AddChoice("Voice preset", Enum.GetValues<VoicePreset>().Select(AudioFilters.VoiceName).ToList(), (int)item.Audio.Voice);
            normalize = fields.AddCheck("Even out loudness", item.Audio.Normalize);
        }

        // Transition from the item before.
        Func<int>? transition = null;
        Func<double>? transitionLength = null;
        var entries = layer.Kind == LayerKind.Video ? TransitionCatalog.Video : TransitionCatalog.Audio;
        if (_editor.FocusedItemIndex > 0)
        {
            fields.AddHeading("Transition in", Icons.Transition);
            var current = layer.Kind == LayerKind.Video ? item.TransitionIn.Video : item.TransitionIn.Audio;
            transition = fields.AddChoice(layer.Kind == LayerKind.Video ? "Transition from the item before" : "Crossfade from the item before",
                entries.Select(e => e.Name).ToList(), Math.Max(0, entries.ToList().FindIndex(e => e.Value == current)));
            transitionLength = fields.AddTime("Transition length", item.TransitionIn.Duration, 0.1);
        }

        _applyProperties = () =>
        {
            if (Invalid(fields))
            {
                return;
            }

            var edited = item.Clone(newId: false);
            if (isText)
            {
                edited.Text.Content = content();
            }
            else
            {
                edited.Name = content();
            }

            edited.Duration = length();
            edited.FadeIn = fadeIn();
            edited.FadeOut = fadeOut();
            if (isText)
            {
                edited.Text.Font = FontCatalog.Fonts[font!()].Name;
                edited.Text.SizePercent = textSize!();
                edited.Text.Bold = bold!();
                edited.Text.ColorName = colorNames[color!()];
                edited.Text.Outline = outline!();
                edited.Text.Background = (TextBackground)background!();
                edited.Text.AnimationIn = (TextAnimation)animIn!();
                edited.Text.AnimationOut = (TextAnimation)animOut!();
            }

            if (position is not null)
            {
                edited.Placement.Position = (PositionPreset)position();
                edited.Placement.MarginPercent = margin!();
                edited.Placement.OpacityPercent = opacity!();
                if (size is not null)
                {
                    edited.Placement.SizePercent = size();
                    edited.Placement.Fill = fill!();
                    edited.Placement.CornerRadiusPercent = radius!();
                    edited.Placement.CropLeft = cropLeft!();
                    edited.Placement.CropRight = cropRight!();
                    edited.Placement.CropTop = cropTop!();
                    edited.Placement.CropBottom = cropBottom!();
                }
            }

            if (volume is not null)
            {
                edited.Volume = volume() / 100;
                edited.AudioMuted = mute!();
                edited.Audio.NoiseReduction = (NoiseReduction)noise!();
                edited.Audio.Voice = (VoicePreset)voice!();
                edited.Audio.Normalize = normalize!();
            }

            if (transition is not null)
            {
                var value = entries[transition()].Value;
                if (layer.Kind == LayerKind.Video)
                {
                    edited.TransitionIn.Video = value;
                    edited.TransitionIn.Audio = value == TransitionSpec.None ? TransitionSpec.None : "tri";
                }
                else
                {
                    edited.TransitionIn.Audio = value;
                }

                edited.TransitionIn.Duration = transitionLength!();
            }

            var index = PropertiesPanel.Children.OfType<Control>().ToList().FindIndex(c => c.IsKeyboardFocusWithin);
            _ = ApplyAsync(_editor.UpdateFocusedItem(edited));
            RebuildProperties(force: true);
            var controls = PropertiesPanel.Children.OfType<Control>().ToList();
            if (index >= 0 && index < controls.Count)
            {
                controls[index].Focus();
            }
        };

        fields.AddButton("Apply changes", () => _applyProperties?.Invoke(), "Or press Enter in any field (Control Enter in the text box).");

        // Smart tools for this kind of item.
        if (sound)
        {
            fields.AddHeading("Smart tools", Icons.Lightning);
            var transcript = _editor.Project.TranscriptFor(item.Path);
            fields.AddInfo(transcript is null
                ? "No transcript yet."
                : $"Transcript: {transcript.WordsBetween(item.SourceIn, item.SourceOut).Count()} words in {Transcript.LanguageName(transcript.Language)}.");
            fields.AddButton(transcript is null ? "Transcribe speech..." : "Edit by transcript...",
                () => _ = RunAsync(transcript is null ? CommandId.Transcribe : CommandId.TranscriptEditor));
            fields.AddButton("Shorten pauses...", () => _ = RunAsync(CommandId.ShortenPauses));
            fields.AddInfo(item.Bpm > 0 ? $"Music: {item.Bpm:0} beats per minute, {item.Beats.Count} beats found." : "Beats not found yet.");
            fields.AddButton("Find beats", () => _ = RunAsync(CommandId.FindBeats));
            if (item.VolumeDips.Count > 0)
            {
                fields.AddInfo($"Lowered under speech in {item.VolumeDips.Count} places.");
            }
        }

        fields.AddButton("All options...", () => _ = RunAsync(CommandId.ItemProperties));
        if (picture)
        {
            fields.AddButton("Position mode", () =>
            {
                FocusTimeline();
                _ = RunAsync(CommandId.PositionMode);
            });
        }
    }

    private bool Invalid(FieldBuilder fields)
    {
        if (fields.Validate() is not { } problem)
        {
            return false;
        }

        _earcons.Play(EarconEvent.Error);
        Say(problem.Message);
        problem.Control.Focus();
        return true;
    }

    private void OnPropertiesKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        var multiline = e.OriginalSource is TextBox { AcceptsReturn: true };
        if (multiline && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        if (e.OriginalSource is Button)
        {
            return;
        }

        e.Handled = true;
        _applyProperties?.Invoke();
    }
}
