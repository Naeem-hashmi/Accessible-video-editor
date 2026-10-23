using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class TimelineTests
{
    [Fact]
    public void Items_follow_each_other_with_gaps()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 3, gap: 1), Make.Clip("c", 1));
        Assert.Equal(new[] { 0.0, 3, 6 }, Make.Starts(layer));
        Assert.Equal(7, Timeline.LayerEnd(layer), 6);
    }

    [Fact]
    public void A_transition_overlaps_the_previous_item()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 4).WithTransition("fade", 1));
        Assert.Equal(new[] { 0.0, 3 }, Make.Starts(layer));
        Assert.Equal(7, Timeline.LayerEnd(layer), 6);
    }

    [Fact]
    public void Transitions_never_take_more_than_half_an_item()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 1), Make.Clip("b", 4).WithTransition("fade", 3));
        Assert.Equal(0.5, Timeline.EffectiveTransition(layer, 1), 6);
    }

    [Fact]
    public void Transitions_only_play_when_items_touch()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 4, gap: 1).WithTransition("fade", 1));
        Assert.Equal(0, Timeline.EffectiveTransition(layer, 1));
        Assert.Equal(new[] { 0.0, 5 }, Make.Starts(layer));
    }

    [Fact]
    public void Audio_layers_use_the_audio_curve_to_decide_a_transition()
    {
        var layer = Make.Layer(LayerKind.Audio, Make.Clip("a", 4), Make.Clip("b", 4).WithTransition("fade", 1));
        Assert.Equal(0, Timeline.EffectiveTransition(layer, 1));
        layer.Items[1].TransitionIn.Audio = "qsin";
        Assert.Equal(1, Timeline.EffectiveTransition(layer, 1), 6);
    }

    [Fact]
    public void Item_at_time_prefers_the_incoming_item_during_a_transition()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 4).WithTransition("fade", 1));
        Assert.Equal(0, Timeline.ItemAt(layer, 2));
        Assert.Equal(1, Timeline.ItemAt(layer, 3.5));
        Assert.Equal(-1, Timeline.ItemAt(layer, 7.5));
    }

    [Fact]
    public void Nearest_item_finds_the_next_one_after_a_gap_or_the_last()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 2, gap: 3));
        Assert.Equal(1, Timeline.NearestItem(layer, 3));
        Assert.Equal(1, Timeline.NearestItem(layer, 100));
        Assert.Equal(-1, Timeline.NearestItem(Make.Layer(LayerKind.Video), 0));
    }
}

public sealed class ProjectEditorTests
{
    [Fact]
    public void Layer_names_are_numbered_by_kind()
    {
        var project = new Project();
        Assert.Equal("Video 1", ProjectEditor.AddLayer(project, LayerKind.Video).Name);
        Assert.Equal("Audio 1", ProjectEditor.AddLayer(project, LayerKind.Audio).Name);
        var second = ProjectEditor.AddLayer(project, LayerKind.Video);
        Assert.Equal("Video 2", second.Name);
        Assert.Equal(PositionPreset.BottomRight, second.Defaults.Placement.Position);
    }

    [Fact]
    public void Moving_a_layer_changes_its_number()
    {
        var project = new Project();
        var a = ProjectEditor.AddLayer(project, LayerKind.Video);
        var b = ProjectEditor.AddLayer(project, LayerKind.Video);
        var result = ProjectEditor.MoveLayer(project, b.Id, -1);
        Assert.True(result.Ok);
        Assert.Same(b, project.Layers[0]);
        Assert.Contains("layer 1", result.Message);
        Assert.False(ProjectEditor.MoveLayer(project, b.Id, -1).Ok);
        Assert.False(ProjectEditor.MoveLayer(project, a.Id, 1).Ok);
    }

    [Fact]
    public void Solo_mutes_others_and_pressing_again_restores()
    {
        var project = new Project();
        var a = ProjectEditor.AddLayer(project, LayerKind.Video);
        var b = ProjectEditor.AddLayer(project, LayerKind.Audio);
        var c = ProjectEditor.AddLayer(project, LayerKind.Audio);
        Assert.True(ProjectEditor.ToggleSolo(project, b.Id).Ok);
        Assert.True(a.Muted && !b.Muted && c.Muted);
        Assert.True(ProjectEditor.ToggleSolo(project, b.Id).Ok);
        Assert.True(!a.Muted && !b.Muted && !c.Muted);
        Assert.False(ProjectEditor.UnmuteAll(project).Ok);
    }

