using AccessibleVideoEditor.Core.Checks;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Templates;
using AccessibleVideoEditor.Core.Text;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0 seconds")]
    [InlineData(1, "1 second")]
    [InlineData(5.5, "5.5 seconds")]
    [InlineData(60, "1 minute")]
    [InlineData(65.04, "1 minute 5 seconds")]
    [InlineData(125.25, "2 minutes 5.3 seconds")]
    [InlineData(3723, "1 hour 2 minutes 3 seconds")]
    [InlineData(-4, "0 seconds")]
    public void Words(double seconds, string expected) => Assert.Equal(expected, TimeFormat.Words(seconds));

    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(65.5, "1:05.5")]
    [InlineData(3723.04, "1:02:03.0")]
    [InlineData(59.96, "1:00.0")]
    public void Clock(double seconds, string expected) => Assert.Equal(expected, TimeFormat.Clock(seconds));

    [Theory]
    [InlineData("90", 90)]
    [InlineData("1:30", 90)]
    [InlineData("1:02:03.5", 3723.5)]
    [InlineData("2,5", 2.5)]
    [InlineData("1m 30s", 90)]
    [InlineData("2 minutes", 120)]
    [InlineData("1h", 3600)]
    [InlineData("+5", 15)]
    [InlineData("-2.5", 7.5)]
    [InlineData("-30", 0)]
    [InlineData(" 12.25 ", 12.25)]
    public void Parse_valid(string text, double expected)
    {
        Assert.True(TimeFormat.TryParse(text, 10, out var value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1:75")]
    [InlineData("1:2:3:4")]
    [InlineData("1.5:20")]
    [InlineData("+")]
    public void Parse_invalid(string text) => Assert.False(TimeFormat.TryParse(text, 10, out _));
}

public sealed class DescriberTests
{
    [Fact]
    public void Item_description_names_position_length_and_settings()
    {
        var a = Make.Clip("intro.mp4", 12.5);
        var b = Make.Clip("interview.mp4", 30).WithTransition("fade", 1);
        b.Volume = 0.5;
        b.Placement = new Placement { Position = PositionPreset.TopRight, SizePercent = 25 };
        var layer = Make.Layer(LayerKind.Video, a, b);
        var text = new Describer().Item(layer, 1);
        Assert.Equal(
            "2 of 2, interview.mp4, video, starts 11.5 seconds, length 30 seconds, Cross fade 1 second, volume 50 percent, top right, 25 percent size",
            text);
    }

    [Fact]
    public void Brief_and_detailed_differ()
    {
        var clip = Make.Clip("a.mp4", 3);
        clip.FadeIn = 1;
        clip.Audio = new AudioCleanup { NoiseReduction = NoiseReduction.Light, MidDb = -3 };
        var layer = Make.Layer(LayerKind.Video, clip);
        Assert.Equal("a.mp4, 0 seconds, 3 seconds long", new Describer(Verbosity.Brief).Item(layer, 0));
        var detailed = new Describer(Verbosity.Detailed).Item(layer, 0);
        Assert.Contains("fade in 1 second", detailed);
        Assert.Contains("noise reduction light", detailed);
        Assert.Contains("middle minus 3", detailed);
    }

    [Fact]
    public void Layer_description_includes_number_kind_count_and_mute()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video);
        var music = ProjectEditor.AddLayer(project, LayerKind.Audio, "Music");
        music.Items.Add(Make.Clip("song.mp3", 70));
        music.Muted = true;
        Assert.Equal("Layer 2, Audio, Music, 1 item, muted, ends 1 minute 10 seconds", new Describer().Layer(project, music));
        Assert.Equal("Layer 1, Video, empty", new Describer().Layer(project, project.Layers[0]));
    }

    [Fact]
    public void Whats_here_lists_every_layer()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(Make.Clip("main.mp4", 10));
        var logo = Make.Clip("logo.png", 5);
        logo.Placement = new Placement { Position = PositionPreset.BottomLeft, SizePercent = 10 };
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(logo);
        ProjectEditor.AddLayer(project, LayerKind.Audio);
        Assert.Equal(
            "At 4 seconds. layer 1: main.mp4, 6 seconds left. layer 2: logo.png, bottom left, 10 percent size, 1 second left. layer 3: nothing.",
            new Describer().WhatsHere(project, 4));
    }

    [Fact]
    public void Clock_style_is_used_when_chosen()
    {
        var layer = Make.Layer(LayerKind.Video, Make.Clip("a", 65));
        Assert.Contains("length 1:05.0", new Describer(Verbosity.Normal, TimeStyle.Clock).Item(layer, 0));
    }
}

