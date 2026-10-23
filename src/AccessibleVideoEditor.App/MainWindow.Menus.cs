using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AccessibleVideoEditor.App.Dialogs;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App;

/// <summary>
/// The options menu (Applications key, Shift F10 or right click): everything that can be done with the
/// focused item, the selected items or the focused layer, each with its keys shown. And the settings dialog
/// for several selected items at once.
/// </summary>
public partial class MainWindow
{
    /// <summary>Opens the options menu for whatever has focus: the layer list, or an item on the timeline.</summary>
    internal ContextMenu? ShowOptionsMenu()
    {
        var forLayer = LayersList.IsKeyboardFocusWithin;
        var menu = forLayer ? BuildLayerMenu() : BuildItemMenu();
        if (menu is null)
        {
            _earcons.Play(EarconEvent.Error);
            Say(_editor.FocusedLayer is null ? "No layers. Control Shift V adds a video layer." : "This layer is empty. Press Control I to add files.");
            return null;
        }

        var target = Keyboard.FocusedElement as UIElement ?? (forLayer ? LayersList : ItemsList);
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        return menu;
    }

    /// <summary>The menu for the focused item, or for every selected item when several are selected.</summary>
    internal ContextMenu? BuildItemMenu()
    {
        if (_editor.FocusedLayer is not { } layer || _editor.FocusedItem is not { } item)
        {
            return null;
        }

        var selected = _editor.SelectedIndexes;
        var many = selected.Count > 1;
        var items = many ? selected.Select(i => layer.Items[i]).ToList() : [item];
        var clips = items.Any(i => i.Kind is ItemKind.Video or ItemKind.Audio);
        var sound = items.Any(i => i.HasAudio);
        var menu = new ContextMenu();
        AutomationProperties.SetName(menu, many ? $"Options for {selected.Count} selected items" : $"Options for {item.Name}");

        if (many)
        {
            menu.Items.Add(Command(CommandId.ItemProperties, $"_Settings for the {selected.Count} selected items..."));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command(CommandId.Copy, $"_Copy {selected.Count} items"));
            menu.Items.Add(Command(CommandId.Cut, $"Cu_t {selected.Count} items"));
            menu.Items.Add(Command(CommandId.Duplicate, $"D_uplicate {selected.Count} items"));
            menu.Items.Add(Command(CommandId.Delete, $"_Delete {selected.Count} items"));
            menu.Items.Add(Command(CommandId.DeleteKeepTiming, "Delete, _keep timing"));
        }
        else
        {
            menu.Items.Add(Command(CommandId.ItemProperties, "All _settings..."));
            menu.Items.Add(Command(CommandId.EditTextOrRename, item.Kind == ItemKind.Text ? "_Edit text..." : "_Rename..."));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command(CommandId.Copy, "_Copy"));
            menu.Items.Add(Command(CommandId.Cut, "Cu_t"));
            menu.Items.Add(Command(CommandId.Paste, "_Paste after this"));
            menu.Items.Add(Command(CommandId.Duplicate, "D_uplicate"));
            menu.Items.Add(Command(CommandId.Delete, "_Delete"));
            menu.Items.Add(Command(CommandId.DeleteKeepTiming, "Delete, _keep timing"));
            menu.Items.Add(new Separator());
            menu.Items.Add(Command(CommandId.Split, "Sp_lit at playhead"));
            menu.Items.Add(Command(CommandId.TrimStart, "Trim start to playhead"));
            menu.Items.Add(Command(CommandId.TrimEnd, "Trim end to playhead"));
        }

        var stretch = SubMenu("Length and spee_d");
        stretch.Items.Add(Command(CommandId.Longer, "_Longer"));
        stretch.Items.Add(Command(CommandId.Shorter, "_Shorter"));
        stretch.Items.Add(Command(CommandId.LongerFine, "A little longer"));
        stretch.Items.Add(Command(CommandId.ShorterFine, "A little shorter"));
        if (clips)
        {
            stretch.Items.Add(new Separator());
            stretch.Items.Add(Command(CommandId.Faster, "_Faster"));
            stretch.Items.Add(Command(CommandId.Slower, "Slo_wer (slow motion)"));
            stretch.Items.Add(Command(CommandId.NormalSpeed, "_Normal speed"));
        }

        menu.Items.Add(stretch);

        var move = SubMenu("_Move");
        move.Items.Add(Command(CommandId.SlideEarlier, "Slide _earlier"));
        move.Items.Add(Command(CommandId.SlideLater, "Slide _later"));
        move.Items.Add(Command(CommandId.SlideEarlierFine, "Slide earlier a little"));
        move.Items.Add(Command(CommandId.SlideLaterFine, "Slide later a little"));
        if (!many)
        {
            move.Items.Add(new Separator());
            move.Items.Add(Command(CommandId.MoveItemEarlier, "Swap with the _previous item"));
            move.Items.Add(Command(CommandId.MoveItemLater, "Swap with the _next item"));
            move.Items.Add(Command(CommandId.MoveItemFirst, "Move to the _start"));
            move.Items.Add(Command(CommandId.MoveItemLast, "Move to the en_d"));
        }

        menu.Items.Add(move);

        if (sound)
        {
            var audio = SubMenu("S_ound");
            audio.Items.Add(Command(CommandId.ToggleItemMute, items.All(i => i.AudioMuted) ? "_Unmute" : "_Mute"));
            audio.Items.Add(Command(CommandId.ItemVolumeUp, "_Louder"));
            audio.Items.Add(Command(CommandId.ItemVolumeDown, "_Quieter"));
            if (!many && clips)
            {
                audio.Items.Add(new Separator());
                audio.Items.Add(Command(CommandId.Transcribe, "_Transcribe speech..."));
                audio.Items.Add(Command(CommandId.TranscriptEditor, "_Edit by transcript..."));
                audio.Items.Add(Command(CommandId.FindRepeats, "Find _repeats and fillers..."));
                audio.Items.Add(Command(CommandId.AiCleanup, "Clean up with _AI..."));
                audio.Items.Add(Command(CommandId.ExportTranscript, "E_xport transcript..."));
                audio.Items.Add(Command(CommandId.ImportTranscript, "_Import edited transcript..."));
                audio.Items.Add(Command(CommandId.ShortenPauses, "Shorten _pauses..."));
                audio.Items.Add(Command(CommandId.FindBeats, "Find _beats"));
            }

            menu.Items.Add(audio);
        }

        if (!many && layer.Kind == LayerKind.Video)
        {
            var picture = SubMenu("Pict_ure");
            picture.Items.Add(Command(CommandId.PositionMode, "_Position and size"));
            picture.Items.Add(Command(CommandId.TransitionHere, "_Transition into it..."));
            picture.Items.Add(Command(CommandId.DescribePicture, "_Describe the picture"));
            menu.Items.Add(picture);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Command(CommandId.ToggleSelect, _editor.IsSelected(item.Id) ? "U_nselect this item" : "Sel_ect this item"));
        menu.Items.Add(Command(CommandId.SelectAll, "Select _all on this layer"));
        if (_editor.HasSelection)
        {
            menu.Items.Add(Command(CommandId.Stop, "Clear the selectio_n"));
        }

        if (!many)
        {
            menu.Items.Add(Command(CommandId.DescribeItem, "Describe in deta_il"));
        }

        return menu;
    }

    /// <summary>The menu for the focused layer.</summary>
    internal ContextMenu? BuildLayerMenu()
    {
        if (_editor.FocusedLayer is not { } layer)
        {
            return null;
        }

        var menu = new ContextMenu();
        AutomationProperties.SetName(menu, $"Options for layer {layer.Name}");
        menu.Items.Add(Command(CommandId.LayerProperties, "Layer _settings...", toLayers: true));
        menu.Items.Add(Command(CommandId.ToggleMute, layer.Muted ? "_Unmute layer" : "_Mute layer", toLayers: true));
        menu.Items.Add(Command(CommandId.ToggleSolo, "S_olo", toLayers: true));
        menu.Items.Add(Command(CommandId.UnmuteAll, "Unmute _all layers", toLayers: true));
        menu.Items.Add(Command(CommandId.LayerVolumeUp, "_Louder", toLayers: true));
        menu.Items.Add(Command(CommandId.LayerVolumeDown, "_Quieter", toLayers: true));
        menu.Items.Add(new Separator());
        menu.Items.Add(Command(CommandId.MoveLayerUp, "Move _up the list", toLayers: true));
        menu.Items.Add(Command(CommandId.MoveLayerDown, "Move _down the list", toLayers: true));
        menu.Items.Add(Command(CommandId.SlideLayerEarlier, "Slide everything _earlier", toLayers: true));
        menu.Items.Add(Command(CommandId.SlideLayerLater, "Slide everything la_ter", toLayers: true));
        menu.Items.Add(new Separator());
        menu.Items.Add(Command(CommandId.ImportToEnd, "Add _files...", toLayers: true));
        if (layer.Items.Count > 0)
        {
            menu.Items.Add(Command(CommandId.SelectAll, "Select all its _items"));
            menu.Items.Add(Command(CommandId.Transcribe, "T_ranscribe speech...", toLayers: true));
            menu.Items.Add(Command(CommandId.MakeCaptions, "Make _captions", toLayers: true));
            menu.Items.Add(Command(CommandId.FindRepeats, "Find re_peats and fillers...", toLayers: true));
            menu.Items.Add(Command(CommandId.AiCleanup, "Clean up with A_I...", toLayers: true));
            menu.Items.Add(Command(CommandId.AutoDuck, "Lower this music under speec_h...", toLayers: true));
            menu.Items.Add(Command(CommandId.CutToBeats, "Cut to the _beat", toLayers: true));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Command(CommandId.NewVideoLayer, "New _video layer", toLayers: true));
        menu.Items.Add(Command(CommandId.NewAudioLayer, "New audio layer", toLayers: true));
        menu.Items.Add(Command(CommandId.DeleteLayer, "Delete this layer", toLayers: true));
        return menu;
    }

    private static MenuItem SubMenu(string header) => new() { Header = header };

    /// <summary>A menu entry that runs a command, shows its key, and returns focus where it belongs first.</summary>
    private MenuItem Command(CommandId id, string header, bool toLayers = false)
    {
        var info = CommandCatalog.Get(id);
        var item = MenuItem(header, _keymap.GestureText(id));
        AutomationProperties.SetHelpText(item, info.Help);
        item.Click += async (_, _) =>
        {
            await Dispatcher.InvokeAsync(toLayers ? FocusLayerList : FocusTimeline);
            await RunAsync(id);
        };
        return item;
    }

    // ------------------------------------------------------------------ settings for many items

    private static readonly double[] VolumeChoices = [0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 2];
    private static readonly double[] FadeChoices = [0, 0.25, 0.5, 1, 2, 3];

    /// <summary>One dialog that changes the same settings on every selected item. "Keep as is" leaves each item's own value.</summary>
    private async Task ShowSelectionOptionsAsync()
    {
        var layer = _editor.FocusedLayer!;
        var indexes = _editor.SelectedIndexes;
        var items = indexes.Select(i => layer.Items[i]).ToList();
        const string keep = "Keep as is";
        var form = new FormDialog(this, $"Settings for {indexes.Count} selected items");
        form.AddIntro($"{_editor.SelectionSummary()} Each setting left at \"{keep}\" stays as it is on every item.");

        var hasSound = items.Any(i => i.HasAudio);
        Func<int>? volume = null, mute = null;
        if (hasSound)
        {
            volume = form.AddChoice("Volume", [keep, .. VolumeChoices.Select(v => Describer.Percent(v))], 0);
            mute = form.AddChoice("Sound", [keep, "On", "Muted"], 0);
        }

        var clips = items.Count(i => i.Kind is ItemKind.Video or ItemKind.Audio);
        Func<int>? speed = null;
        if (clips > 0)
        {
            speed = form.AddChoice("Speed", [keep, .. StretchEditing.SpeedSteps.Select(SpeedName)], 0,
                clips < items.Count ? "Only video and sound clips have a speed; pictures and text keep theirs." : null);
        }

        var lengthChoice = form.AddChoice("Length", [keep, "Set every item to the same length"], 0,
            "Clips can't be longer than their file allows at their speed.");
        var length = form.AddTime("Same length for all", items.Min(i => i.Duration), minimum: Timeline.MinItemDuration);
        var fadeIn = form.AddChoice("Fade in", [keep, .. FadeChoices.Select(f => f == 0 ? "None" : TimeFormat.Words(f))], 0);
        var fadeOut = form.AddChoice("Fade out", [keep, .. FadeChoices.Select(f => f == 0 ? "None" : TimeFormat.Words(f))], 0);
        var transitions = layer.Kind == LayerKind.Video ? TransitionCatalog.Video : TransitionCatalog.Audio;
        var transition = form.AddChoice(layer.Kind == LayerKind.Video ? "Transition into each" : "Crossfade into each",
            [keep, .. transitions.Select(t => t.Name)], 0, "Plays only where an item directly follows the one before it.");
        if (form.ShowDialog() != true)
        {
            return;
        }

        var ripple = _settings.Ripple;
        var layerId = layer.Id;
        var feedback = _editor.Edit("settings for items", project =>
        {
            var target = project.FindLayer(layerId)!;
            var changes = new List<string>();
            foreach (var index in indexes.OrderByDescending(i => i))
            {
                var item = target.Items[index];
                if (volume is not null && volume() > 0)
                {
                    item.Volume = VolumeChoices[volume() - 1];
                }

                if (mute is not null && mute() > 0 && item.HasAudio)
                {
                    item.AudioMuted = mute() == 2;
                }

                if (speed is not null && speed() > 0 && item.Kind is ItemKind.Video or ItemKind.Audio)
                {
                    var result = StretchEditing.SetSpeed(target, index, StretchEditing.SpeedSteps[speed() - 1], ripple);
                    if (!result.Ok)
                    {
                        return result;
                    }
                }

                if (lengthChoice() == 1 && Math.Abs(length() - item.Duration) > 0.001)
                {
                    // Clips stop where their file ends; the others get exactly this length.
                    var result = StretchEditing.ChangeLength(target, index, length() - item.Duration, ripple);
                    if (!result.Ok && !result.Message.Contains("can't be longer", StringComparison.Ordinal))
                    {
                        return result;
                    }
                }

                if (fadeIn() > 0)
                {
                    item.FadeIn = Math.Min(FadeChoices[fadeIn() - 1], item.Duration / 2);
                }

                if (fadeOut() > 0)
                {
                    item.FadeOut = Math.Min(FadeChoices[fadeOut() - 1], item.Duration / 2);
                }

                if (transition() > 0)
                {
                    var value = transitions[transition() - 1].Value;
                    item.TransitionIn = layer.Kind == LayerKind.Video
                        ? new TransitionSpec { Video = value, Audio = item.TransitionIn.Audio, Duration = item.TransitionIn.Duration }
                        : new TransitionSpec { Video = item.TransitionIn.Video, Audio = value, Duration = item.TransitionIn.Duration };
                }
            }

            return EditResult.Success($"Updated {indexes.Count} items.");
        });
        await ApplyAsync(feedback);
    }
}
