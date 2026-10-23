using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Online;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Templates;
using AccessibleVideoEditor.Core.Tests.Support;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class PositionModeTests
{
    private static EditorController EditorWithLogo(out Item logo)
    {
        var project = new Project { Width = 1000, Height = 1000 };
        var main = ProjectEditor.AddLayer(project, LayerKind.Video);
        main.Items.Add(MediaProbe.CreateColorCard("Black", 10, new LayerDefaults()));
        var top = ProjectEditor.AddLayer(project, LayerKind.Video);
        logo = Make.Clip("logo.png", 10);
        logo.Kind = ItemKind.Image;
        logo.HasAudio = false;
        logo.SourceWidth = 100;
        logo.SourceHeight = 100;
        logo.Placement = new Placement { Position = PositionPreset.TopLeft, SizePercent = 20 };
        top.Items.Add(logo);
        var editor = new EditorController(new ProjectSession(project), new AppSettings());
        editor.GoToLayer(2, listenOnly: false);
        return editor;
    }

    private static FrameRect Rect(EditorController e) => Geometry.RectOf(e.FocusedItem!, e.Project.Width, e.Project.Height);

    [Fact]
    public void P_enters_the_mode_and_explains_the_keys()
    {
        var editor = EditorWithLogo(out _);
        var feedback = editor.Execute(CommandId.PositionMode)!;
        Assert.True(editor.InPositionMode);
        Assert.StartsWith("Position mode for logo.png. Now top left, 20 percent wide and 20 percent high", feedback.Speech);
        Assert.Contains("number pad", feedback.Speech);
        Assert.NotNull(feedback.Position);
    }

    [Fact]
    public void Arrows_move_and_report_the_position_and_edges()
    {
        var editor = EditorWithLogo(out _);
        editor.Execute(CommandId.PositionMode);
        var right = editor.Position(PositionAction.Right);
        Assert.Equal("9 percent across, 4 percent down.", right.Speech);
        Assert.Equal(PositionPreset.Custom, editor.FocusedItem!.Placement.Position);
        editor.Position(PositionAction.Right, fine: true);
        Assert.Equal(100, Rect(editor).X, 0);

        for (var i = 0; i < 30; i++)
        {
            editor.Position(PositionAction.Up);
        }

        var edge = editor.Position(PositionAction.Up);
        Assert.Equal("Already at the top edge.", edge.Speech);
        Assert.Equal(EarconEvent.Edge, edge.Earcon);
        Assert.Contains("touching the top edge", editor.Position(PositionAction.Left).Speech);
        var pan = editor.Position(PositionAction.Right).Position!.Value;
        Assert.InRange(pan.Y, 0, 0.2);
    }

    [Fact]
    public void Size_snap_full_frame_and_fill()
    {
        var editor = EditorWithLogo(out _);
        editor.Execute(CommandId.PositionMode);
        Assert.Equal("Size 25 percent, 25 percent wide and 25 percent high.", editor.Position(PositionAction.Bigger).Speech);
        editor.Position(PositionAction.Snap, snap: 3);
        Assert.Equal(PositionPreset.BottomRight, editor.FocusedItem!.Placement.Position);
        Assert.Equal(EarconEvent.Edge, editor.Position(PositionAction.ToggleFill).Earcon);
        Assert.StartsWith("full frame", editor.Position(PositionAction.FullFrame).Speech);
        Assert.Contains("A full frame picture can't move", editor.Position(PositionAction.Left).Speech);
        Assert.StartsWith("Fill", editor.Position(PositionAction.ToggleFill).Speech);
        Assert.True(editor.FocusedItem!.Placement.Fill);
        editor.Position(PositionAction.Smaller);
        Assert.Equal(PositionPreset.Center, editor.FocusedItem!.Placement.Position);
        Assert.Equal(95, editor.FocusedItem!.Placement.SizePercent);
    }

    [Fact]
    public void Crop_and_opacity_steps()
    {
        var editor = EditorWithLogo(out _);
        editor.Execute(CommandId.PositionMode);
        editor.Position(PositionAction.CropLeft);
        editor.Position(PositionAction.CropLeft);
        Assert.Equal(10, editor.FocusedItem!.Placement.CropLeft);
        editor.Position(PositionAction.UncropLeft);
        Assert.Equal(5, editor.FocusedItem!.Placement.CropLeft);
        Assert.Contains("cropped 5 percent off the left", editor.Position(PositionAction.Describe).Speech);
        editor.Position(PositionAction.ResetCrop);
        Assert.False(editor.FocusedItem!.Placement.HasCrop);
        Assert.Equal("Opacity 90 percent.", editor.Position(PositionAction.OpacityDown).Speech);
        Assert.Equal("Opacity 100 percent.", editor.Position(PositionAction.OpacityUp).Speech);
        Assert.Equal("Already fully solid.", editor.Position(PositionAction.OpacityUp).Speech);
    }

    [Fact]
    public void Enter_keeps_and_escape_undoes_everything()
    {
        var editor = EditorWithLogo(out _);
        editor.Execute(CommandId.PositionMode);
        editor.Position(PositionAction.Right);
        editor.Position(PositionAction.Bigger);
        var cancelled = editor.Position(PositionAction.Cancel);
        Assert.False(editor.InPositionMode);
        Assert.Contains("back where it was", cancelled.Speech);
        Assert.Equal(PositionPreset.TopLeft, editor.FocusedItem!.Placement.Position);
        Assert.Equal(20, editor.FocusedItem!.Placement.SizePercent);

        editor.Execute(CommandId.PositionMode);
        editor.Position(PositionAction.Snap, snap: 5);
        Assert.StartsWith("Position kept. centre", editor.Position(PositionAction.Done).Speech);
        Assert.Equal(PositionPreset.Center, editor.FocusedItem!.Placement.Position);
        Assert.Equal(EarconEvent.Error, editor.Position(PositionAction.Right).Earcon);
    }

    [Fact]
    public void Position_mode_needs_a_picture()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Audio).Items.Add(Make.Clip("song.mp3", 3));
        var editor = new EditorController(new ProjectSession(project), new AppSettings());
        Assert.Equal(EarconEvent.Error, editor.Execute(CommandId.PositionMode)!.Earcon);
        Assert.False(editor.InPositionMode);
    }

    [Fact]
    public void Text_size_changes_with_plus_and_minus()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(
            MediaProbe.CreateTextItem(new TextStyle { Content = "Hello", SizePercent = 8 }, 3, new LayerDefaults()));
        var editor = new EditorController(new ProjectSession(project), new AppSettings());
        editor.Execute(CommandId.PositionMode);
        Assert.Equal("Text size 9 percent of the frame height.", editor.Position(PositionAction.Bigger).Speech);
        Assert.Equal("Text can't be cropped.", editor.Position(PositionAction.CropTop).Speech);
    }
}