public sealed class KeymapTests
{
    [Fact]
    public void Default_keymap_has_no_conflicts_or_bad_keys()
    {
        var keymap = Keymap.Default();
        Assert.Empty(keymap.Problems);
        foreach (var command in CommandCatalog.All)
        {
            Assert.NotEmpty(keymap.KeysFor(command.Id));
        }
    }

    [Fact]
    public void Every_command_has_help_text_and_a_catalog_entry()
    {
        foreach (var id in Enum.GetValues<CommandId>())
        {
            var info = CommandCatalog.Get(id);
            Assert.False(string.IsNullOrWhiteSpace(info.Title));
            Assert.EndsWith(".", info.Help);
        }

        Assert.Equal(Enum.GetValues<CommandId>().Length, CommandCatalog.All.Count);
    }

    [Fact]
    public void Default_keys_use_valid_windows_key_names()
    {
        var known = new HashSet<string>(
            Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
                .Concat(Enumerable.Range(0, 10).Select(d => $"D{d}"))
                .Concat(Enumerable.Range(1, 12).Select(f => $"F{f}"))
                .Concat(["Left", "Right", "Up", "Down", "Home", "End", "Delete", "Enter", "Escape", "Space",
                    "OemOpenBrackets", "OemCloseBrackets", "OemComma", "OemPeriod"]));
        foreach (var command in CommandCatalog.All)
        {
            foreach (var key in command.DefaultKeys)
            {
                Assert.True(KeyChord.TryParse(key, out var chord), key);
                Assert.Contains(chord.Key, known);
            }
        }
    }

    [Fact]
    public void The_users_requested_layout_is_in_place()
    {
        var keymap = Keymap.Default();
        CommandId Of(string key)
        {
            Assert.True(KeyChord.TryParse(key, out var chord));
            Assert.True(keymap.TryGetCommand(chord, out var id), key);
            return id;
        }

        Assert.Equal(CommandId.Rewind, Of("J"));
        Assert.Equal(CommandId.PlayPause, Of("K"));
        Assert.Equal(CommandId.Forward, Of("L"));
        Assert.Equal(CommandId.ToggleMute, Of("M"));
        Assert.Equal(CommandId.Layer1, Of("Alt+D1"));
        Assert.Equal(CommandId.Layer9, Of("Alt+D9"));
        Assert.Equal(CommandId.ListenAllLayers, Of("Alt+D0"));
        Assert.Equal(CommandId.SetRangeStart, Of("OemOpenBrackets"));
        Assert.Equal(CommandId.SetRangeEnd, Of("OemCloseBrackets"));
        Assert.Equal(CommandId.PreviousMarker, Of("Alt+Left"));
        Assert.Equal(CommandId.NextMarker, Of("Alt+Right"));
        Assert.Equal(CommandId.SlideEarlier, Of("Alt+J"));
        Assert.Equal(CommandId.SlideLater, Of("Alt+L"));
        Assert.Equal(CommandId.MoveItemLater, Of("Alt+Shift+Right"));
        Assert.Equal(CommandId.Delete, Of("Delete"));
    }

    [Fact]
    public void Overrides_replace_defaults_and_conflicts_are_reported()
    {
        var keymap = Keymap.Create(new Dictionary<string, string[]>
        {
            ["Split"] = ["Ctrl+K"],
            ["AddMarker"] = ["Ctrl+K"],
            ["Nonsense"] = ["X"],
            ["Undo"] = ["Ctrl++"],
        });
        Assert.True(KeyChord.TryParse("Ctrl+K", out var chord));
        Assert.True(keymap.TryGetCommand(chord, out var id));
        Assert.Equal(CommandId.Split, id);
        Assert.Equal(3, keymap.Problems.Count);
        Assert.Equal("Ctrl+K", keymap.GestureText(CommandId.Split));
    }

    [Fact]
    public void Keys_are_spoken_and_displayed_readably()
    {
        Assert.True(KeyChord.TryParse("Ctrl+Shift+OemOpenBrackets", out var chord));
        Assert.Equal("Control Shift left bracket", chord.Spoken());
        Assert.Equal("Ctrl+Shift+[", Keymap.Display(chord));
        Assert.True(KeyChord.TryParse("Alt+D3", out var alt3));
        Assert.Equal("Alt 3", alt3.Spoken());
        Assert.Equal("Alt+3", Keymap.Display(alt3));
    }

