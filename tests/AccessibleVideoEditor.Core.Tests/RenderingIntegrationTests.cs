using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Renders real projects with FFmpeg and checks the results: length, streams, pixels and sound.</summary>
[Collection(MediaCollection.Name)]
public sealed class RenderingIntegrationTests(MediaFixture media)
{
    private static Project SmallProject() => new() { Width = 320, Height = 180, FrameRate = 25 };

    private static void AssertDuration(double expected, double actual, double tolerance = 0.12) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Fact]
    public async Task Two_different_videos_play_one_after_another()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(await media.ItemFor(media.VideoA, LayerKind.Video));
        layer.Items.Add(await media.ItemFor(media.VideoB, LayerKind.Video));

        var (path, plan) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("sequence.mp4"),
        });

        var info = await media.Probe(path);
        AssertDuration(6, info.Duration);
        AssertDuration(6, plan.Duration, 0.05);
        Assert.True(info.HasVideo && info.HasAudio);
        Assert.Equal(320, info.Width);
        Assert.Equal(180, info.Height);
        Assert.True(await media.PeakDb(path, 1, 2) > -30, "First video's sound should be heard.");
        Assert.True(await media.PeakDb(path, 4, 5) > -30, "Second video's sound should be heard.");
    }

    [Fact]
    public async Task Overlay_picture_appears_top_right_only_during_its_time()
    {
        var project = SmallProject();
        var main = ProjectEditor.AddLayer(project, LayerKind.Video);
        main.Items.Add(MediaColor("Black", 4));
        var overlay = ProjectEditor.AddLayer(project, LayerKind.Video);
        var logo = await media.ItemFor(media.RedPng, LayerKind.Video);
        logo.Duration = 2;
        logo.Placement = new Placement { Position = PositionPreset.TopRight, SizePercent = 25 };
        ProjectEditor.AppendItem(overlay, logo, gapBefore: 1);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("overlay.mp4"),
        });

        AssertDuration(4, (await media.Probe(path)).Duration);
        var during = await media.PixelAt(path, 2.0, 300, 20);
        Assert.True(during.R > 180 && during.G < 80, $"Expected red top right at 2 s, got {during}.");
        var bottomLeft = await media.PixelAt(path, 2.0, 20, 160);
        Assert.True(bottomLeft.R < 60, $"Bottom left should stay black, got {bottomLeft}.");
        var before = await media.PixelAt(path, 0.5, 300, 20);
        Assert.True(before.R < 60, $"Overlay must not show before its gap ends, got {before}.");
        var after = await media.PixelAt(path, 3.5, 300, 20);
        Assert.True(after.R < 60, $"Overlay must disappear after it ends, got {after}.");
    }

    [Theory]
    [InlineData(PositionPreset.TopLeft, 20, 20)]
    [InlineData(PositionPreset.TopCenter, 160, 20)]
    [InlineData(PositionPreset.MiddleLeft, 20, 90)]
    [InlineData(PositionPreset.Center, 160, 90)]
    [InlineData(PositionPreset.MiddleRight, 300, 90)]
    [InlineData(PositionPreset.BottomLeft, 20, 160)]
    [InlineData(PositionPreset.BottomCenter, 160, 160)]
    [InlineData(PositionPreset.BottomRight, 300, 160)]
    [InlineData(PositionPreset.FullFrame, 160, 90)]
    public async Task Every_position_preset_places_the_picture_where_its_name_says(PositionPreset position, int x, int y)
    {
        var project = SmallProject();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(MediaColor("Black", 1));
        var overlay = ProjectEditor.AddLayer(project, LayerKind.Video);
        var logo = await media.ItemFor(media.RedPng, LayerKind.Video);
        logo.Duration = 1;
        logo.Placement = new Placement { Position = position, SizePercent = 30 };
        overlay.Items.Add(logo);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath($"pos-{position}.mp4"),
        });

        var pixel = await media.PixelAt(path, 0.5, x, y);
        Assert.True(pixel.R > 180 && pixel.G < 80, $"{position}: expected red at {x},{y}, got {pixel}.");
    }

    [Fact]
    public async Task Custom_position_and_opacity_are_applied()
    {
        var project = SmallProject();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(MediaColor("White", 1));
        var overlay = ProjectEditor.AddLayer(project, LayerKind.Video);
        var logo = await media.ItemFor(media.RedPng, LayerKind.Video);
        logo.Duration = 1;
        logo.Placement = new Placement
        {
            Position = PositionPreset.Custom, SizePercent = 20, CustomXPercent = 50, CustomYPercent = 50, OpacityPercent = 50,
        };
        overlay.Items.Add(logo);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("custom.mp4"),
        });

        // Half-transparent red over white is pink: red high, green and blue around the middle.
        var pixel = await media.PixelAt(path, 0.5, 180, 100);
        Assert.True(pixel.R > 200 && pixel.G is > 90 and < 190, $"Expected pink, got {pixel}.");
        var outside = await media.PixelAt(path, 0.5, 100, 40);
        Assert.True(outside.G > 220, $"Outside the picture should stay white, got {outside}.");
    }

    [Fact]
    public async Task Every_video_transition_renders_with_the_right_length()
    {
        var project = new Project { Width = 160, Height = 90, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(MediaColor("White", 1));
        foreach (var entry in TransitionCatalog.Video.Where(e => e.Value != TransitionSpec.None))
        {
            var item = MediaColor(layer.Items.Count % 2 == 0 ? "Red" : "Blue", 1);
            item.TransitionIn = new TransitionSpec { Video = entry.Value, Duration = 0.4 };
            layer.Items.Add(item);
        }

        var expected = Timeline.LayerEnd(layer);
        var (path, plan) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("all-transitions.mp4"),
        });

        AssertDuration(expected, (await media.Probe(path)).Duration);
        foreach (var entry in TransitionCatalog.Video.Where(e => e.Value != TransitionSpec.None))
        {
            Assert.Contains($"transition={entry.Value}:", plan.FilterGraph);
        }
    }

    [Fact]
    public async Task Cross_fade_blends_the_two_pictures_in_the_middle()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(MediaColor("Black", 2));
        var white = MediaColor("White", 2);
        white.TransitionIn = new TransitionSpec { Video = "fade", Duration = 1 };
        layer.Items.Add(white);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("crossfade.mp4"),
        });

        AssertDuration(3, (await media.Probe(path)).Duration);
        var middle = await media.PixelAt(path, 1.5, 160, 90);
        Assert.InRange(middle.R, 60, 200);
        var end = await media.PixelAt(path, 2.6, 160, 90);
        Assert.True(end.R > 230, $"Should be white after the transition, got {end}.");
    }

    [Fact]
    public async Task Every_audio_crossfade_curve_renders_on_an_audio_layer()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        foreach (var entry in TransitionCatalog.Audio.Where(e => e.Value != TransitionSpec.None))
        {
            var item = await media.ItemFor(media.Music, LayerKind.Audio);
            item.Duration = 0.8;
            item.TransitionIn = new TransitionSpec { Audio = entry.Value, Duration = 0.3 };
            layer.Items.Add(item);
        }

        var expected = Timeline.LayerEnd(layer);
        var (path, plan) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.M4a, OutputPath = media.OutPath("curves.m4a"),
        });

        var info = await media.Probe(path);
        Assert.False(info.HasVideo);
        AssertDuration(expected, info.Duration, 0.15);
        Assert.Contains("c1=qsin", plan.FilterGraph);
    }

    [Fact]
    public async Task Every_sound_cleanup_option_renders()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        var settings = new List<AudioCleanup>();
        foreach (var nr in Enum.GetValues<NoiseReduction>())
        {
            settings.Add(new AudioCleanup { NoiseReduction = nr });
        }

        foreach (var hum in Enum.GetValues<HumFilter>())
        {
            settings.Add(new AudioCleanup { Hum = hum, RemoveRumble = true });
        }

        foreach (var voice in Enum.GetValues<VoicePreset>())
        {
            settings.Add(new AudioCleanup { Voice = voice });
        }

        settings.Add(new AudioCleanup { LowDb = 12, MidDb = -12, HighDb = 6 });
        settings.Add(new AudioCleanup { LowDb = -12, MidDb = 12, HighDb = -12, Normalize = true });

        foreach (var cleanup in settings)
        {
            var item = await media.ItemFor(media.Music, LayerKind.Audio);
            item.Duration = 0.5;
            item.Audio = cleanup;
            layer.Items.Add(item);
        }

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.Wav, OutputPath = media.OutPath("cleanup.wav"),
        });

        AssertDuration(Timeline.LayerEnd(layer), (await media.Probe(path)).Duration, 0.15);
    }

    [Fact]
    public async Task Muted_layers_are_left_out_but_a_single_layer_can_still_be_heard()
    {
        var project = SmallProject();
        var video = ProjectEditor.AddLayer(project, LayerKind.Video);
        video.Items.Add(await media.ItemFor(media.Silent, LayerKind.Video));
        var music = ProjectEditor.AddLayer(project, LayerKind.Audio);
        var song = await media.ItemFor(media.Music, LayerKind.Audio);
        song.Duration = 2;
        music.Items.Add(song);
        music.Muted = true;

        var (all, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("muted.mp4"),
        });
        Assert.True(await media.PeakDb(all, 0.2, 1.8) < -60, "A muted layer must be silent in the mix.");

        var (solo, _) = await media.Render(new RenderRequest
        {
            Project = project, Scope = RenderScope.Layer(music.Id), Profile = OutputProfile.PreviewAudio,
            OutputPath = media.OutPath("solo.m4a"),
        });
        Assert.True(await media.PeakDb(solo, 0.2, 1.8) > -30, "Listening to one layer plays it even when muted.");
    }

    [Fact]
    public async Task Volume_and_item_mute_change_the_sound()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        var loud = await media.ItemFor(media.Voice, LayerKind.Audio);
        loud.Duration = 1;
        var quiet = await media.ItemFor(media.Voice, LayerKind.Audio);
        quiet.Duration = 1;
        quiet.Volume = 0.1;
        var muted = await media.ItemFor(media.Voice, LayerKind.Audio);
        muted.Duration = 1;
        muted.AudioMuted = true;
        layer.Items.AddRange([loud, quiet, muted]);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.Wav, OutputPath = media.OutPath("volume.wav"),
        });

        var loudDb = await media.PeakDb(path, 0.2, 0.8);
        var quietDb = await media.PeakDb(path, 1.2, 1.8);
        var mutedDb = await media.PeakDb(path, 2.2, 2.8);
        Assert.InRange(loudDb - quietDb, 17, 23);
        Assert.True(mutedDb < -60);
    }

    [Fact]
    public async Task Fades_start_and_end_quietly_and_dark()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var item = await media.ItemFor(media.VideoA, LayerKind.Video);
        item.FadeIn = 1;
        item.FadeOut = 1;
        layer.Items.Add(item);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("fades.mp4"),
        });

        var first = await media.PixelAt(path, 0, 160, 90);
        Assert.True(first.R + first.G + first.B < 60, $"First frame should be nearly black, got {first}.");
        Assert.True(await media.PeakDb(path, 0, 0.1) < await media.PeakDb(path, 1.2, 1.8) - 10);
    }

    [Fact]
    public async Task Preview_from_the_middle_starts_at_that_time()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(MediaColor("Red", 2));
        layer.Items.Add(MediaColor("Blue", 2));

        var (path, plan) = await media.Render(new RenderRequest
        {
            Project = project, Start = 2.5, Profile = OutputProfile.PreviewVideo, OutputPath = media.OutPath("window.mp4"),
            CanvasOverride = (160, 90),
        });

        AssertDuration(1.5, plan.Duration, 0.05);
        var pixel = await media.PixelAt(path, 0.2, 80, 45);
        Assert.True(pixel.B > 150 && pixel.R < 80, $"Should start inside the blue card, got {pixel}.");
    }

    [Fact]
    public async Task Range_end_limits_the_length()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(await media.ItemFor(media.VideoA, LayerKind.Video));
        layer.Items.Add(await media.ItemFor(media.VideoB, LayerKind.Video));

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Start = 1, End = 4, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("range.mp4"),
        });

        AssertDuration(3, (await media.Probe(path)).Duration);
    }

    [Theory]
    [InlineData("vertical-1080x1920", 1080, 1920)]
    [InlineData("square-1080", 1080, 1080)]
    [InlineData("youtube-720p", 1280, 720)]
    public async Task Export_presets_produce_their_size(string profileId, int width, int height)
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var item = await media.ItemFor(media.BlueJpg, LayerKind.Video);
        item.Duration = 0.5;
        layer.Items.Add(item);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.Find(profileId)!, OutputPath = media.OutPath(profileId + ".mp4"),
        });

        var info = await media.Probe(path);
        Assert.Equal(width, info.Width);
        Assert.Equal(height, info.Height);
    }

    [Fact]
    public async Task Audio_only_exports_work()
    {
        var project = SmallProject();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(await media.ItemFor(media.VideoA, LayerKind.Video));
        foreach (var profile in new[] { OutputProfile.M4a, OutputProfile.Wav, OutputProfile.Mp3 })
        {
            if (profile == OutputProfile.Mp3 && !media.Tools.HasEncoder("libmp3lame"))
            {
                continue;
            }

            var (path, _) = await media.Render(new RenderRequest
            {
                Project = project, Profile = profile, OutputPath = media.OutPath("audio" + profile.Extension),
            });
            var info = await media.Probe(path);
            Assert.False(info.HasVideo);
            AssertDuration(3, info.Duration, 0.15);
        }
    }

    [Fact]
    public async Task A_video_file_on_an_audio_layer_gives_only_its_sound()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        layer.Items.Add(await media.ItemFor(media.VideoA, LayerKind.Audio));

        Assert.False(GraphBuilder.ScopeHasVideo(project, RenderScope.AllLayers));
        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.PreviewAudio, OutputPath = media.OutPath("video-as-audio.m4a"),
        });
        Assert.True(await media.PeakDb(path, 0.5, 2.5) > -30);
    }

    [Fact]
    public async Task Trimmed_and_split_items_render_with_the_edited_length()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(await media.ItemFor(media.VideoA, LayerKind.Video));
        Assert.True(ProjectEditor.SplitItem(layer, 0, 1.5).Ok);
        Assert.True(ProjectEditor.TrimStart(layer, 1, 2, ripple: true).Ok);
        Assert.True(ProjectEditor.DeleteItem(layer, 0, ripple: true).Ok);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("edited.mp4"),
        });

        AssertDuration(Timeline.LayerEnd(layer), (await media.Probe(path)).Duration);
        AssertDuration(1, Timeline.LayerEnd(layer), 0.01);
    }

    [Fact]
    public async Task Rendering_reports_progress_and_can_be_cancelled()
    {
        var project = new Project { Width = 1280, Height = 720, FrameRate = 30 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        for (var i = 0; i < 20; i++)
        {
            layer.Items.Add(await media.ItemFor(media.VideoB, LayerKind.Video));
        }

        var output = media.OutPath("cancel.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var plan = new GraphBuilder(media.Tools.HasEncoder).Build(new RenderRequest
        {
            Project = project, Profile = OutputProfile.Uhd4K, OutputPath = output,
        });
        using var cts = new CancellationTokenSource();
        var reports = 0;
        var progress = new Progress<double>(_ =>
        {
            if (Interlocked.Increment(ref reports) >= 1)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            RenderRunner.RunAsync(media.Tools, plan, output, progress, cts.Token));
        Assert.False(File.Exists(output), "A cancelled render must not leave a partial file.");
    }

    [Fact]
    public async Task Missing_media_gives_a_clear_message()
    {
        var project = SmallProject();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var item = await media.ItemFor(media.VideoA, LayerKind.Video);
        item.Path = Path.Combine(media.Folder, "gone.mp4");
        layer.Items.Add(item);

        var error = Assert.Throws<RenderException>(() => new GraphBuilder(media.Tools.HasEncoder).Build(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("never.mp4"),
        }));
        Assert.Contains("gone.mp4", error.Message);
        await Task.CompletedTask;
    }

    private static Item MediaColor(string color, double seconds) =>
        Media.MediaProbe.CreateColorCard(color, seconds, new LayerDefaults());
}
