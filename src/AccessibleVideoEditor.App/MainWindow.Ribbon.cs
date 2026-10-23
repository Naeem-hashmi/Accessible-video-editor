using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.App;

/// <summary>
/// The ribbon (tabs of icon buttons, like Word) and the play controls under the preview. Every button says
/// its name and its key, and runs the same command as the key, so mouse, keyboard and screen reader users
/// get the same result.
/// </summary>
public partial class MainWindow
{
    private sealed record RibbonButton(string Icon, string Label, Action Run, CommandId? Command = null, string? Help = null);

    private RibbonButton Cmd(string icon, CommandId command, string? label = null) =>
        new(icon, label ?? CommandCatalog.Get(command).Title, () => _ = RunAsync(command), command, CommandCatalog.Get(command).Help);

    private void BuildRibbon()
    {
        Ribbon.Items.Clear();
        AddRibbonTab("Home", Icons.Play,
        [
            ("Clipboard", [Cmd(Icons.Undo, CommandId.Undo), Cmd(Icons.Redo, CommandId.Redo), Cmd(Icons.Cut, CommandId.Cut, "Cut"),
                Cmd(Icons.Copy, CommandId.Copy, "Copy"), Cmd(Icons.Paste, CommandId.Paste, "Paste")]),
            ("Edit", [Cmd(Icons.Split, CommandId.Split), Cmd(Icons.Delete, CommandId.Delete, "Delete"), Cmd(Icons.Edit, CommandId.EditTextOrRename, "Rename or edit text"),
                Cmd(Icons.Settings, CommandId.ItemProperties, "All options")]),
            ("Make", [Cmd(Icons.Lightning, CommandId.QuickVideo), Cmd(Icons.Import, CommandId.ImportToEnd, "Import"), Cmd(Icons.Save, CommandId.SaveProject, "Save"),
                Cmd(Icons.Export, CommandId.Export, "Export")]),
        ]);
        AddRibbonTab("Insert", Icons.Add,
        [
            ("Media", [Cmd(Icons.Import, CommandId.ImportToEnd, "Files at the end"), Cmd(Icons.Import, CommandId.ImportAtPlayhead, "Files at the playhead"),
                Cmd(Icons.ColorCard, CommandId.AddColorCard, "Colour card")]),
            ("Text and sound", [Cmd(Icons.Text, CommandId.AddText, "Text"), Cmd(Icons.Music, CommandId.AddSoundEffect, "Sound effect"),
                Cmd(Icons.Search, CommandId.OnlineMusic, "Free music online")]),
            ("Layers and marks", [Cmd(Icons.Video, CommandId.NewVideoLayer, "Video layer"), Cmd(Icons.Music, CommandId.NewAudioLayer, "Audio layer"),
                Cmd(Icons.Marker, CommandId.AddMarker, "Marker")]),
        ]);
        AddRibbonTab("Transitions", Icons.Transition,
        [
            ("Into the selected item", TransitionCatalog.Video.Take(12)
                .Select(t => new RibbonButton(Icons.Transition, t.Name, () => SetTransition(t.Value), Help: t.Description)).ToList()),
            ("More", [Cmd(Icons.Transition, CommandId.TransitionHere, "Transition at the playhead"), new RibbonButton(Icons.Settings, "Transition length", () => _ = RunAsync(CommandId.ItemProperties), Help: "Open item options to set the transition and its length.")]),
        ]);
        AddRibbonTab("Animations", Icons.Animation,
        [
            ("Text comes in", TextAnimationCatalog.All.Select(a => new RibbonButton(Icons.Animation, TextAnimationCatalog.InName(a), () => SetTextAnimation(a, comingIn: true))).ToList()),
            ("Text goes out", TextAnimationCatalog.All.Take(3).Select(a => new RibbonButton(Icons.Animation, TextAnimationCatalog.OutName(a), () => SetTextAnimation(a, comingIn: false))).ToList()),
            ("Any item", [new RibbonButton(Icons.Animation, "Fade in and out", () => SetFades(0.5), Help: "Half a second of fade at the start and end of the selected item."),
                new RibbonButton(Icons.Animation, "No fades", () => SetFades(0))]),
        ]);
        AddRibbonTab("Audio", Icons.Volume,
        [
            ("Volume", [Cmd(Icons.Volume, CommandId.ItemVolumeUp, "Louder"), Cmd(Icons.Volume, CommandId.ItemVolumeDown, "Quieter"), Cmd(Icons.Mute, CommandId.ToggleItemMute, "Mute item"),
                Cmd(Icons.Mute, CommandId.ToggleMute, "Mute layer"), Cmd(Icons.Volume, CommandId.ToggleSolo, "Solo layer")]),
            ("Smart", [Cmd(Icons.Volume, CommandId.AutoDuck), Cmd(Icons.Stopwatch, CommandId.ShortenPauses), Cmd(Icons.Music, CommandId.FindBeats), Cmd(Icons.Split, CommandId.CutToBeats)]),
        ]);
        AddRibbonTab("Captions", Icons.Captions,
        [
            ("Speech to text", [Cmd(Icons.Microphone, CommandId.Transcribe), Cmd(Icons.Edit, CommandId.TranscriptEditor), Cmd(Icons.Captions, CommandId.MakeCaptions)]),
        ]);
        AddRibbonTab("Picture", Icons.Picture,
        [
            ("Place", [Cmd(Icons.Move, CommandId.PositionMode, "Position mode"), Cmd(Icons.Describe, CommandId.DescribePicture, "Describe picture")]),
        ]);
        AddRibbonTab("Export", Icons.Export,
        [
            ("Finish", [Cmd(Icons.Export, CommandId.Export, "Export video"), Cmd(Icons.Check, CommandId.ProjectCheck, "Check project"), Cmd(Icons.Settings, CommandId.ProjectSettings, "Project settings")]),
            ("Templates", [Cmd(Icons.Templates, CommandId.TemplateManager, "Templates"), Cmd(Icons.Save, CommandId.SaveAsTemplate, "Save as template")]),
            ("Help", [Cmd(Icons.Help, CommandId.KeyboardHelp, "Keys")]),
        ]);
        Ribbon.SelectedIndex = 0;
    }

