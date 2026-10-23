using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class StretchAndSelectionTests
{
    private static (EditorController Editor, Layer Layer) Setup(bool ripple, params Item[] items)
    {
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.AddRange(items);
        var editor = new EditorController(new ProjectSession(project), new AppSettings { Ripple = ripple });
        return (editor, editor.Project.Layers[0]);
    }

    private static Feedback Press(EditorController editor, CommandId command) =>
        editor.Execute(command) ?? throw new InvalidOperationException($"{command} needs the window.");

    private static Layer Current(EditorController editor) => editor.Project.Layers[0];

    [Fact]
    public void A_clip_played_twice_as_fast_is_half_as_long_and_keeps_its_content()
    {
        var clip = Make.Clip("talk.mp4", 10, sourceDuration: 60);
        clip.SourceIn = 5;
        var (editor, _) = Setup(true, clip, Make.Clip("next.mp4", 4));
        var feedback = Press(editor, CommandId.Faster);
        var item = Current(editor).Items[0];
        Assert.Equal(1.25, item.Speed);
        Assert.Equal(8, item.Duration, 6);
        Assert.Equal(15, item.SourceOut, 6);
        Assert.Contains("1.25 times speed, now 8 seconds long", feedback.Speech);
        Press(editor, CommandId.Faster);
        Press(editor, CommandId.Faster);
        Assert.Equal(2, Current(editor).Items[0].Speed);
        Assert.Equal(5, Current(editor).Items[0].Duration, 6);
        Assert.Equal(5, Timeline.Span(Current(editor), 1).Start, 6); // Ripple: the next clip moved up.
        Press(editor, CommandId.NormalSpeed);
        Assert.Equal(10, Current(editor).Items[0].Duration, 6);
    }

    [Fact]
    public void Slow_motion_stops_at_a_quarter_speed_and_never_runs_past_the_file()
    {
        var (editor, _) = Setup(true, Make.Clip("a.mp4", 4, sourceDuration: 4));
        for (var i = 0; i < 6; i++)
        {
            Press(editor, CommandId.Slower);
        }

        var item = Current(editor).Items[0];
        Assert.Equal(0.25, item.Speed);
        Assert.Equal(16, item.Duration, 6);
        Assert.Equal(EarconEvent.Edge, Press(editor, CommandId.Slower).Earcon);
        // Making it longer can't show more file than there is.
        Assert.Equal(EarconEvent.Error, Press(editor, CommandId.Longer).Earcon);
    }

    [Fact]
    public void Pictures_and_text_stretch_by_length_and_have_no_speed()
    {
        var picture = new Item { Kind = ItemKind.Image, Name = "photo.jpg", Path = "photo.jpg", Duration = 5, HasVideo = true };
        var (editor, _) = Setup(true, picture);
        Press(editor, CommandId.Longer);
        Assert.Equal(6, Current(editor).Items[0].Duration, 6);
        Press(editor, CommandId.ShorterFine);
        Assert.Equal(5.9, Current(editor).Items[0].Duration, 6);
        var speed = Press(editor, CommandId.Faster);
        Assert.Equal(EarconEvent.Error, speed.Earcon);
        Assert.Contains("not a video or sound clip", speed.Speech);
    }

    [Fact]
    public void Without_ripple_stretching_uses_the_gap_and_the_next_item_keeps_its_time()
    {
        var (editor, _) = Setup(false, Make.Clip("a", 4, sourceDuration: 20), Make.Clip("b", 3, gap: 1.5));
        Press(editor, CommandId.Longer);
        Assert.Equal(5, Current(editor).Items[0].Duration, 6);
        Assert.Equal(5.5, Timeline.Span(Current(editor), 1).Start, 6);
        var blocked = Press(editor, CommandId.Longer);
        Assert.Equal(EarconEvent.Error, blocked.Earcon);
        Assert.Contains("next item is in the way", blocked.Speech);
        Press(editor, CommandId.Shorter);
        Assert.Equal(5.5, Timeline.Span(Current(editor), 1).Start, 6);
    }

    [Fact]
    public void Shift_arrows_select_a_range_and_delete_removes_all_of_it_in_one_undo()
    {
        var (editor, _) = Setup(true, Make.Clip("a", 2), Make.Clip("b", 3), Make.Clip("c", 4), Make.Clip("d", 5));
        Press(editor, CommandId.NextItem);
        var feedback = Press(editor, CommandId.SelectNext);
        Assert.Contains("c. 2 items selected, 7 seconds in all.", feedback.Speech);
        Assert.Equal([1, 2], editor.SelectedIndexes);
        Press(editor, CommandId.SelectPrevious);
        Press(editor, CommandId.SelectPrevious);
        Assert.Equal([0, 1], editor.SelectedIndexes); // The range follows from where it was started.
        var deleted = Press(editor, CommandId.Delete);
        Assert.Contains("Deleted 2 selected items, 5 seconds in all. 2 items left", deleted.Speech);
        Assert.Equal(["c", "d"], Current(editor).Items.Select(i => i.Name));
        Assert.False(editor.HasSelection);
        Press(editor, CommandId.Undo);
        Assert.Equal(4, Current(editor).Items.Count);
    }

    [Fact]
    public void Control_space_picks_items_apart_and_actions_apply_to_each()
    {
        var (editor, _) = Setup(true, Make.Clip("a", 2), Make.Clip("b", 3), Make.Clip("c", 4));
        Press(editor, CommandId.ToggleSelect);
        Press(editor, CommandId.NextItem);
        Press(editor, CommandId.NextItem);
        Assert.Contains("Selected c. 2 items selected", Press(editor, CommandId.ToggleSelect).Speech);
        Press(editor, CommandId.ToggleItemMute);
        Assert.Equal([true, false, true], Current(editor).Items.Select(i => i.AudioMuted));
        Press(editor, CommandId.ItemVolumeDown);
        Assert.Equal([0.9, 1.0, 0.9], Current(editor).Items.Select(i => i.Volume));
        Press(editor, CommandId.Faster);
        Assert.Equal([1.25, 1.0, 1.25], Current(editor).Items.Select(i => i.Speed));

        Press(editor, CommandId.Duplicate);
        Assert.Equal(["a", "b", "c", "a", "c"], Current(editor).Items.Select(i => i.Name));
        Assert.Equal([3, 4], editor.SelectedIndexes); // The copies are selected now.

        Press(editor, CommandId.Copy);
        Assert.Contains("Selection of 2 items cleared", Press(editor, CommandId.Stop).Speech);
        Press(editor, CommandId.FirstItem);
        Assert.Contains("Pasted 2 items", Press(editor, CommandId.Paste).Speech);
        Assert.Equal(["a", "a", "c", "b", "c", "a", "c"], Current(editor).Items.Select(i => i.Name));
    }

    [Fact]
    public void Select_all_then_slide_moves_the_group_without_ripple()
    {
        var (editor, _) = Setup(false, Make.Clip("a", 2), Make.Clip("b", 3, gap: 1));
        Press(editor, CommandId.SelectAll);
        Press(editor, CommandId.SlideLater);
        Assert.Equal([1.0, 4.0], Make.Starts(Current(editor)));
        Press(editor, CommandId.SlideEarlierFine);
        Assert.Equal([0.9, 3.9], Make.Starts(Current(editor)));
    }

    [Fact]
    public void Holding_a_key_speeds_up_after_two_and_four_seconds_and_folds_into_one_undo()
    {
        var hold = new HoldAccelerator();
        var key = new KeyChord("L", false, true, true);
        var t = new DateTime(2026, 1, 1);
        Assert.Equal(1, hold.Factor(key, false, t));
        Assert.Equal(1, hold.Factor(key, true, t.AddSeconds(1.9)));
        Assert.Equal(4, hold.Factor(key, true, t.AddSeconds(2.1)));
        Assert.Equal(10, hold.Factor(key, true, t.AddSeconds(4.5)));
        Assert.Equal(1, hold.Factor(key, false, t.AddSeconds(5))); // Let go and press again.
        Assert.Equal(1, hold.Factor(new KeyChord("J", false, true, true), true, t.AddSeconds(9)));

        var (editor, _) = Setup(true, Make.Clip("a", 2), Make.Clip("b", 3));
        Press(editor, CommandId.NextItem);
        Press(editor, CommandId.SlideLaterFine);
        editor.Repeating = true;
        editor.HoldFactor = 10;
        Press(editor, CommandId.SlideLaterFine);
        Press(editor, CommandId.SlideLaterFine);
        editor.Repeating = false;
        editor.HoldFactor = 1;
        Assert.Equal(4.1, Timeline.Span(Current(editor), 1).Start, 6);
        Press(editor, CommandId.Undo);
        Assert.Equal(2, Timeline.Span(Current(editor), 1).Start, 6); // One undo takes back the whole hold.
    }

    [Fact]
    public void Captions_transcript_and_pause_cuts_follow_a_fast_clip()
    {
        var clip = Make.Clip("talk.mp4", 10, sourceDuration: 30);
        clip.SourceIn = 10;
        clip.Speed = 2;
        clip.Duration = 5;
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(clip);
        project.Transcripts.Add(new Transcript
        {
            MediaPath = "talk.mp4",
            Words = [new() { Text = "before", Start = 5, End = 6 }, new() { Text = "hello", Start = 12, End = 13 }, new() { Text = "world", Start = 18, End = 19 }],
        });
        var words = Captions.WordsOnTimeline(project, layer);
        Assert.Equal(["hello", "world"], words.Select(w => w.Text));
        Assert.Equal(1, words[0].Start, 6); // File 12 s is 1 s into the clip at double speed.
        Assert.Equal(4.5, words[1].End, 6);
        Assert.Equal(Make.Starts(layer), [0.0]);
        Assert.Equal(20, TranscriptCoverage.Used(project, "talk.mp4")[0].End - TranscriptCoverage.Margin, 6);

        var cut = CutEditing.RemoveSourceRanges(layer, 0, [new Audio.TimeStretch(14, 16)], 30, "pauses");
        Assert.True(cut.Ok, cut.Message);
        Assert.Equal([2.0, 2.0], layer.Items.Select(i => Math.Round(i.Duration, 6)));
        Assert.Equal([10.0, 16.0], layer.Items.Select(i => i.SourceIn));
    }
}
