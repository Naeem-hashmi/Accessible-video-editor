using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>Urdu captions draw with the bundled Naskh font, which has every Urdu letter.</summary>
[Collection(MediaCollection.Name)]
public sealed class UrduTextTests(MediaFixture media)
{
    [Fact]
    public void The_bundled_urdu_font_is_found_first()
    {
        var path = FontCatalog.Resolve("Noto Naskh Arabic", bold: true);
        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("Fonts", "NotoNaskhArabic-Bold.ttf"), path);
    }

    [Theory]
    [InlineData("NotoNaskhArabic-Regular.ttf")]
    [InlineData("NotoNaskhArabic-Bold.ttf")]
    public void The_font_has_every_urdu_letter(string file)
    {
        // Every letter of the Urdu alphabet, including the ones Arabic fonts often lack (ٹ ڈ ڑ ں ہ ھ ے).
        const string alphabet = "اآبپتٹثجچحخدڈذرڑزژسشصضطظعغفقکگلمنںوہھءیےۓ";
        var map = TrueTypeCharacters(Path.Combine(AppContext.BaseDirectory, "Fonts", file));
        var missing = alphabet.Where(c => !map.Contains(c)).ToList();
        Assert.True(missing.Count == 0, "Missing: " + string.Join(" ", missing.Select(c => $"U+{(int)c:X4}")));
    }

    /// <summary>The characters a TrueType font maps (its cmap table, formats 4 and 12).</summary>
    private static HashSet<int> TrueTypeCharacters(string path)
    {
        var data = File.ReadAllBytes(path);
        int U16(int o) => (data[o] << 8) | data[o + 1];
        long U32(int o) => ((long)data[o] << 24) | ((long)data[o + 1] << 16) | ((long)data[o + 2] << 8) | data[o + 3];
        var tables = U16(4);
        var cmap = -1;
        for (var i = 0; i < tables; i++)
        {
            var record = 12 + 16 * i;
            if (data[record] == 'c' && data[record + 1] == 'm' && data[record + 2] == 'a' && data[record + 3] == 'p')
            {
                cmap = (int)U32(record + 8);
            }
        }

        Assert.True(cmap >= 0, "No cmap table");
        var result = new HashSet<int>();
        for (var i = 0; i < U16(cmap + 2); i++)
        {
            var sub = cmap + (int)U32(cmap + 4 + 8 * i + 4);
            var format = U16(sub);
            if (format == 4)
            {
                var segments = U16(sub + 6) / 2;
                for (var s = 0; s < segments; s++)
                {
                    var end = U16(sub + 14 + 2 * s);
                    var start = U16(sub + 16 + 2 * segments + 2 * s);
                    var delta = (short)U16(sub + 16 + 4 * segments + 2 * s);
                    var rangeAt = sub + 16 + 6 * segments + 2 * s;
                    var range = U16(rangeAt);
                    for (var c = start; c <= end && c != 0xFFFF; c++)
                    {
                        var glyph = range == 0 ? (c + delta) & 0xFFFF : U16(rangeAt + range + 2 * (c - start)) is var g && g != 0 ? (g + delta) & 0xFFFF : 0;
                        if (glyph != 0)
                        {
                            result.Add(c);
                        }
                    }
                }
            }
            else if (format == 12)
            {
                var groups = (int)U32(sub + 12);
                for (var g = 0; g < groups; g++)
                {
                    var start = (int)U32(sub + 16 + 12 * g);
                    var end = (int)U32(sub + 20 + 12 * g);
                    for (var c = start; c <= end; c++)
                    {
                        result.Add(c);
                    }
                }
            }
        }

        return result;
    }

    [Fact]
    public async Task An_urdu_caption_is_drawn()
    {
        if (!media.Tools.Text.Available)
        {
            return;
        }

        var project = new Project { Width = 640, Height = 360, FrameRate = 25 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video, Captions.LayerName);
        var style = Captions.DefaultStyle("ur");
        style.Content = "آج ہم چاول بنانا سیکھیں گے";
        style.SizePercent = 10;
        layer.Items.Add(MediaProbe.CreateTextItem(style, 1, layer.Defaults, new Placement { Position = PositionPreset.Center }));
        var (output, plan) = await media.Render(new RenderRequest { Project = project, Profile = OutputProfile.ProjectSize, OutputPath = media.OutPath("urdu.mp4") });
        Assert.Contains("NotoNaskhArabic-Bold.ttf", string.Join(" ", plan.AuxiliaryFiles.Select(f => f.Value.CopyFrom)));

        // White letters in the middle of the frame.
        var white = 0;
        for (var x = 100; x < 540; x += 4)
        {
            var (r, g, b) = await media.PixelAt(output, 0.5, x, 180);
            if (r > 200 && g > 200 && b > 200)
            {
                white++;
            }
        }

        Assert.True(white > 5, $"Only {white} white pixels across the middle line");
        if (Environment.GetEnvironmentVariable("AVE_KEEP_FRAMES") is { } keep)
        {
            await media.Generate("urdu-frame.png", "-ss", "0.5", "-i", output, "-frames:v", "1");
            File.Copy(Path.Combine(media.Folder, "urdu-frame.png"), Path.Combine(keep, "urdu-frame.png"), overwrite: true);
        }
    }
}