    private void AddRibbonTab(string name, string icon, IReadOnlyList<(string Group, List<RibbonButton> Buttons)> groups)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(IconBlock(icon, 15));
        header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var tab = new TabItem { Header = header };
        AutomationProperties.SetName(tab, name);

        // One row of groups. When it is wider than the window it scrolls left and right; moving focus with the
        // arrow keys scrolls the focused button into view.
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2) };
        KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
        foreach (var (group, buttons) in groups)
        {
            var box = new GroupBox { Header = group, Margin = new Thickness(2, 0, 2, 0), Padding = new Thickness(2) };
            AutomationProperties.SetName(box, group);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var b in buttons)
            {
                row.Children.Add(MakeIconButton(b.Icon, b.Label, b.Run, b.Command, b.Help, vertical: true));
            }

            box.Content = row;
            panel.Children.Add(box);
        }

        var scroller = new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            PanningMode = PanningMode.HorizontalOnly,
        };
        scroller.PreviewMouseWheel += (_, e) =>
        {
            // The mouse wheel scrolls the ribbon sideways.
            scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        };
        tab.Content = scroller;
        Ribbon.Items.Add(tab);
    }

    private TextBlock IconBlock(string icon, double size) => new()
    {
        Text = icon,
        FontFamily = (FontFamily)FindResource("IconFont"),
        FontSize = size,
        Margin = new Thickness(0, 0, 6, 0),
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private Button MakeIconButton(string icon, string label, Action run, CommandId? command, string? help, bool vertical)
    {
        var gesture = command is { } id ? _keymap.GestureText(id) : "";
        var content = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal };
        var glyph = IconBlock(icon, vertical ? 20 : 16);
        if (vertical)
        {
            glyph.Margin = new Thickness(0, 0, 0, 2);
        }

        content.Children.Add(glyph);
        content.Children.Add(new TextBlock
        {
            Text = label,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = vertical ? 84 : double.PositiveInfinity,
            MaxHeight = vertical ? 40 : double.PositiveInfinity,
            FontSize = vertical ? 12 : 14,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var button = new Button
        {
            Content = content,
            ToolTip = gesture.Length > 0 ? $"{label} ({gesture})" + (help is null ? "" : "\n" + help) : label + (help is null ? "" : "\n" + help),
            MinWidth = vertical ? 64 : 0,
            Padding = new Thickness(6, 3, 6, 3),
        };
        AutomationProperties.SetName(button, label);
        if (gesture.Length > 0)
        {
            AutomationProperties.SetAcceleratorKey(button, gesture);
        }

        if (help is not null)
        {
            AutomationProperties.SetHelpText(button, help);
        }

        button.Click += (_, _) => run();
        return button;
    }

    private void BuildTransport()
    {
        Transport.Children.Clear();
        foreach (var (icon, label, command) in new (string, string, CommandId)[]
                 {
                     (Icons.Previous, "Go to start", CommandId.GoToStart),
                     (Icons.Previous, "Rewind", CommandId.Rewind),
                     (Icons.Play, "Play or pause", CommandId.PlayPause),
                     (Icons.Next, "Fast forward", CommandId.Forward),
                     (Icons.Stop, "Stop", CommandId.Stop),
                     (Icons.Marker, "Previous marker", CommandId.PreviousMarker),
                     (Icons.Marker, "Next marker", CommandId.NextMarker),
                     (Icons.Volume, "Listen to all layers", CommandId.ListenAllLayers),
                 })
        {
            Transport.Children.Add(MakeIconButton(icon, label, () => _ = RunAsync(command), command, null, vertical: false));
        }
    }

    // ------------------------------------------------------------------ ribbon actions on the selected item

    private void SetTransition(string video)
    {
        if (_editor.FocusedItem is not { } item)
        {
            Say("Select an item first: the transition goes into it from the item before.");
            return;
        }

        if (_editor.FocusedItemIndex == 0)
        {
            Say("The first item has nothing before it to transition from. Select a later item.");
            return;
        }

        var edited = item.Clone(newId: false);
        edited.TransitionIn = new TransitionSpec
        {
            Video = video,
            Audio = video == TransitionSpec.None ? TransitionSpec.None : "tri",
            Duration = item.TransitionIn.Duration > 0.05 ? item.TransitionIn.Duration : 1,
        };
        _ = ApplyAsync(_editor.UpdateFocusedItem(edited));
    }

    private void SetTextAnimation(TextAnimation animation, bool comingIn)
    {
        if (_editor.FocusedItem is not { Kind: ItemKind.Text } item)
        {
            Say("Select a text item first.");
            return;
        }

        var edited = item.Clone(newId: false);
        if (comingIn)
        {
            edited.Text.AnimationIn = animation;
        }
        else
        {
            edited.Text.AnimationOut = animation;
        }

        _ = ApplyAsync(_editor.UpdateFocusedItem(edited));
    }

    private void SetFades(double seconds)
    {
        if (_editor.FocusedItem is not { } item)
        {
            Say("Select an item first.");
            return;
        }

        var edited = item.Clone(newId: false);
        edited.FadeIn = Math.Min(seconds, item.Duration / 2);
        edited.FadeOut = Math.Min(seconds, item.Duration / 2);
        _ = ApplyAsync(_editor.UpdateFocusedItem(edited));
    }
}
