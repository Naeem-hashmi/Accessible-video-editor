using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.Core.Tests;

[Collection(MediaCollection.Name)]
public sealed class TextRenderingTests(MediaFixture media)
{
    private static Project Canvas() => new() { Width = 320, Height = 180, FrameRate = 25 };

    private static Item TextItem(string words, TextAnimation animIn, TextAnimation animOut, double seconds = 3)
    {
        var style = new TextStyle
        {
            Content = words, SizePercent = 20, ColorName = "White", Background = TextBackground.LightBox,
            Outline = false, AnimationIn = animIn, AnimationOut = animOut, AnimationDuration = 0.8,
        };
        return MediaProbe.CreateTextItem(style, seconds, new LayerDefaults());
    }

    private async Task<string> RenderText(Item text, string name)
    {
        var project = Canvas();
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(MediaProbe.CreateColorCard("Black", text.Duration, new LayerDefaults()));
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(text);
        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath(name),
        });
        return path;
    }

    private bool CanDrawText => media.Tools.Text.Available;

    [Fact]
    public void Text_drawing_is_available_where_required()
    {
        // CI sets AVE_REQUIRE_TEXT so a build without drawtext fails instead of skipping the text tests.
        if (Environment.GetEnvironmentVariable("AVE_REQUIRE_TEXT") == "1")
        {
            Assert.True(CanDrawText, "This FFmpeg has no drawtext filter, so text can't be rendered.");
        }
    }

    [Fact]
    public async Task Text_fades_in_stays_and_fades_out()
    {
        if (!CanDrawText)
        {
            return;
        }

        var path = await RenderText(TextItem("HELLO", TextAnimation.Fade, TextAnimation.Fade), "text-fade.mp4");
        var start = await media.PixelAt(path, 0, 160, 90);
        var middle = await media.PixelAt(path, 1.5, 160, 90);
        var end = await media.PixelAt(path, 2.92, 160, 90);
        Assert.True(start.R < 50, $"Faded in from nothing, got {start}.");
        Assert.True(middle.R > 180, $"Light box visible in the middle of the text, got {middle}.");
        Assert.True(end.R < 120, $"Faded out at the end, got {end}.");
    }

    [Theory]
    [InlineData(TextAnimation.SlideFromLeft)]
    [InlineData(TextAnimation.SlideFromRight)]
    [InlineData(TextAnimation.SlideFromTop)]
    [InlineData(TextAnimation.SlideFromBottom)]
    [InlineData(TextAnimation.Rise)]
    [InlineData(TextAnimation.None)]
    [InlineData(TextAnimation.Typewriter)]
    public async Task Every_text_animation_renders_and_ends_up_in_place(TextAnimation animation)
    {
        if (!CanDrawText)
        {
            return;
        }

        var path = await RenderText(TextItem("MOVING TEXT", animation, animation), $"text-{animation}.mp4");
        Assert.InRange((await media.Probe(path)).Duration, 2.9, 3.1);
        var resting = await media.PixelAt(path, 1.5, 160, 90);
        Assert.True(resting.R > 180, $"{animation}: text should be at rest in the centre at 1.5 s, got {resting}.");
        if (animation is TextAnimation.SlideFromLeft or TextAnimation.SlideFromRight or TextAnimation.Typewriter)
        {
            var early = await media.PixelAt(path, 0.04, 160, 90);
            Assert.True(early.R < 120, $"{animation}: should not be in the centre yet at the start, got {early}.");
        }
    }

    [Fact]
    public async Task Text_with_symbols_quotes_colons_and_two_lines_renders()
    {
        if (!CanDrawText)
        {
            return;
        }

        var item = TextItem("It's 100% \"done\": yes; [ok] \\ 'quotes'\nSecond line: ünïcödé", TextAnimation.Fade, TextAnimation.None);
        var path = await RenderText(item, "text-symbols.mp4");
        Assert.InRange((await media.Probe(path)).Duration, 2.9, 3.1);
    }

    [Fact]
    public async Task Text_can_be_placed_in_a_corner()
    {
        if (!CanDrawText)
        {
            return;
        }

        var item = TextItem("Hi", TextAnimation.None, TextAnimation.None);
        item.Placement = new Placement { Position = PositionPreset.BottomRight };
        var path = await RenderText(item, "text-corner.mp4");
        var rect = Geometry.RectOf(item, 320, 180);
        var inside = await media.PixelAt(path, 1, (int)(rect.Right - 4), (int)(rect.Bottom - 4));
        var center = await media.PixelAt(path, 1, 160, 90);
        Assert.True(inside.R > 180, $"Bottom right corner should have the light box, got {inside}.");
        Assert.True(center.R < 60, $"Centre should be empty, got {center}.");
    }
}