    [Fact]
    public void Ripple_delete_closes_the_space()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 3), Make.Clip("c", 1));
        var result = ProjectEditor.DeleteItem(layer, 1, ripple: true);
        Assert.True(result.Ok);
        Assert.Equal("Deleted item 2 of 3, b, 3 seconds long, from 2 seconds. 2 items left on Video 1.", result.Message);
        Assert.Equal(new[] { 0.0, 2 }, Make.Starts(layer));
        Assert.Equal(layer.Items[1].Id, result.FocusItemId);
    }

    [Fact]
    public void Delete_without_ripple_keeps_later_items_in_place()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 3), Make.Clip("c", 1));
        ProjectEditor.DeleteItem(layer, 1, ripple: false);
        Assert.Equal(new[] { 0.0, 5 }, Make.Starts(layer));
    }

    [Fact]
    public void Delete_without_ripple_keeps_timing_after_a_transition()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 4).WithTransition("fade", 1), Make.Clip("c", 2));
        var before = Timeline.Span(layer, 2).Start;
        ProjectEditor.DeleteItem(layer, 1, ripple: false);
        Assert.Equal(before, Timeline.Span(layer, 1).Start, 6);
    }

    [Fact]
    public void Moving_an_item_swaps_content_but_keeps_gaps_and_transitions_in_place()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 3, gap: 1), Make.Clip("c", 1).WithTransition("fade", 0.5));
        var result = ProjectEditor.MoveItem(layer, 2, -2);
        Assert.True(result.Ok);
        Assert.Equal(new[] { "c", "a", "b" }, layer.Items.Select(i => i.Name));
        Assert.Equal(1, layer.Items[1].GapBefore);
        Assert.Equal("fade", layer.Items[2].TransitionIn.Video);
        Assert.Equal(TransitionSpec.None, layer.Items[0].TransitionIn.Video);
        Assert.Contains("position 1 of 3", result.Message);
    }

    [Fact]
    public void Moving_past_the_ends_fails_politely()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 3));
        Assert.Equal("Already the first item.", ProjectEditor.MoveItem(layer, 0, -1).Message);
        Assert.Equal("Already the last item.", ProjectEditor.MoveItem(layer, 1, 1).Message);
    }

    [Fact]
    public void Slide_with_ripple_moves_later_items_too()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 2), Make.Clip("c", 2));
        Assert.True(ProjectEditor.SlideItem(layer, 1, 1.5, ripple: true).Ok);
        Assert.Equal(new[] { 0.0, 3.5, 5.5 }, Make.Starts(layer));
        Assert.True(ProjectEditor.SlideItem(layer, 1, -1, ripple: true).Ok);
        Assert.Equal(new[] { 0.0, 2.5, 4.5 }, Make.Starts(layer));
    }

    [Fact]
    public void Slide_without_ripple_keeps_the_next_item_in_place()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 2), Make.Clip("c", 2, gap: 3));
        Assert.True(ProjectEditor.SlideItem(layer, 1, 2, ripple: false).Ok);
        Assert.Equal(new[] { 0.0, 4, 7 }, Make.Starts(layer));
        var blocked = ProjectEditor.SlideItem(layer, 1, 5, ripple: false);
        Assert.False(blocked.Ok);
        Assert.Contains("in the way", blocked.Message);
    }

    [Fact]
    public void Slide_earlier_stops_at_the_previous_item_or_the_start()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2, gap: 1), Make.Clip("b", 2));
        Assert.True(ProjectEditor.SlideItem(layer, 0, -5, ripple: true).Ok);
        Assert.Equal(0, layer.Items[0].GapBefore);
        Assert.False(ProjectEditor.SlideItem(layer, 0, -1, ripple: true).Ok);
        Assert.False(ProjectEditor.SlideItem(layer, 1, -1, ripple: true).Ok);
    }

    [Fact]
    public void Split_makes_two_items_that_play_the_same_material()
    {
        var clip = Make.Clip("a", 10, sourceDuration: 20);
        clip.SourceIn = 5;
        clip.FadeIn = 1;
        clip.FadeOut = 2;
        var layer = Make.Layer(LayerKind.Video, clip);
        var result = ProjectEditor.SplitItem(layer, 0, 4);
        Assert.True(result.Ok);
        Assert.Equal(2, layer.Items.Count);
        Assert.Equal(4, layer.Items[0].Duration, 6);
        Assert.Equal(6, layer.Items[1].Duration, 6);
        Assert.Equal(9, layer.Items[1].SourceIn, 6);
        Assert.Equal(1, layer.Items[0].FadeIn);
        Assert.Equal(0, layer.Items[0].FadeOut);
        Assert.Equal(0, layer.Items[1].FadeIn);
        Assert.Equal(2, layer.Items[1].FadeOut);
        Assert.NotEqual(layer.Items[0].Id, layer.Items[1].Id);
        Assert.Equal(10, Timeline.LayerEnd(layer), 6);
    }

    [Fact]
    public void Split_refuses_at_the_very_edge()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 10));
        Assert.False(ProjectEditor.SplitItem(layer, 0, 0.05).Ok);
        Assert.False(ProjectEditor.SplitItem(layer, 0, 9.99).Ok);
    }

    [Fact]
    public void Trim_start_without_ripple_keeps_the_end()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 6, sourceDuration: 6), Make.Clip("c", 1));
        Assert.True(ProjectEditor.TrimStart(layer, 1, 4, ripple: false).Ok);
        Assert.Equal(new[] { 0.0, 4, 8 }, Make.Starts(layer));
        Assert.Equal(2, layer.Items[1].SourceIn, 6);
        Assert.Equal(4, layer.Items[1].Duration, 6);
    }

    [Fact]
    public void Trim_start_with_ripple_pulls_later_items_earlier()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 2), Make.Clip("b", 6), Make.Clip("c", 1));
        Assert.True(ProjectEditor.TrimStart(layer, 1, 4, ripple: true).Ok);
        Assert.Equal(new[] { 0.0, 2, 6 }, Make.Starts(layer));
    }

    [Fact]
    public void Trim_end_respects_ripple()
    {
        var rippled = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 2));
        Assert.True(ProjectEditor.TrimEnd(rippled, 0, 3, ripple: true).Ok);
        Assert.Equal(new[] { 0.0, 3 }, Make.Starts(rippled));

        var kept = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 2));
        Assert.True(ProjectEditor.TrimEnd(kept, 0, 3, ripple: false).Ok);
        Assert.Equal(new[] { 0.0, 4 }, Make.Starts(kept));
    }

    [Fact]
    public void Trim_outside_the_item_explains_what_to_do()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4));
        Assert.Contains("inside the item", ProjectEditor.TrimStart(layer, 0, 0, ripple: true).Message);
        Assert.Contains("inside the item", ProjectEditor.TrimEnd(layer, 0, 5, ripple: true).Message);
    }

    [Fact]
    public void Insert_at_time_uses_free_space_without_moving_anything()
    {
        var layer = Make.Layer(LayerKind.Audio, Make.Clip("a", 2), Make.Clip("b", 2, gap: 6));
        var effect = Make.Clip("fx", 1);
        Assert.True(ProjectEditor.InsertAtTime(layer, effect, 4).Ok);
        Assert.Equal(new[] { 0.0, 4, 8 }, Make.Starts(layer));
        Assert.False(ProjectEditor.InsertAtTime(layer, Make.Clip("big", 5), 5).Ok);
        Assert.True(ProjectEditor.InsertAtTime(layer, Make.Clip("late", 1), 20).Ok);
        Assert.Equal(20, Timeline.Span(layer, 3).Start, 6);
        Assert.False(ProjectEditor.HasRoomAt(layer, 1, 0.5));
    }

    [Fact]
    public void Delete_range_removes_the_middle_and_ripples()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 10, sourceDuration: 10), Make.Clip("b", 5));
        var result = ProjectEditor.DeleteRange(layer, 4, 6, ripple: true);
        Assert.True(result.Ok);
        Assert.Equal(13, Timeline.LayerEnd(layer), 6);
        Assert.Equal(new[] { "a", "a", "b" }, layer.Items.Select(i => i.Name));
        Assert.Equal(6, layer.Items[1].SourceIn, 6);
        Assert.Equal(new[] { 0.0, 4, 8 }, Make.Starts(layer));
    }

    [Fact]
    public void Delete_range_without_ripple_leaves_a_gap()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 10, sourceDuration: 10));
        Assert.True(ProjectEditor.DeleteRange(layer, 4, 6, ripple: false).Ok);
        Assert.Equal(10, Timeline.LayerEnd(layer), 6);
        Assert.Equal(new[] { 0.0, 6 }, Make.Starts(layer));
    }

    [Fact]
    public void Update_item_clamps_every_value()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4, sourceDuration: 10));
        var edited = layer.Items[0].Clone(newId: false);
        edited.Duration = 50;
        edited.Volume = 9;
        edited.FadeIn = 100;
        edited.TransitionIn = new TransitionSpec { Video = "not-a-transition", Audio = "qsin", Duration = 99 };
        edited.Placement = new Placement { SizePercent = 0, OpacityPercent = 300 };
        edited.Audio = new AudioCleanup { LowDb = 40, MidDb = -40 };
        Assert.True(ProjectEditor.UpdateItem(layer, 0, edited).Ok);
        var item = layer.Items[0];
        Assert.Equal(10, item.Duration);
        Assert.Equal(4, item.Volume);
        Assert.Equal(5, item.FadeIn);
        Assert.Equal(TransitionSpec.None, item.TransitionIn.Video);
        Assert.Equal("qsin", item.TransitionIn.Audio);
        Assert.Equal(10, item.TransitionIn.Duration);
        Assert.Equal(5, item.Placement.SizePercent);
        Assert.Equal(100, item.Placement.OpacityPercent);
        Assert.Equal(12, item.Audio.LowDb);
        Assert.Equal(-12, item.Audio.MidDb);
    }

    [Fact]
    public void Transition_at_a_marker_finds_the_nearest_join()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 4), Make.Clip("b", 4), Make.Clip("c", 4));
        var result = ProjectEditor.SetTransitionAt(layer, 8.2, new TransitionSpec { Video = "wipeleft", Duration = 1 });
        Assert.True(result.Ok);
        Assert.Equal("wipeleft", layer.Items[2].TransitionIn.Video);
        Assert.Contains("Wipe left", result.Message);
        Assert.False(ProjectEditor.SetTransitionAt(layer, 6, new TransitionSpec { Video = "fade" }).Ok);
    }

    [Fact]
    public void Markers_are_sorted_unique_and_navigable()
    {
        var project = new Project();
        Assert.True(ProjectEditor.AddMarker(project, 10).Ok);
        Assert.True(ProjectEditor.AddMarker(project, 2, "Intro").Ok);
        Assert.False(ProjectEditor.AddMarker(project, 10.01).Ok);
        ProjectEditor.SetRangeStart(project, 5);
        Assert.Equal(new[] { 2.0, 10 }, project.Markers.Select(m => m.Time));
        Assert.Equal(5, ProjectEditor.NeighbourMarker(project, 2, 1));
        Assert.Equal(10, ProjectEditor.NeighbourMarker(project, 5, 1));
        Assert.Null(ProjectEditor.NeighbourMarker(project, 10, 1));
        Assert.Equal(2, ProjectEditor.NeighbourMarker(project, 5, -1));
        Assert.Equal("Range start", ProjectEditor.MarkerLabelAt(project, 5));
        Assert.Equal("Intro", ProjectEditor.MarkerLabelAt(project, 2));
        Assert.Equal("Marker 3", ProjectEditor.NextMarkerName(project));
    }

    [Fact]
    public void Range_start_and_end_are_swapped_if_set_backwards()
    {
        var project = new Project();
        ProjectEditor.SetRangeStart(project, 8);
        var result = ProjectEditor.SetRangeEnd(project, 3);
        Assert.Equal(3, project.RangeStart);
        Assert.Equal(8, project.RangeEnd);
        Assert.Contains("swapped", result.Message);
        Assert.True(ProjectEditor.ClearRange(project).Ok);
        Assert.False(ProjectEditor.ClearRange(project).Ok);
    }
}