public sealed class SoundAndTextCommandTests
{
    private static EditorController Editor()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(Make.Clip("main.mp4", 10));
        ProjectEditor.AddLayer(project, LayerKind.Audio, "Music").Items.Add(Make.Clip("song.mp3", 10));
        return new EditorController(new ProjectSession(project), new AppSettings());
    }

    [Fact]
    public void Item_volume_and_mute_keys()
    {
        var editor = Editor();
        Assert.Equal("Volume 110 percent.", editor.Execute(CommandId.ItemVolumeUp)!.Speech);
        Assert.Equal("Volume 100 percent.", editor.Execute(CommandId.ItemVolumeDown)!.Speech);
        Assert.Equal("main.mp4 sound muted.", editor.Execute(CommandId.ToggleItemMute)!.Speech);
        Assert.True(editor.FocusedItem!.AudioMuted);
        editor.Execute(CommandId.ItemVolumeUp);
        Assert.False(editor.FocusedItem!.AudioMuted);
        for (var i = 0; i < 40; i++)
        {
            editor.Execute(CommandId.ItemVolumeUp);
        }

        Assert.Equal(EarconEvent.Edge, editor.Execute(CommandId.ItemVolumeUp)!.Earcon);
        Assert.Equal(4, editor.FocusedItem!.Volume);
    }

    [Fact]
    public void Layer_volume_keys()
    {
        var editor = Editor();
        editor.GoToLayer(2, false);
        Assert.Equal("Music volume 90 percent.", editor.Execute(CommandId.LayerVolumeDown)!.Speech);
        Assert.Equal(0.9, editor.FocusedLayer!.Volume, 6);
    }

    [Fact]
    public void Pictures_without_sound_say_so()
    {
        var project = new Project();
        var card = MediaProbe.CreateColorCard("Red", 2, new LayerDefaults());
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(card);
        var editor = new EditorController(new ProjectSession(project), new AppSettings());
        Assert.Equal("Red colour card has no sound.", editor.Execute(CommandId.ItemVolumeUp)!.Speech);
    }

    [Fact]
    public void Added_text_goes_above_the_main_video_on_a_text_layer()
    {
        var editor = Editor();
        editor.GoToTime(2);
        var feedback = editor.AddText(new TextStyle { Content = "Welcome" }, 3, new Placement { Position = PositionPreset.BottomCenter });
        Assert.StartsWith("Added text \"Welcome\" on layer 3 at 2 seconds, bottom centre", feedback.Speech);
        Assert.Equal("Text", editor.FocusedLayer!.Name);
        Assert.Equal(2, Timeline.Span(editor.FocusedLayer, 0).Start, 6);
        Assert.Equal(ItemKind.Text, editor.FocusedItem!.Kind);

        editor.GoToTime(6);
        editor.AddText(new TextStyle { Content = "Second" }, 2, new Placement());
        Assert.Equal(3, editor.Project.Layers.Count);
        Assert.Equal(EarconEvent.Error, editor.AddText(new TextStyle { Content = "  " }, 2, new Placement()).Earcon);

        var withLogo = new EditorController(new ProjectSession(ProjectTemplates.Find("youtube")!.Create()), new AppSettings());
        withLogo.AddText(new TextStyle { Content = "Hi" }, 2, new Placement());
        Assert.Equal("Text", withLogo.FocusedLayer!.Name);
        Assert.Empty(withLogo.Project.Layers.Single(l => l.Name == "Logo").Items);
    }

    [Fact]
    public void Describe_picture_command_speaks_the_frame()
    {
        var editor = Editor();
        Assert.StartsWith("At 0 seconds. Layer 1, main.mp4", editor.Execute(CommandId.DescribePicture)!.Speech);
    }

    [Fact]
    public void Function_keys_are_not_timeline_only_but_letters_are()
    {
        Assert.False(CommandCatalog.Get(CommandId.PreviousPane).IsTimelineOnly);
        Assert.False(CommandCatalog.Get(CommandId.SwitchPane).IsTimelineOnly);
        Assert.False(CommandCatalog.Get(CommandId.FocusLibrary).IsTimelineOnly);
        Assert.True(CommandCatalog.Get(CommandId.PositionMode).IsTimelineOnly);
        Assert.True(CommandCatalog.Get(CommandId.TrimStart).IsTimelineOnly);
    }
}

