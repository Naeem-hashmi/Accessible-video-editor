using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Keyboard workflows, exactly as a screen reader user would press the keys.</summary>
public sealed class EditorControllerTests
{
    private static EditorController Editor(out Project project, int videoClips = 3, bool withAudio = true)
    {
        project = new Project { Title = "Test" };
        var video = ProjectEditor.AddLayer(project, LayerKind.Video);
        for (var i = 0; i < videoClips; i++)
        {
            video.Items.Add(Make.Clip($"clip{i + 1}.mp4", 4, sourceDuration: 10));
        }

        if (withAudio)
        {
            var audio = ProjectEditor.AddLayer(project, LayerKind.Audio, "Music");
            audio.Items.Add(Make.Clip("song.mp3", 10));
        }

        return new EditorController(new ProjectSession(project), new AppSettings());
    }

    private static Feedback Press(EditorController editor, CommandId command) =>
        editor.Execute(command) ?? throw new InvalidOperationException($"{command} needs the window.");

    [Fact]
    public void Starts_on_layer_1_first_item_listening_to_everything()
    {
        var editor = Editor(out var project);
        Assert.Equal(project.Layers[0].Id, editor.FocusedLayerId);
        Assert.Equal(project.Layers[0].Items[0].Id, editor.FocusedItemId);
        Assert.True(editor.ListenAll);
        Assert.Equal(0, editor.Playhead);
    }

    [Fact]
    public void Left_and_right_move_between_items_and_carry_the_playhead()
    {
        var editor = Editor(out var project);
        var feedback = Press(editor, CommandId.NextItem);
        Assert.Equal(project.Layers[0].Items[1].Id, editor.FocusedItemId);
        Assert.Equal(4, editor.Playhead);
        Assert.Equal(EarconEvent.Navigate, feedback.Earcon);

        Press(editor, CommandId.LastItem);
        Assert.Equal(8, editor.Playhead);
        var edge = Press(editor, CommandId.NextItem);
        Assert.Equal("Last item.", edge.Speech);
        Assert.Equal(EarconEvent.Edge, edge.Earcon);
        Press(editor, CommandId.FirstItem);
        Assert.Equal("First item.", Press(editor, CommandId.PreviousItem).Speech);
    }

    [Fact]
    public void Alt_number_focuses_a_layer_and_listens_to_it_alone_and_alt_0_toggles_all()
    {
        var editor = Editor(out var project);
        var feedback = Press(editor, CommandId.Layer2);
        Assert.Equal(project.Layers[1].Id, editor.FocusedLayerId);
        Assert.False(editor.ListenAll);
        Assert.Equal(project.Layers[1].Id, editor.CurrentScope.LayerId);
        Assert.StartsWith("Layer 2, Audio, Music, 1 item", feedback.Speech);
        Assert.EndsWith("listening to this layer only.", feedback.Speech);
        Assert.Equal(PlaybackRequest.Refresh, feedback.Playback);

        Assert.Equal("Listening to all layers.", Press(editor, CommandId.ListenAllLayers).Speech);
        Assert.Null(editor.CurrentScope.LayerId);
        Assert.Contains("only", Press(editor, CommandId.ListenAllLayers).Speech);

        var missing = Press(editor, CommandId.Layer5);
        Assert.Equal("There is no layer 5. The project has 2 layers.", missing.Speech);
        Assert.Equal(EarconEvent.Error, missing.Earcon);
    }

    [Fact]
    public void Up_and_down_reach_any_number_of_layers_keeping_the_time()
    {
        var project = new Project();
        for (var i = 0; i < 12; i++)
        {
            var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
            layer.Items.Add(Make.Clip($"a{i}", 3));
            layer.Items.Add(Make.Clip($"b{i}", 3));
        }

        var editor = new EditorController(new ProjectSession(project), new AppSettings());
        Press(editor, CommandId.NextItem);
        for (var i = 0; i < 11; i++)
        {
            Press(editor, CommandId.NextLayer);
        }

        Assert.Equal(11, editor.FocusedLayerIndex);
        Assert.Equal("b11", editor.FocusedItem!.Name);
        Assert.Equal(EarconEvent.Edge, Press(editor, CommandId.NextLayer).Earcon);
        Assert.True(editor.ListenAll, "Arrow keys keep the listening mode.");
        Assert.Equal(EarconEvent.LayerChange, editor.GoToLayer(12, listenOnly: false).Earcon);
    }