public sealed class ProjectSessionTests
{
    private static ProjectSession NewSession()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(Make.Clip("a", 2));
        return new ProjectSession(project);
    }

    [Fact]
    public void Undo_and_redo_restore_the_project()
    {
        var session = NewSession();
        session.Execute("delete", p => ProjectEditor.DeleteItem(p.Layers[0], 0, true));
        Assert.Empty(session.Project.Layers[0].Items);
        Assert.True(session.IsDirty);
        Assert.Equal("Undo delete.", session.Undo().Message);
        Assert.Single(session.Project.Layers[0].Items);
        Assert.Equal("Redo delete.", session.Redo().Message);
        Assert.Empty(session.Project.Layers[0].Items);
        Assert.False(session.Redo().Ok);
    }

    [Fact]
    public void A_failed_edit_changes_nothing_and_adds_no_undo_step()
    {
        var session = NewSession();
        var result = session.Execute("bad", p =>
        {
            p.Layers[0].Items[0].Duration = 99;
            return EditResult.Fail("nope");
        });
        Assert.False(result.Ok);
        Assert.Equal(2, session.Project.Layers[0].Items[0].Duration);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void An_exception_in_an_edit_is_rolled_back()
    {
        var session = NewSession();
        var result = session.Execute("crash", p =>
        {
            p.Layers.Clear();
            throw new InvalidOperationException("boom");
        });
        Assert.False(result.Ok);
        Assert.Contains("boom", result.Message);
        Assert.Single(session.Project.Layers);
    }

    [Fact]
    public void Undo_history_is_capped()
    {
        var session = NewSession();
        for (var i = 0; i < ProjectSession.MaxUndo + 20; i++)
        {
            session.Execute("mute", p => ProjectEditor.ToggleMute(p, p.Layers[0].Id));
        }

        var count = 0;
        while (session.Undo().Ok)
        {
            count++;
        }

        Assert.Equal(ProjectSession.MaxUndo, count);
    }

    [Fact]
    public void Saved_projects_load_back_identically()
    {
        var session = NewSession();
        var item = session.Project.Layers[0].Items[0];
        item.TransitionIn = new TransitionSpec { Video = "fade", Audio = "qsin", Duration = 0.5 };
        item.Audio = new AudioCleanup { NoiseReduction = NoiseReduction.Medium, LowDb = 3 };
        item.Placement = new Placement { Position = PositionPreset.TopRight, SizePercent = 20 };
        session.Project.Markers.Add(new Marker { Time = 1, Name = "Here" });
        session.Project.RangeStart = 0.5;

        var path = Path.Combine(Path.GetTempPath(), $"ave-{Guid.NewGuid():N}{ProjectSerializer.FileExtension}");
        try
        {
            session.Save(path);
            Assert.False(session.IsDirty);
            var loaded = ProjectSerializer.Load(path);
            Assert.Equal(ProjectSerializer.ToJson(session.Project), ProjectSerializer.ToJson(loaded));
            var json = File.ReadAllText(path);
            Assert.Contains("\"position\": \"TopRight\"", json);
            Assert.DoesNotContain("availableDuration", json);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Moved_media_is_found_next_to_the_project()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"ave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var media = Path.Combine(folder, "clip.mp4");
            File.WriteAllText(media, "x");
            var project = new Project();
            var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
            layer.Items.Add(new Item { Kind = ItemKind.Video, Path = @"C:\Old place\clip.mp4", Name = "clip.mp4", Duration = 1 });
            var path = Path.Combine(folder, "p.avproj");
            ProjectSerializer.Save(project, path);
            Assert.Equal(media, ProjectSerializer.Load(path).Layers[0].Items[0].Path);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Newer_project_files_are_refused_with_a_clear_message()
    {
        var json = ProjectSerializer.ToJson(new Project()).Replace("\"formatVersion\": 1", "\"formatVersion\": 99");
        var error = Assert.Throws<InvalidDataException>(() => ProjectSerializer.FromJson(json));
        Assert.Contains("newer version", error.Message);
    }

    [Fact]
    public void Damaged_values_are_repaired_on_load()
    {
        const string json = """{"width":0,"height":0,"frameRate":0,"layers":[{"kind":"Video","items":[{"duration":-5,"gapBefore":-1}]}]}""";
        var project = ProjectSerializer.FromJson(json);
        Assert.Equal(1920, project.Width);
        Assert.Equal(30, project.FrameRate);
        Assert.Equal(Timeline.MinItemDuration, project.Layers[0].Items[0].Duration);
        Assert.Equal(0, project.Layers[0].Items[0].GapBefore);
    }
}