public sealed class TemplateLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ave-templates-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static Project Sample()
    {
        var project = new Project { Title = "My vlog" };
        var main = ProjectEditor.AddLayer(project, LayerKind.Video, "Main");
        main.Items.Add(Make.Clip("clip.mp4", 5));
        main.Defaults.Transition = new TransitionSpec { Video = "fade", Duration = 1 };
        var titles = ProjectEditor.AddLayer(project, LayerKind.Video, "Titles");
        titles.Items.Add(MediaProbe.CreateTextItem(new TextStyle { Content = "Episode 1" }, 3, new LayerDefaults()));
        project.Markers.Add(new Marker { Time = 2, Name = "Here" });
        return project;
    }

    [Fact]
    public void Built_in_templates_are_listed_first()
    {
        var library = new TemplateLibrary(_root);
        var list = library.List();
        Assert.Equal(ProjectTemplates.All.Count, list.Count);
        Assert.All(list, t => Assert.Equal(TemplateLibrary.BuiltInCategory, t.Category));
        Assert.Equal(new[] { TemplateLibrary.DefaultCategory }, library.Categories());
    }

    [Fact]
    public void Saving_without_media_keeps_layers_defaults_and_text()
    {
        var library = new TemplateLibrary(_root);
        var saved = library.Save(Sample(), "Vlog: intro?", "Vlogs", "Intro title and fade", includeMedia: false);
        Assert.Equal("Vlog- intro-", saved.Name);
        var listed = Assert.Single(library.List(), t => !t.IsBuiltIn);
        Assert.Equal("Vlogs", listed.Category);
        Assert.Contains("Vlogs", library.Categories());

        var project = library.Create(listed);
        Assert.Equal("Vlog- intro-", project.Title);
        Assert.Empty(project.Layers[0].Items);
        Assert.Equal("fade", project.Layers[0].Defaults.Transition.Video);
        Assert.Equal(ItemKind.Text, Assert.Single(project.Layers[1].Items).Kind);
        Assert.Empty(project.Markers);
        Assert.NotEqual(Sample().Layers[0].Id, project.Layers[0].Id);
    }

    [Fact]
    public void Saving_with_media_keeps_everything()
    {
        var library = new TemplateLibrary(_root);
        var saved = library.Save(Sample(), "Full", TemplateLibrary.DefaultCategory, "", includeMedia: true);
        Assert.True(saved.IncludesMedia);
        var project = library.Create(library.List().Single(t => t.Name == "Full"));
        Assert.Single(project.Layers[0].Items);
        Assert.Single(project.Markers);
        Assert.Equal(TemplateLibrary.DefaultCategory, library.List().Single(t => t.Name == "Full").Category);
    }

    [Fact]
    public void Rename_move_import_and_delete()
    {
        var library = new TemplateLibrary(_root);
        var t = library.Save(Sample(), "One", "A", "", false);
        t = library.Rename(t, "Two");
        Assert.Equal("Two", t.Name);
        t = library.Move(t, "B");
        Assert.Equal("B", library.List().Single(x => !x.IsBuiltIn).Category);
        var imported = library.Import(t.FilePath!, "A");
        Assert.Equal(2, library.List().Count(x => !x.IsBuiltIn));
        library.Delete(imported);
        Assert.Single(library.List(), x => !x.IsBuiltIn);
        Assert.Throws<InvalidOperationException>(() => library.Delete(library.List()[0]));
        library.Save(Sample(), "Clash", "B", "", false);
        Assert.Throws<IOException>(() => library.Rename(library.List().Single(x => x.Name == "Two"), "Clash"));
    }

    [Fact]
    public void Damaged_template_files_are_skipped()
    {
        var library = new TemplateLibrary(_root);
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "broken" + TemplateLibrary.FileExtension), "{ nope");
        Assert.Equal(ProjectTemplates.All.Count, library.List().Count);
    }

    [Fact]
    public void Text_presets_are_valid()
    {
        Assert.True(TextPresets.All.Count >= 5);
        foreach (var preset in TextPresets.All)
        {
            var clamped = ProjectEditor.ClampText(preset.Style);
            Assert.Equal(preset.Style.SizePercent, clamped.SizePercent);
            Assert.Contains(FontCatalog.Fonts, f => f.Name == clamped.Font);
            Assert.True(preset.Duration > 0);
        }
    }
}

