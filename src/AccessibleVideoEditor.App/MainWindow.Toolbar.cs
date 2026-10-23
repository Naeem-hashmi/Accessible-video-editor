using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AccessibleVideoEditor.Core.Input;

namespace AccessibleVideoEditor.App;

/// <summary>The toolbar: the most used commands as icon buttons with words. Arrow keys move between them.</summary>
public partial class MainWindow
{
    private void BuildToolbar()
    {
        (string Icon, string Label, Action Run)[] buttons =
        [
            (Icons.Import, "Import", () => _ = RunAsync(CommandId.ImportToEnd)),
            (Icons.Text, "Add text", () => _ = RunAsync(CommandId.AddText)),
            (Icons.Music, "Sounds", () => FocusLibrary(LibraryTab.Sounds)),
            (Icons.Play, "Play or pause", () => _ = RunAsync(CommandId.PlayPause)),
            (Icons.Stop, "Stop", () => _ = RunAsync(CommandId.Stop)),
            (Icons.Split, "Split", () => _ = RunAsync(CommandId.Split)),
            (Icons.Delete, "Delete item", () => _ = RunAsync(CommandId.Delete)),
            (Icons.Marker, "Add marker", () => _ = RunAsync(CommandId.AddMarker)),
            (Icons.Move, "Position", () => _ = RunAsync(CommandId.PositionMode)),
            (Icons.Describe, "Describe picture", () => _ = RunAsync(CommandId.DescribePicture)),
            (Icons.Undo, "Undo", () => _ = RunAsync(CommandId.Undo)),
            (Icons.Redo, "Redo", () => _ = RunAsync(CommandId.Redo)),
            (Icons.Templates, "Templates", () => _ = RunAsync(CommandId.TemplateManager)),
            (Icons.Save, "Save", () => _ = RunAsync(CommandId.SaveProject)),
            (Icons.Export, "Export", () => _ = RunAsync(CommandId.Export)),
            (Icons.Help, "Keys", () => _ = RunAsync(CommandId.KeyboardHelp)),
        ];

        var commandFor = new Dictionary<string, CommandId>
        {
            ["Import"] = CommandId.ImportToEnd, ["Add text"] = CommandId.AddText, ["Play or pause"] = CommandId.PlayPause,
            ["Stop"] = CommandId.Stop, ["Split"] = CommandId.Split, ["Delete item"] = CommandId.Delete, ["Add marker"] = CommandId.AddMarker,
            ["Position"] = CommandId.PositionMode, ["Describe picture"] = CommandId.DescribePicture, ["Undo"] = CommandId.Undo,
            ["Redo"] = CommandId.Redo, ["Templates"] = CommandId.TemplateManager, ["Save"] = CommandId.SaveProject,
            ["Export"] = CommandId.Export, ["Keys"] = CommandId.KeyboardHelp,
        };

        foreach (var (icon, label, run) in buttons)
        {
            var gesture = commandFor.TryGetValue(label, out var id) ? _keymap.GestureText(id) : "Ctrl+1";
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = icon,
                FontFamily = (FontFamily)FindResource("IconFont"),
                FontSize = 18,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = content, ToolTip = $"{label} ({gesture})" };
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAcceleratorKey(button, gesture);
            button.Click += (_, _) => run();
            Toolbar.Children.Add(button);
        }
    }
}
