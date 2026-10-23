using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

[Collection(MediaCollection.Name)]
public sealed class ThumbnailTests(MediaFixture media)
{
    [Fact]
    public void Thumbnails_fill_a_clip_and_show_the_part_of_the_file_it_plays()
    {
        var clip = Make.Clip("a.mp4", 10, sourceDuration: 100);
        clip.SourceIn = 20;
        // 10 s at 40 px per second is 400 px: seven 64 px slots, the last one cut off.
        var slots = Thumbnails.Layout(clip, 100, 500, 40, 64);
        Assert.Equal(7, slots.Count);
        Assert.Equal(100, slots[0].X);
        Assert.All(slots, s => Assert.InRange(s.SourceTime, 20, 31));
        Assert.True(slots.Zip(slots.Skip(1)).All(p => p.Second.SourceTime >= p.First.SourceTime));
        // Only what is on screen is made.
        Assert.Equal(2, Thumbnails.Layout(clip, 100, 500, 40, 64, 150, 200).Count);
        // At double speed the same width covers twice as much of the file.
        clip.Speed = 2;
        Assert.InRange(Thumbnails.Layout(clip, 100, 500, 40, 64)[^1].SourceTime, 38, 41);
        // Sound has no pictures; a picture is shown once.
        Assert.Empty(Thumbnails.Layout(new Item { Kind = ItemKind.Audio, HasAudio = true, Duration = 5 }, 0, 200, 40, 64));
        Assert.Single(Thumbnails.Layout(new Item { Kind = ItemKind.Image, HasVideo = true, Duration = 5 }, 0, 200, 40, 64));
    }

    [Fact]
    public void A_frame_comes_back_at_the_asked_size()
    {
        var pixels = FfmpegStream.Frame(media.Tools, media.VideoA, 1, 64, 36);
        Assert.NotNull(pixels);
        Assert.Equal(64 * 36 * 4, pixels!.Length);
        Assert.Contains(pixels, b => b > 30); // A real picture, not all black.
        Assert.Null(FfmpegStream.Frame(media.Tools, Path.Combine(media.Folder, "missing.mp4"), 0, 64, 36));
    }
}