    [Fact]
    public void Keymap_round_trips_through_a_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ave-keys-{Guid.NewGuid():N}.json");
        try
        {
            Keymap.Default().Save(path);
            var loaded = Keymap.Load(path);
            Assert.Empty(loaded.Problems);
            Assert.Equal("Ctrl+E", loaded.GestureText(CommandId.Export));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public sealed class MediaProbeParseTests
{
    [Fact]
    public void Video_with_sound()
    {
        const string json = """
            {"streams":[{"codec_type":"video","width":1920,"height":1080,"nb_frames":"300"},{"codec_type":"audio"}],
             "format":{"duration":"10.5","format_name":"mov,mp4,m4a,3gp,3g2,mj2"}}
            """;
        var info = MediaProbe.Parse("a.mp4", json);
        Assert.Equal(ItemKind.Video, info.Kind);
        Assert.Equal(10.5, info.Duration);
        Assert.True(info.HasAudio);
        Assert.Equal(1920, info.Width);
    }

    [Fact]
    public void Png_is_a_still_picture()
    {
        const string json = """{"streams":[{"codec_type":"video","width":100,"height":50}],"format":{"format_name":"png_pipe","duration":"0.04"}}""";
        var info = MediaProbe.Parse("logo.png", json);
        Assert.Equal(ItemKind.Image, info.Kind);
        Assert.Equal(0, info.Duration);
    }

    [Fact]
    public void Mp3_with_cover_art_is_audio()
    {
        const string json = """
            {"streams":[{"codec_type":"audio","duration":"200.1"},{"codec_type":"video","width":500,"height":500,"disposition":{"attached_pic":1}}],
             "format":{"duration":"200.1","format_name":"mp3"}}
            """;
        var info = MediaProbe.Parse("song.mp3", json);
        Assert.Equal(ItemKind.Audio, info.Kind);
        Assert.False(info.HasVideo);
    }

    [Fact]
    public void Files_without_streams_are_rejected()
    {
        Assert.Throws<MediaProbeException>(() => MediaProbe.Parse("x.txt", """{"streams":[],"format":{}}"""));
    }

    [Fact]
    public void Items_take_their_layer_defaults()
    {
        var defaults = new LayerDefaults
        {
            ImageDuration = 4, Volume = 0.2,
            Transition = new TransitionSpec { Video = "fade", Duration = 1 },
            Placement = new Placement { Position = PositionPreset.TopRight },
        };
        var image = MediaProbe.CreateItem(new MediaInfo { Path = Path.Combine("x", "logo.png"), Kind = ItemKind.Image, HasVideo = true }, defaults, LayerKind.Video);
        Assert.Equal(4, image.Duration);
        Assert.Equal("logo.png", image.Name);
        Assert.Equal("fade", image.TransitionIn.Video);
        Assert.Equal(PositionPreset.TopRight, image.Placement.Position);
        image.Placement.Position = PositionPreset.Center;
        Assert.Equal(PositionPreset.TopRight, defaults.Placement.Position);

        var asAudio = MediaProbe.CreateItem(new MediaInfo { Path = "v.mp4", Kind = ItemKind.Video, HasVideo = true, HasAudio = true, Duration = 3 }, defaults, LayerKind.Audio);
        Assert.False(asAudio.HasVideo);
        Assert.Equal(0.2, asAudio.Volume);
    }
}

public sealed class RenderHelpersTests
{
    [Theory]
    [InlineData("out_time_us=5000000", 10, 0.5)]
    [InlineData("out_time_ms=10000000", 10, 1)]
    [InlineData("out_time_us=99000000", 10, 1)]
    public void Progress_lines_are_read(string line, double total, double expected)
    {
        Assert.True(RenderRunner.TryParseProgress(line, total, out var fraction));
        Assert.Equal(expected, fraction, 6);
    }

    [Theory]
    [InlineData("frame=10")]
    [InlineData("out_time_us=N/A")]
    public void Other_lines_are_ignored(string line) => Assert.False(RenderRunner.TryParseProgress(line, 10, out _));

    [Fact]
    public void Encoder_list_is_parsed()
    {
        const string text = """
            Encoders:
             V..... = Video
             ------
             V....D libx264              libx264 H.264 / AVC
             A....D aac                  AAC (Advanced Audio Coding)
             A....D libmp3lame           libmp3lame MP3
            """;
        var set = FfmpegTools.ParseEncoders(text);
        Assert.Contains("libx264", set);
        Assert.Contains("aac", set);
        Assert.Contains("libmp3lame", set);
    }

    [Theory]
    [InlineData(" T.C drawtext          V->V       Draw text on top of video frames using libfreetype library.")]
    [InlineData(" T.C.. drawtext        V->V       Draw text on top of video frames using libfreetype library.\r")]
    [InlineData("  TSC  drawtext  V->V  Draw text.")]
    public void Filter_lists_of_any_version_are_parsed(string line)
    {
        Assert.Contains("drawtext", FfmpegTools.ParseFilters("Filters:\n  T.. = Timeline support\n ------\n" + line + "\n"));
    }

    [Fact]
    public void Without_libx264_a_fallback_encoder_is_used()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(MediaProbe.CreateColorCard("Red", 1, new LayerDefaults()));
        var plan = new GraphBuilder(_ => false).Build(new RenderRequest
        {
            Project = project, Profile = OutputProfile.YouTube1080, OutputPath = "x.mp4",
        });
        Assert.Contains("mpeg4", plan.OutputArguments);
        Assert.Throws<RenderException>(() => new GraphBuilder(_ => false).Build(new RenderRequest
        {
            Project = project, Profile = OutputProfile.Mp3, OutputPath = "x.mp3",
        }));
    }

    [Fact]
    public void Empty_or_fully_muted_projects_explain_themselves()
    {
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(MediaProbe.CreateColorCard("Red", 1, new LayerDefaults()));
        layer.Muted = true;
        var error = Assert.Throws<RenderException>(() => new GraphBuilder(_ => true).Build(new RenderRequest
        {
            Project = project, Profile = OutputProfile.YouTube1080, OutputPath = "x.mp4",
        }));
        Assert.Contains("muted", error.Message);
    }

    [Fact]
    public void Windowing_cuts_items_before_the_start()
    {
        var clip = Make.Clip("a", 10, sourceDuration: 10);
        clip.SourceIn = 1;
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.AddRange([clip, Make.Clip("b", 5, gap: 2)]);
        var music = ProjectEditor.AddLayer(project, LayerKind.Audio);
        music.Items.Add(Make.Clip("m", 3));

        var cut = ProjectWindow.StartingAt(project, 4);
        Assert.Equal(6, cut.Layers[0].Items[0].Duration, 6);
        Assert.Equal(5, cut.Layers[0].Items[0].SourceIn, 6);
        Assert.Equal(new[] { 0.0, 8 }, Make.Starts(cut.Layers[0]));
        Assert.Empty(cut.Layers[1].Items);
        Assert.Equal(10, layer.Items[0].Duration);
    }
}

public sealed class TemplatesSettingsAndChecksTests
{
    [Fact]
    public void Every_template_creates_a_valid_project()
    {
        foreach (var template in ProjectTemplates.All)
        {
            var project = template.Create();
            Assert.NotEmpty(project.Layers);
            Assert.Contains(project.Layers, l => l.Kind == LayerKind.Video);
            Assert.Equal(project.Layers.Count, project.Layers.Select(l => l.Name).Distinct().Count());
            var roundTrip = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
            Assert.Equal(project.Layers.Count, roundTrip.Layers.Count);
        }

        Assert.Equal(1080, ProjectTemplates.Find("vertical")!.Create().Width);
    }