public sealed class OnlineAndCreditsTests
{
    private const string Sample = """
        {"result_count":3,"page_count":1,"page_size":20,"page":1,"results":[
         {"id":"a1","title":"Calm Piano","creator":"Ann","license":"by","license_version":"4.0","license_url":"https://creativecommons.org/licenses/by/4.0/",
          "attribution":"\"Calm Piano\" by Ann is licensed under CC BY 4.0.","url":"https://cdn.example.org/a1.mp3","foreign_landing_url":"https://example.org/a1",
          "filetype":"mp32","duration":72000,"source":"jamendo"},
         {"id":"b2","title":"Rain","creator":"Bo","license":"cc0","license_version":"1.0","url":"https://cdn.example.org/b2.wav","filetype":"wav","duration":5000,"source":"freesound"},
         {"id":"c3","title":"Not allowed","creator":"Cy","license":"by-nc","url":"https://cdn.example.org/c3.mp3","duration":1000}
        ]}
        """;

    [Fact]
    public void Search_url_asks_only_for_licences_that_allow_any_use()
    {
        var uri = OpenverseClient.SearchUri("calm piano & rain", OnlineAudioCategory.Music, 2);
        Assert.StartsWith("https://api.openverse.org/v1/audio/?", uri.AbsoluteUri);
        Assert.Contains("license=cc0,pdm,by", uri.Query);
        Assert.Contains("category=music", uri.Query);
        Assert.Contains("q=calm%20piano%20%26%20rain", uri.Query);
        Assert.Contains("page=2", uri.Query);
        Assert.Contains("category=sound_effect", OpenverseClient.SearchUri("x", OnlineAudioCategory.SoundEffect, 1).Query);
    }

