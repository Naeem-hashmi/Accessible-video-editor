using System.Windows;
using System.Windows.Controls;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.App;

/// <summary>Mouse editing on the visual timeline. Every action goes through the same edits as the keys.</summary>
public partial class MainWindow
{
    private void WireTracks()
    {
        Tracks.Clicked += (layerId, itemId, time) =>
        {
            _editor.FocusLayer(layerId);
            if (itemId is { } id)
            {
                _editor.FocusItem(id);
            }
            else
            {
                _editor.SetPlayheadFromPlayback(time);
            }

            _preview.Pause();
            Refresh();
            Say(itemId is null ? $"{_editor.FocusedLayer?.Name}, {TimeFormatClock(time)}" : _editor.FocusedItemText(), interrupt: true);
        };
        Tracks.ItemDoubleClicked += (layerId, itemId) =>
        {
            _editor.FocusLayer(layerId);
            _editor.FocusItem(itemId);
            Refresh();
            _ = RunAsync(CommandId.ItemProperties);
        };
        Tracks.ItemMoved += (layerId, itemId, start) =>
        {
            _editor.FocusLayer(layerId);
            _editor.FocusItem(itemId);
            var feedback = _editor.Edit("move item", project =>
            {
                var layer = project.FindLayer(layerId)!;
                var index = layer.IndexOf(itemId);
                return ProjectEditor.SlideItem(layer, index, start - Timeline.Span(layer, index).Start, _settings.Ripple);
            });
            _ = ApplyAsync(feedback);
        };
        Tracks.ItemResized += (layerId, itemId, end) =>
        {
            _editor.FocusLayer(layerId);
            _editor.FocusItem(itemId);
            if (_editor.FocusedItem is not { } item || _editor.FocusedLayer is not { } layer)
            {
                return;
            }

            var edited = item.Clone(newId: false);
            var span = Timeline.Span(layer, _editor.FocusedItemIndex);
            edited.Duration = Math.Min(item.AvailableDuration, Math.Max(Timeline.MinItemDuration, end - span.Start));
            _ = ApplyAsync(_editor.UpdateFocusedItem(edited));
        };
        Tracks.MenuRequested += (layerId, itemId, time) =>
        {
            _editor.FocusLayer(layerId);
            if (itemId is { } id)
            {
                _editor.FocusItem(id);
            }

            _editor.SetPlayheadFromPlayback(time);
            Refresh();
            var menu = (itemId is null ? null : BuildItemMenu()) ?? new ContextMenu();
            menu.PlacementTarget = Tracks;
            if (menu.Items.Count > 0)
            {
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(Command(CommandId.ImportAtPlayhead, "Add _files here"));
            menu.Items.Add(Command(CommandId.AddText, "Add te_xt here"));
            menu.Items.Add(Command(CommandId.AddMarker, "Add mar_ker here"));
            menu.Items.Add(Command(CommandId.LayerProperties, "La_yer settings..."));
            menu.IsOpen = true;
        };
    }

    private static string TimeFormatClock(double time) => Core.Text.TimeFormat.Clock(time);
}