    [Fact]
    public void J_K_L_and_escape_ask_the_player_to_act()
    {
        var editor = Editor(out _);
        Assert.Equal(PlaybackRequest.Toggle, Press(editor, CommandId.PlayPause).Playback);
        Assert.Equal(PlaybackRequest.Faster, Press(editor, CommandId.Forward).Playback);
        Assert.Equal(PlaybackRequest.Stop, Press(editor, CommandId.Stop).Playback);
        editor.SetPlayheadFromPlayback(9);
        var back = Press(editor, CommandId.Rewind);
        Assert.Equal(4, editor.Playhead, 6);
        Assert.Equal(PlaybackRequest.Seek, back.Playback);
        Press(editor, CommandId.Rewind);
        Assert.Equal(0, editor.Playhead);
        Assert.Equal("At the start.", Press(editor, CommandId.Rewind).Speech);
    }

    [Fact]
    public void Brackets_set_a_range_and_alt_arrows_jump_between_markers()
    {
        var editor = Editor(out var project);
        editor.GoToTime(2);
        Assert.StartsWith("Range start 2 seconds", Press(editor, CommandId.SetRangeStart).Speech);
        editor.GoToTime(5);
        Press(editor, CommandId.SetRangeEnd);
        editor.GoToTime(9);
        Press(editor, CommandId.AddMarker);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.AddMarker).Earcon);

        editor.GoToTime(0);
        Assert.Equal("Range start, 2 seconds.", Press(editor, CommandId.NextMarker).Speech);
        Assert.Equal("Range end, 5 seconds.", Press(editor, CommandId.NextMarker).Speech);
        Assert.Equal("Marker 1, 9 seconds.", Press(editor, CommandId.NextMarker).Speech);
        Assert.Equal(EarconEvent.Edge, Press(editor, CommandId.NextMarker).Earcon);
        Assert.Equal("Range end, 5 seconds.", Press(editor, CommandId.PreviousMarker).Speech);
        Assert.Single(project.Markers);
    }

    [Fact]
    public void Shift_delete_removes_the_range_from_the_focused_layer_only()
    {
        var editor = Editor(out var project);
        editor.GoToTime(2);
        Press(editor, CommandId.SetRangeStart);
        editor.GoToTime(6);
        Press(editor, CommandId.SetRangeEnd);
        var result = Press(editor, CommandId.DeleteRange);
        Assert.Equal(EarconEvent.Deleted, result.Earcon);
        Assert.Equal(8, Timeline.LayerEnd(editor.Project.Layers[0]), 6);
        Assert.Equal(10, Timeline.LayerEnd(editor.Project.Layers[1]), 6);
        Assert.Equal(2, editor.Playhead);
        Press(editor, CommandId.Undo);
        Assert.Equal(12, Timeline.LayerEnd(editor.Project.Layers[0]), 6);
        _ = project;
    }

    [Fact]
    public void Alt_shift_arrows_reorder_and_alt_j_l_slide()
    {
        var editor = Editor(out _);
        Press(editor, CommandId.LastItem);
        var moved = Press(editor, CommandId.MoveItemEarlier);
        Assert.StartsWith("Moved to position 2 of 3", moved.Speech);
        Assert.Equal("clip3.mp4", editor.FocusedItem!.Name);
        Assert.Equal(1, editor.FocusedItemIndex);
        Press(editor, CommandId.MoveItemFirst);
        Assert.Equal(0, editor.FocusedItemIndex);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.MoveItemEarlier).Earcon);

        var slid = Press(editor, CommandId.SlideLater);
        Assert.StartsWith("Slid 1 second later", slid.Speech);
        Assert.Equal(1, editor.Playhead, 6);
        Press(editor, CommandId.SlideEarlierFine);
        Assert.Equal(0.9, editor.FocusedItem!.GapBefore, 6);
        Assert.Equal(13 - 0.1, Timeline.LayerEnd(editor.FocusedLayer!), 6);
    }

    [Fact]
    public void S_splits_the_item_under_the_playhead_and_trims_work()
    {
        var editor = Editor(out _);
        editor.GoToTime(6);
        var split = Press(editor, CommandId.Split);
        Assert.StartsWith("Split clip2.mp4 at 6 seconds", split.Speech);
        Assert.Equal(4, editor.FocusedLayer!.Items.Count);
        Assert.Equal(2, editor.FocusedItemIndex);

        editor.GoToTime(7);
        Press(editor, CommandId.TrimStart);
        Assert.Equal(1, editor.FocusedItem!.Duration, 6);

        editor.GoToTime(100);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.Split).Earcon);
    }

    [Fact]
    public void Delete_moves_focus_to_the_neighbour_and_undo_brings_it_back()
    {
        var editor = Editor(out _);
        Press(editor, CommandId.NextItem);
        var deleted = Press(editor, CommandId.Delete);
        Assert.Equal("Deleted clip2.mp4.", deleted.Speech);
        Assert.Equal("clip3.mp4", editor.FocusedItem!.Name);
        var undo = Press(editor, CommandId.Undo);
        Assert.Equal("Undo delete item.", undo.Speech);
        Assert.Equal(3, editor.FocusedLayer!.Items.Count);
        Assert.Equal("Redo delete item.", Press(editor, CommandId.Redo).Speech);
        Assert.Equal(EarconEvent.Edge, Press(editor, CommandId.Redo).Earcon);
    }

    [Fact]
    public void Ripple_toggle_changes_how_delete_behaves()
    {
        var editor = Editor(out _);
        Assert.StartsWith("Ripple off", Press(editor, CommandId.ToggleRipple).Speech);
        Press(editor, CommandId.Delete);
        Assert.Equal(4, Timeline.Span(editor.FocusedLayer!, 0).Start, 6);
        Press(editor, CommandId.ToggleRipple);
        Press(editor, CommandId.NextItem);
        Press(editor, CommandId.Delete);
        Assert.Equal(new[] { 4.0 }, Make.Starts(editor.FocusedLayer!));
        Assert.Equal(8, Timeline.LayerEnd(editor.FocusedLayer!), 6);
    }

    [Fact]
    public void Copy_paste_and_duplicate_respect_layer_kinds()
    {
        var editor = Editor(out var project);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.Paste).Earcon);
        Press(editor, CommandId.Copy);
        Press(editor, CommandId.Paste);
        Assert.Equal(4, project.Layers[0].Items.Count);
        Assert.Equal(1, editor.FocusedItemIndex);

        Press(editor, CommandId.Duplicate);
        Assert.Equal(5, editor.Project.Layers[0].Items.Count);

        Press(editor, CommandId.Layer2);
        var pasted = Press(editor, CommandId.Paste);
        Assert.False(pasted.IsError);
        Assert.False(editor.FocusedItem!.HasVideo, "A video pasted onto an audio layer is sound only.");

        Press(editor, CommandId.Copy);
        Press(editor, CommandId.Layer1);
        Assert.False(Press(editor, CommandId.Paste).IsError);
        Assert.False(editor.FocusedItem!.HasVideo, "Without picture size information it stays sound only.");
    }

    [Fact]
    public void Mute_solo_and_unmute_all_speak_and_refresh_playback()
    {
        var editor = Editor(out var project);
        var muted = Press(editor, CommandId.ToggleMute);
        Assert.Equal("Video 1 muted.", muted.Speech);
        Assert.Equal(EarconEvent.Muted, muted.Earcon);
        Assert.Equal(PlaybackRequest.Refresh, muted.Playback);
        Press(editor, CommandId.ToggleSolo);
        Assert.False(project.Layers[0].Muted);
        Assert.True(editor.Project.Layers[1].Muted);
        Assert.Equal("All layers unmuted.", Press(editor, CommandId.UnmuteAll).Speech);
    }

    [Fact]
    public void Information_keys_speak_useful_sentences()
    {
        var editor = Editor(out _);
        editor.GoToTime(5);
        Assert.Equal("Playhead 5 seconds of 12 seconds, 1 second into clip2.mp4.", Press(editor, CommandId.SpeakTime).Speech);
        Assert.StartsWith("Test, 12 seconds long, 2 layers, on layer 1, Video 1, listening to all layers, playhead 5 seconds, ripple on",
            Press(editor, CommandId.SpeakStatus).Speech);
        Assert.StartsWith("At 5 seconds. layer 1: clip2.mp4", Press(editor, CommandId.WhatsHere).Speech);
        Assert.Contains("ends 4 seconds", Press(editor, CommandId.DescribeItem).Speech);
        Assert.Equal("Verbosity detailed.", Press(editor, CommandId.CycleVerbosity).Speech);
    }

    [Fact]
    public void New_and_deleted_layers_move_focus_and_can_be_undone()
    {
        var editor = Editor(out _);
        var created = Press(editor, CommandId.NewVideoLayer);
        Assert.Equal("New layer 3, Video, Video 2.", created.Speech);
        Assert.Equal(2, editor.FocusedLayerIndex);
        Assert.Null(editor.FocusedItemId);
        Assert.Contains("empty", editor.FocusedItemText());

        Press(editor, CommandId.Layer1);
        var deleted = Press(editor, CommandId.DeleteLayer);
        Assert.Equal("Deleted layer Video 1 with 3 items. Control Z to undo.", deleted.Speech);
        Assert.Equal("Music", editor.FocusedLayer!.Name);
        Press(editor, CommandId.Undo);
        Assert.Equal(3, editor.Project.Layers.Count);
    }

    [Fact]
    public void Moving_layers_reorders_the_stack()
    {
        var editor = Editor(out _);
        Press(editor, CommandId.Layer2);
        Assert.Equal("Music is now layer 1.", Press(editor, CommandId.MoveLayerUp).Speech);
        Assert.Equal(0, editor.FocusedLayerIndex);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.MoveLayerUp).Earcon);
    }

    [Fact]
    public void Adding_media_goes_to_the_right_layer_or_free_space()
    {
        var editor = Editor(out _);
        var picture = new MediaInfo { Path = "logo.png", Kind = ItemKind.Image, HasVideo = true };
        var sound = new MediaInfo { Path = "pop.wav", Kind = ItemKind.Audio, HasAudio = true, Duration = 1 };

        var added = editor.AddMedia([picture], atPlayhead: false);
        Assert.StartsWith("Added logo.png on layer 1", added.Speech);
        Assert.Equal(12, editor.Playhead, 6);

        Press(editor, CommandId.Layer1);
        editor.AddMedia([sound], atPlayhead: false);
        Assert.Equal("Music", editor.FocusedLayer!.Name);

        editor.GoToTime(3);
        editor.AddMedia([sound], atPlayhead: true);
        Assert.Equal(3, editor.Project.Layers.Count);
        Assert.Equal(3, Timeline.Span(editor.FocusedLayer!, 0).Start, 6);
        Assert.Equal(EarconEvent.Undo, Press(editor, CommandId.Undo).Earcon);
        Assert.Equal(2, editor.Project.Layers.Count);
    }

    [Fact]
    public void Transition_at_playhead_and_at_marker()
    {
        var editor = Editor(out _);
        editor.GoToTime(4);
        var set = editor.SetTransitionAtPlayhead(new TransitionSpec { Video = "dissolve", Duration = 1 });
        Assert.Equal("Dissolve into clip2.mp4.", set.Speech);
        Assert.Equal("clip2.mp4", editor.FocusedItem!.Name);
        Assert.Equal(EarconEvent.Error, editor.SetTransitionAt(5.5, new TransitionSpec { Video = "fade" }).Earcon);
    }

    [Fact]
    public void Item_layer_and_project_options_apply()
    {
        var editor = Editor(out _);
        var edited = editor.FocusedItem!.Clone(newId: false);
        edited.Volume = 0.5;
        edited.Name = "Opening";
        Assert.Equal("Updated Opening.", editor.UpdateFocusedItem(edited).Speech);
        Assert.Equal(0.5, editor.FocusedItem!.Volume);

        var defaults = new LayerDefaults { ImageDuration = 3 };
        editor.UpdateFocusedLayer("Main", 0.8, defaults);
        Assert.Equal("Main", editor.FocusedLayer!.Name);
        Assert.Equal(3, editor.FocusedLayer.Defaults.ImageDuration);

        var settings = editor.UpdateProjectSettings("Holiday", 1281, 721, 25);
        Assert.Equal("Project is 1280 by 720 at 25 frames per second.", settings.Speech);
    }

    [Fact]
    public void Empty_project_gives_guidance_instead_of_errors()
    {
        var editor = new EditorController(new ProjectSession(new Project()), new AppSettings());
        Assert.Equal("No layers.", Press(editor, CommandId.NextItem).Speech);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.Split).Earcon);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.ToggleMute).Earcon);
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.Layer1).Earcon);
        Assert.Equal("No layers.", editor.FocusedItemText());
        foreach (var command in Enum.GetValues<CommandId>())
        {
            // No command may throw on an empty project.
            editor.Execute(command);
        }
    }

    [Fact]
    public void Every_keyboard_command_is_safe_on_a_full_project()
    {
        var editor = Editor(out _);
        foreach (var command in Enum.GetValues<CommandId>())
        {
            var feedback = editor.Execute(command);
            if (feedback is not null)
            {
                Assert.NotNull(feedback.Speech);
            }
        }
    }

    [Fact]
    public void Loading_a_project_resets_focus_and_announces_it()
    {
        var editor = Editor(out _);
        Press(editor, CommandId.Layer2);
        var other = new Project { Title = "Other" };
        ProjectEditor.AddLayer(other, LayerKind.Video);
        var feedback = editor.Load(new ProjectSession(other), "Opened");
        Assert.Equal("Opened. Other, 1 layer, 0 seconds long.", feedback.Speech);
        Assert.True(editor.ListenAll);
        Assert.Equal(other.Layers[0].Id, editor.FocusedLayerId);
    }
}
