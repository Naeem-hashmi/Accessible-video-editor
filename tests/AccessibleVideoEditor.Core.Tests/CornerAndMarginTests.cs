using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

[Collection(MediaCollection.Name)]
public sealed class CornerAndMarginTests(MediaFixture media)
{
    [Fact]
    public async Task A_picture_gets_its_margin_and_rounded_corners_in_the_render()
    {
        var project = new Project { Width = 320, Height = 240, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        var picture = await media.ItemFor(media.RedPng, LayerKind.Video);
        picture.Duration = 1;
        picture.Placement = new Placement { Position = PositionPreset.TopLeft, SizePercent = 50, MarginPercent = 10, CornerRadiusPercent = 25 };
        layer.Items.Add(picture);

        var (output, _) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("rounded.mp4") });

        // 10 % of 240 is a 24 px margin; the square is 120 px with a 30 px corner radius.
        static bool Red((int R, int G, int B) p) => p.R > 180 && p.G < 80 && p.B < 80;
        static bool Black((int R, int G, int B) p) => p.R < 50 && p.G < 50 && p.B < 50;
        Assert.True(Black(await media.PixelAt(output, 0.5, 12, 84)), "margin left of the picture");
        Assert.True(Red(await media.PixelAt(output, 0.5, 84, 84)), "middle of the picture");
        Assert.True(Red(await media.PixelAt(output, 0.5, 84, 28)), "middle of the top edge");
        Assert.True(Black(await media.PixelAt(output, 0.5, 27, 27)), "the rounded-off corner");
        Assert.True(Black(await media.PixelAt(output, 0.5, 141, 141)), "the rounded-off bottom right corner");
    }

    [Fact]
    public void The_margin_moves_the_spoken_position_too()
    {
        var item = new Item { Kind = ItemKind.Image, HasVideo = true, SourceWidth = 200, SourceHeight = 200, Duration = 1,
            Placement = new Placement { Position = PositionPreset.BottomRight, SizePercent = 50, MarginPercent = 0 } };
        var rect = Geometry.RectOf(item, 320, 240);
        Assert.Equal(320, rect.X + rect.Width, 1);
        Assert.Equal(240, rect.Y + rect.Height, 1);
        item.Placement.MarginPercent = 10;
        rect = Geometry.RectOf(item, 320, 240);
        Assert.Equal(296, rect.X + rect.Width, 1);
    }
}