[Collection(MediaCollection.Name)]
public sealed class CropAndFillTests(MediaFixture media)
{
    [Fact]
    public async Task Crop_removes_the_chosen_edge()
    {
        // VideoB is colour bars: white on the left, blue-ish on the right. Cropping 80 percent from the
        // left leaves only right-hand colours.
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var full = await media.ItemFor(media.VideoB, LayerKind.Video);
        full.Duration = 1;
        layer.Items.Add(full);
        var cropped = full.Clone();
        cropped.Placement = new Placement { CropLeft = 80 };
        layer.Items.Add(cropped);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("crop.mp4"),
        });
        var before = await media.PixelAt(path, 0.5, 160, 40);
        var after = await media.PixelAt(path, 1.5, 160, 40);
        Assert.NotEqual(before, after);
        Assert.True(Math.Abs(after.R - before.R) + Math.Abs(after.G - before.G) + Math.Abs(after.B - before.B) > 60,
            $"Cropping should show a different part of the picture: {before} then {after}.");
    }

    [Fact]
    public async Task Fill_covers_the_frame_without_black_bars()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var photo = await media.ItemFor(media.BlueJpg, LayerKind.Video);
        photo.Duration = 1;
        layer.Items.Add(photo);
        var filled = photo.Clone();
        filled.Placement = new Placement { Fill = true };
        layer.Items.Add(filled);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("fill.mp4"),
        });
        var fitEdge = await media.PixelAt(path, 0.5, 5, 90);
        var fillEdge = await media.PixelAt(path, 1.5, 5, 90);
        Assert.True(fitEdge.B < 60, $"A 4:3 photo fitted into 16:9 leaves black bars at the sides, got {fitEdge}.");
        Assert.True(fillEdge.B > 150, $"Fill reaches the side edge, got {fillEdge}.");
        Assert.Equal("full frame, filling the frame with the edges cut off", FrameDescriber.Placement(filled, 320, 180));
    }

    [Fact]
    public async Task Spoken_geometry_matches_the_rendered_picture()
    {
        var project = new Project { Width = 320, Height = 180, FrameRate = 25 };
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(MediaProbe.CreateColorCard("Black", 1, new LayerDefaults()));
        var logo = await media.ItemFor(media.RedPng, LayerKind.Video);
        logo.Duration = 1;
        logo.Placement = new Placement { Position = PositionPreset.Custom, SizePercent = 30, CustomXPercent = 60, CustomYPercent = 20 };
        ProjectEditor.AddLayer(project, LayerKind.Video).Items.Add(logo);

        var (path, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("geometry.mp4"),
        });
        var rect = Geometry.RectOf(logo, 320, 180);
        Assert.Equal(54, rect.Width, 0);
        Assert.Equal(54, rect.Height, 0);
        var inside = await media.PixelAt(path, 0.5, (int)(rect.X + 3), (int)(rect.Y + 3));
        var outsideLeft = await media.PixelAt(path, 0.5, (int)(rect.X - 4), (int)(rect.Y + 10));
        var outsideBelow = await media.PixelAt(path, 0.5, (int)(rect.X + 10), (int)(rect.Bottom + 4));
        Assert.True(inside.R > 180, $"Inside the spoken rectangle should be red, got {inside}.");
        Assert.True(outsideLeft.R < 60 && outsideBelow.R < 60, $"Outside should be black, got {outsideLeft} and {outsideBelow}.");
    }
}

public sealed class FrameDescriberTests
{
    [Fact]
    public void Describes_layers_positions_sizes_and_overlaps()
    {
        var project = new Project { Width = 1920, Height = 1080 };
        var main = ProjectEditor.AddLayer(project, LayerKind.Video);
        main.Items.Add(Make.Clip("interview.mp4", 10));
        main.Items[0].SourceWidth = 1920;
        main.Items[0].SourceHeight = 1080;
        var logoLayer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var logo = Make.Clip("logo.png", 10);
        logo.Kind = ItemKind.Image;
        logo.SourceWidth = 500;
        logo.SourceHeight = 500;
        logo.Placement = new Placement { Position = PositionPreset.TopRight, SizePercent = 20 };
        logoLayer.Items.Add(logo);
        var textLayer = ProjectEditor.AddLayer(project, LayerKind.Video);
        textLayer.Items.Add(MediaProbe.CreateTextItem(new TextStyle { Content = "Welcome to my channel" }, 10, new LayerDefaults(),
            new Placement { Position = PositionPreset.BottomCenter }));

        var text = FrameDescriber.Describe(project, 2);
        Assert.StartsWith("At 2 seconds. Layer 1, interview.mp4, full frame.", text);
        Assert.Contains("Layer 2, logo.png, top right, 11 percent wide and 20 percent high, over interview.mp4", text);
        Assert.Contains("over interview.mp4", text);
        Assert.Contains("Layer 3, text \"Welcome to my channel\", bottom centre", text);

        var edge = Make.Clip("edge.png", 10);
        edge.SourceWidth = 100;
        edge.SourceHeight = 100;
        edge.Placement = new Placement { Position = PositionPreset.Custom, SizePercent = 20, CustomXPercent = 100, CustomYPercent = 0 };
        Assert.Contains("touching the top and right edges", FrameDescriber.Placement(edge, 1920, 1080));
    }

    [Fact]
    public void Black_frames_and_partial_cover_are_reported()
    {
        var project = new Project { Width = 1000, Height = 1000 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var small = Make.Clip("small.png", 5);
        small.Kind = ItemKind.Image;
        small.SourceWidth = 100;
        small.SourceHeight = 100;
        small.Placement = new Placement { Position = PositionPreset.Center, SizePercent = 40 };
        layer.Items.Add(small);
        var upper = ProjectEditor.AddLayer(project, LayerKind.Video);
        var other = small.Clone();
        other.Name = "other.png";
        other.Placement = new Placement { Position = PositionPreset.Custom, SizePercent = 40, CustomXPercent = 50, CustomYPercent = 50 };
        upper.Items.Add(other);

        var text = FrameDescriber.Describe(project, 1);
        Assert.Contains("Parts of the frame are black", text);
        Assert.Contains("covering part of small.png", text);
        Assert.Equal("At 10 seconds the picture is black.", FrameDescriber.Describe(project, 10));
    }

    [Fact]
    public void Crop_and_opacity_are_mentioned()
    {
        var item = Make.Clip("v.mp4", 3);
        item.SourceWidth = 1920;
        item.SourceHeight = 1080;
        item.Placement = new Placement { Position = PositionPreset.Center, SizePercent = 50, OpacityPercent = 70, CropLeft = 10 };
        var text = FrameDescriber.Placement(item, 1920, 1080);
        Assert.Contains("70 percent opacity", text);
        Assert.Contains("cropped 10 percent off the left", text);
    }
}
