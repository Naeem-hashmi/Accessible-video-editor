using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

[Collection(MediaCollection.Name)]
public sealed class SoundTests(MediaFixture media)
{
    [Fact]
    public void Wav_header_is_correct()
    {
        var wav = WavWriter.ToWav(new float[] { 0, 0.5f, -0.5f, 1 }, stereo: true);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(44 + 4 * 2 * 2, wav.Length);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(2, BitConverter.ToInt16(wav, 22));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 44 + 3 * 4));
    }

    [Fact]
    public void Every_built_in_theme_has_every_sound_except_off()
    {
        foreach (var theme in EarconTheme.BuiltIn)
        {
            foreach (var e in Enum.GetValues<EarconEvent>())
            {
                var wav = theme.Get(e);
                if (theme.Name == "Off")
                {
                    Assert.Null(wav);
                }
                else
                {
                    Assert.NotNull(wav);
                    Assert.InRange(wav!.Length, 1000, 200_000);
                }
            }
        }

        Assert.Equal(EarconTheme.BuiltIn.Count, EarconTheme.BuiltIn.Select(t => t.Name).Distinct().Count());
    }

    [Fact]
    public void A_theme_exports_and_loads_back_as_a_user_theme()
    {
        var folder = Path.Combine(media.Folder, "theme", "My theme");
        EarconTheme.BuiltIn[1].Export(folder);
        File.Delete(Path.Combine(folder, "Error.wav"));
        var loaded = EarconTheme.FromFolder(folder);
        Assert.Equal("My theme", loaded.Name);
        Assert.NotNull(loaded.Get(EarconEvent.Navigate));
        Assert.Null(loaded.Get(EarconEvent.Error));
    }

    [Fact]
    public async Task Every_sound_effect_is_a_valid_file_ffmpeg_can_read_and_use()
    {
        var folder = Path.Combine(media.Folder, "effects");
        SoundEffects.WriteAll(folder);
        Assert.Equal(SoundEffects.BuiltIn.Count, Directory.GetFiles(folder, "*.wav").Length);
        Assert.Equal(SoundEffects.BuiltIn.Count, SoundEffects.BuiltIn.Select(e => e.FileName).Distinct().Count());

        var project = new Project { Width = 160, Height = 90 };
        var layer = ProjectEditor.AddLayer(project, LayerKind.Audio);
        foreach (var effect in SoundEffects.BuiltIn)
        {
            var path = Path.Combine(folder, effect.FileName);
            var info = await media.Probe(path);
            Assert.True(info.HasAudio, effect.Name);
            Assert.InRange(info.Duration, 0.02, 31);
            var peak = await media.PeakDb(path, 0, info.Duration);
            Assert.True(peak > -12 && peak <= 0, $"{effect.Name} peak {peak} dB");
            layer.Items.Add(await media.ItemFor(path, LayerKind.Audio));
        }

        var (output, _) = await media.Render(new RenderRequest
        {
            Project = project, Profile = OutputProfile.M4a, OutputPath = media.OutPath("all-effects.m4a"),
        });
        Assert.InRange((await media.Probe(output)).Duration, Timeline.LayerEnd(layer) - 0.2, Timeline.LayerEnd(layer) + 0.2);
    }

    [Fact]
    public void Existing_files_are_not_overwritten_unless_asked()
    {
        var folder = Path.Combine(media.Folder, "effects-keep");
        Directory.CreateDirectory(folder);
        var mine = Path.Combine(folder, SoundEffects.BuiltIn[0].FileName);
        File.WriteAllText(mine, "my file");
        SoundEffects.WriteAll(folder);
        Assert.Equal("my file", File.ReadAllText(mine));
        SoundEffects.WriteAll(folder, overwrite: true);
        Assert.NotEqual("my file", File.ReadAllText(mine));
    }
}
