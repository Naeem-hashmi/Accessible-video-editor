using System.Diagnostics;
using System.Globalization;
using AccessibleVideoEditor.Core.Sound;

namespace AccessibleVideoEditor.Core.Tests;

/// <summary>The real recordings shipped in assets/sounds/recordings, and the list that describes them.</summary>
public sealed class RecordedSoundTests
{
    private static string Folder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AccessibleVideoEditor.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "assets", "sounds", "recordings");
    }

    [Fact]
    public void Every_listed_recording_exists_and_every_file_is_listed()
    {
        var folder = Folder();
        var listed = RecordedSounds.Parse(File.ReadAllText(Path.Combine(folder, RecordedSounds.ManifestName)));
        var files = Directory.EnumerateFiles(folder, "*.mp3", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(files, listed.Select(s => s.File).OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(listed.Count, RecordedSounds.Load(folder).Count);
    }

    [Fact]
    public void Recordings_are_public_domain_and_credited()
    {
        var listed = RecordedSounds.Load(Folder());
        Assert.True(listed.Count >= 20, $"Only {listed.Count} recordings");
        Assert.All(listed, s =>
        {
            Assert.Contains(s.License, new[] { "CC0", "Public domain" });
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Description));
            Assert.False(string.IsNullOrWhiteSpace(s.Creator));
            Assert.True(s.Source.StartsWith("https://commons.wikimedia.org/wiki/File:", StringComparison.Ordinal) ||
                        s.Source.StartsWith("https://freesound.org/", StringComparison.Ordinal), s.Source);
        });
        Assert.Equal(listed.Count, listed.Select(s => s.Name + s.Category).Distinct().Count());
        var credits = File.ReadAllText(Path.Combine(Folder(), "CREDITS.md"));
        Assert.All(listed, s => Assert.Contains(s.Source, credits));
    }

    [Fact]
    public void The_trending_categories_are_covered()
    {
        var categories = RecordedSounds.Load(Folder()).Select(s => s.Category).ToHashSet();
        foreach (var expected in new[] { "Typing", "Clicks", "Notifications", "Reactions", "Everyday", "Nature" })
        {
            Assert.Contains(expected, categories);
        }
    }

    [Fact]
    public async Task Each_recording_is_real_audio_of_the_listed_length()
    {
        var folder = Folder();
        var tools = await AccessibleVideoEditor.Core.Rendering.FfmpegTools.LocateAsync();
        foreach (var sound in RecordedSounds.Load(folder))
        {
            var psi = new ProcessStartInfo(tools.FfprobePath)
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            foreach (var a in new[] { "-v", "error", "-select_streams", "a:0", "-show_entries", "format=duration", "-of", "csv=p=0", RecordedSounds.FullPath(folder, sound) })
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            Assert.True(double.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds), $"{sound.File}: {output}");
            Assert.InRange(seconds, 0.2, 12.5);
            Assert.InRange(Math.Abs(seconds - sound.Seconds), 0, 0.15);
        }
    }

    [Fact]
    public void Missing_files_are_left_out_and_a_missing_list_means_none()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ave-rec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Typing"));
        try
        {
            Assert.Empty(RecordedSounds.Load(folder));
            File.WriteAllText(Path.Combine(folder, "Typing", "a.mp3"), "x");
            File.WriteAllText(Path.Combine(folder, RecordedSounds.ManifestName),
                """[{"file":"Typing/a.mp3","name":"A","category":"Typing","license":"CC0","seconds":1.5},{"file":"Typing/gone.mp3","name":"B"}]""");
            var sounds = RecordedSounds.Load(folder);
            Assert.Single(sounds);
            Assert.Equal("A, Typing, 1.5 seconds.", sounds[0].Spoken);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