    [Fact]
    public void Results_are_parsed_filtered_and_explained()
    {
        var result = OpenverseClient.Parse(Sample);
        Assert.Equal(2, result.Items.Count);
        var piano = result.Items[0];
        Assert.Equal("mp3", piano.FileType);
        Assert.Equal(72, piano.DurationSeconds);
        Assert.False(piano.IsPublicDomain);
        Assert.Equal("Calm Piano by Ann, 1 minute 12 seconds, CC BY 4.0: free for any use, including commercial, but you must credit Ann, from jamendo", piano.Spoken);
        Assert.Contains("Source: https://example.org/a1", piano.Credit);
        Assert.True(result.Items[1].IsPublicDomain);
        Assert.StartsWith("CC0, public domain", result.Items[1].LicenseText);
    }

    [Fact]
    public async Task Empty_searches_are_refused_before_going_online()
    {
        var client = new OpenverseClient(new HttpClient());
        await Assert.ThrowsAsync<OnlineSearchException>(() => client.SearchAsync("  ", OnlineAudioCategory.Music));
    }

    [Fact]
    public void Credits_follow_the_media_that_needs_them()
    {
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        layer.Items.Add(Make.Clip("/music/song.mp3", 3));
        Assert.True(Credits.Add(project, "Song by Ann, CC BY", "/music/song.mp3"));
        Assert.False(Credits.Add(project, "Song by Ann, CC BY", "/music/song.mp3"));
        Credits.Add(project, "Removed by Bo", "/music/gone.mp3");
        Credits.Add(project, "Thanks to my friends", "");
        Assert.Equal(new[] { "Song by Ann, CC BY", "Thanks to my friends" }, Credits.InUse(project));

        var folder = Path.Combine(Path.GetTempPath(), "ave-credits-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var path = Credits.WriteFor(project, Path.Combine(folder, "My video.mp4"));
            Assert.Equal(Path.Combine(folder, "My video credits.txt"), path);
            Assert.Contains("Song by Ann, CC BY", File.ReadAllText(path!));
            var media = Path.Combine(folder, "track.mp3");
            File.WriteAllText(media + ".credit.txt", "Track by Cy\nCC BY 4.0");
            Assert.Equal("Track by Cy", Credits.ForMediaFile(media));
            Assert.Null(Credits.ForMediaFile(Path.Combine(folder, "none.mp3")));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Adding_a_downloaded_sound_records_its_credit()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ave-credit-add-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var media = Path.Combine(folder, "song.mp3");
            File.WriteAllText(media + ".credit.txt", "\"Song\" by Ann is licensed under CC BY 4.0.\nCC BY");
            var editor = new EditorController(new ProjectSession(new Project()), new AppSettings());
            editor.AddMedia([new MediaInfo { Path = media, Kind = ItemKind.Audio, HasAudio = true, Duration = 3 }], atPlayhead: true);
            Assert.Equal("\"Song\" by Ann is licensed under CC BY 4.0.", Assert.Single(editor.Project.Credits).Text);
            Assert.Contains(Checks.ProjectChecker.Check(editor.Project), f => f.Message.Contains("need") && f.Message.Contains("credit"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