    [Fact]
    public void Settings_survive_a_round_trip_and_bad_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ave-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings { SlideStep = 2.5, EarconTheme = "Retro", Verbosity = Verbosity.Detailed };
            settings.AddRecent("a.avproj");
            settings.AddRecent("b.avproj");
            settings.AddRecent("a.avproj");
            settings.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal(2.5, loaded.SlideStep);
            Assert.Equal("Retro", loaded.EarconTheme);
            Assert.Equal(Verbosity.Detailed, loaded.Verbosity);
            Assert.Equal(new[] { "a.avproj", "b.avproj" }, loaded.RecentProjects);

            File.WriteAllText(path, "{ this is not json");
            Assert.Equal(1.0, AppSettings.Load(path).SlideStep);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Checker_finds_gaps_missing_files_and_dead_transitions()
    {
        var project = new Project();
        var main = ProjectEditor.AddLayer(project, LayerKind.Video);
        main.Items.Add(MediaProbe.CreateColorCard("Red", 2, new LayerDefaults()));
        var later = MediaProbe.CreateColorCard("Blue", 2, new LayerDefaults());
        later.GapBefore = 1;
        later.TransitionIn = new TransitionSpec { Video = "fade", Duration = 1 };
        main.Items.Add(later);
        var music = ProjectEditor.AddLayer(project, LayerKind.Audio);
        music.Items.Add(Make.Clip("missing.mp3", 8));
        music.Muted = true;

        var findings = ProjectChecker.Check(project);
        Assert.Contains(findings, f => f.Message.Contains("black picture for 1 second at 2 seconds"));
        Assert.Contains(findings, f => f.Message.Contains("transition into"));
        Assert.Contains(findings, f => f.Message.Contains("missing"));
        Assert.Contains(findings, f => f.Message.Contains("is muted"));
        Assert.Contains(findings, f => f.Message.Contains("The end will be black"));
    }

    [Fact]
    public void Checker_is_happy_with_a_clean_project()
    {
        var project = new Project();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(MediaProbe.CreateColorCard("Red", 2, new LayerDefaults()));
        var findings = ProjectChecker.Check(project);
        Assert.Single(findings);
        Assert.StartsWith("No problems found", findings[0].Message);
    }
}
