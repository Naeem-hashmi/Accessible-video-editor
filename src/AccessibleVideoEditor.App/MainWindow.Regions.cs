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
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App;

/// <summary>The areas of the window, in F6 order.</summary>
public enum Region
{
    Library,
    Timeline,
    Layers,
    Properties,
    Preview,
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
            if (LibraryRegion.IsKeyboardFocusWithin)
            {
                return Region.Library;
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

                Say($"Properties. {PropertiesTitle.Text}", interrupt: false);
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

    /// <summary>Rebuilds the properties area for the focused item, unless the user is typing in it.</summary>
    private void RebuildProperties(bool force = false)
    {
        if (!force && PropertiesRegion.IsKeyboardFocusWithin)
        {
            return;
        }

        var item = _editor.FocusedItem;
        var layer = _editor.FocusedLayer;
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

        if (item is null || layer is null)
        {
            PropertiesTitle.Text = layer is null ? "Properties: no layers." : "Properties: this layer is empty.";
            fields.AddInfo(layer is null ? "Add a layer with Control Shift V or Control Shift A." : "Add files with Control I, or text with Control Shift T.");
            return;
        }

        PropertiesTitle.Text = $"Properties of {FrameDescriber.Name(item)}, layer {_editor.FocusedLayerIndex + 1}";
        fields.AddInfo(_editor.Describer.Item(layer, _editor.FocusedItemIndex));

        var isText = item.Kind == ItemKind.Text;
        var picture = layer.Kind == LayerKind.Video && (item.HasVideo || item.Kind is ItemKind.Text or ItemKind.Color);
        var content = isText ? fields.AddText("Text", item.Text.Content, multiline: true) : fields.AddText("Name", item.Name);
        var length = fields.AddTime("Length", item.Duration, Timeline.MinItemDuration);
        var fadeIn = fields.AddTime("Fade in", item.FadeIn);
        var fadeOut = fields.AddTime("Fade out", item.FadeOut);

        Func<double>? volume = null;
        Func<bool>? mute = null;
        if (item.HasAudio)
        {
            volume = fields.AddNumber("Volume, percent", Math.Round(item.Volume * 100), 0, 400, "100 is unchanged. Control Up and Control Down change it from the timeline.");
            mute = fields.AddCheck("Mute this item's sound", item.AudioMuted);
        }

        Func<int>? position = null;
        Func<double>? size = null, opacity = null;
        if (picture)
        {
            position = fields.AddChoice("Position", Enum.GetValues<PositionPreset>().Select(Describer.PositionName).ToList(), (int)item.Placement.Position,
                "For exact placement press P in the timeline for position mode.");
            if (!isText)
            {
                size = fields.AddNumber("Size, percent of the frame", item.Placement.SizePercent, 5, 100);
            }

            opacity = fields.AddNumber("Opacity, percent", item.Placement.OpacityPercent, 0, 100);
        }

        Func<int>? font = null, color = null, animIn = null, animOut = null;
        Func<double>? textSize = null;
        var colorNames = ColorCatalog.Colors.Select(c => c.Name).ToList();
        if (isText)
        {
            font = fields.AddChoice("Font", FontCatalog.Fonts.Select(f => $"{f.Name}. {f.Description}").ToList(),
                FontCatalog.Fonts.ToList().FindIndex(f => f.Name == FontCatalog.Find(item.Text.Font).Name));
            textSize = fields.AddNumber("Text size, percent of the frame height", item.Text.SizePercent, 1, 50);
            color = fields.AddChoice("Text colour", colorNames, Math.Max(0, colorNames.FindIndex(c => c == item.Text.ColorName)));
            animIn = fields.AddChoice("Comes in by", TextAnimationCatalog.All.Select(TextAnimationCatalog.InName).ToList(), (int)item.Text.AnimationIn);
            animOut = fields.AddChoice("Goes out by", TextAnimationCatalog.All.Select(TextAnimationCatalog.OutName).ToList(), (int)item.Text.AnimationOut);
        }

        _applyProperties = () =>
        {
            if (fields.Validate() is { } problem)
            {
                _earcons.Play(EarconEvent.Error);
                Say(problem.Message);
                problem.Control.Focus();
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
            if (volume is not null)
            {
                edited.Volume = volume() / 100;
                edited.AudioMuted = mute!();
            }

            if (position is not null)
            {
                edited.Placement.Position = (PositionPreset)position();
                if (size is not null)
                {
                    edited.Placement.SizePercent = size();
                }

                edited.Placement.OpacityPercent = opacity!();
            }

            if (isText)
            {
                edited.Text.Font = FontCatalog.Fonts[font!()].Name;
                edited.Text.SizePercent = textSize!();
                edited.Text.ColorName = colorNames[color!()];
                edited.Text.AnimationIn = (TextAnimation)animIn!();
                edited.Text.AnimationOut = (TextAnimation)animOut!();
            }

            var index = PropertiesPanel.Children.OfType<Control>().ToList().FindIndex(c => c.IsKeyboardFocusWithin);
            var feedback = _editor.UpdateFocusedItem(edited);
            _ = ApplyAsync(feedback);
            RebuildProperties(force: true);
            var controls = PropertiesPanel.Children.OfType<Control>().ToList();
            if (index >= 0 && index < controls.Count)
            {
                controls[index].Focus();
            }
        };

        fields.AddButton("Apply changes", () => _applyProperties?.Invoke(), "Or press Enter in any field (Control Enter in the text box).");
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
