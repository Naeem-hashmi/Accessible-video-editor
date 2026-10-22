using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Templates;

/// <summary>A starting point for a new project: canvas size, layers and their default settings.</summary>
public sealed record ProjectTemplate(string Id, string Name, string Description, Func<Project> Create);

public static class ProjectTemplates
{
    public static IReadOnlyList<ProjectTemplate> All { get; } = new ProjectTemplate[]
    {
        new("basic", "Basic video",
            "One video layer and one audio layer, 1920 by 1080. Good for simple trimming and joining clips.",
            Basic),
        new("youtube", "YouTube video with music and logo",
            "Main video, a logo layer in the top right corner, background music at 20 percent volume, and a sound effects layer. Clips cross fade by default.",
            YouTube),
        new("slideshow", "Photo slideshow",
            "Photos show for 4 seconds each and cross fade into each other, with a music layer that fades in and out.",
            Slideshow),
        new("pip", "Interview or tutorial, picture in picture",
            "Main video full screen, plus a second video layer for a small camera picture in the bottom right corner.",
            PictureInPicture),
        new("podcast", "Podcast or audio to video",
            "A still picture layer and a voice layer with voice clean-up switched on, plus music at low volume.",
            Podcast),
        new("vertical", "Vertical short (Shorts, Reels, TikTok)",
            "1080 by 1920 portrait canvas with a video layer, a text or logo layer at the top, and music.",
            Vertical),
    };

    public static ProjectTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static Project Basic()
    {
        var p = new Project { Title = "Basic video" };
        ProjectEditor.AddLayer(p, LayerKind.Video, "Video");
        ProjectEditor.AddLayer(p, LayerKind.Audio, "Audio");
        return p;
    }

    private static Project YouTube()
    {
        var p = new Project { Title = "YouTube video" };
        var main = ProjectEditor.AddLayer(p, LayerKind.Video, "Main video");
        main.Defaults.Transition = new TransitionSpec { Video = "fade", Audio = "qsin", Duration = 0.5 };
        var logo = ProjectEditor.AddLayer(p, LayerKind.Video, "Logo");
        logo.Defaults.Placement = new Placement { Position = PositionPreset.TopRight, SizePercent = 15, OpacityPercent = 85 };
        logo.Defaults.ImageDuration = 10;
        var music = ProjectEditor.AddLayer(p, LayerKind.Audio, "Music");
        music.Defaults.Volume = 0.2;
        music.Defaults.Transition = new TransitionSpec { Audio = "qsin", Duration = 2 };
        ProjectEditor.AddLayer(p, LayerKind.Audio, "Sound effects");
        return p;
    }

    private static Project Slideshow()
    {
        var p = new Project { Title = "Photo slideshow" };
        var photos = ProjectEditor.AddLayer(p, LayerKind.Video, "Photos");
        photos.Defaults.ImageDuration = 4;
        photos.Defaults.Transition = new TransitionSpec { Video = "fade", Audio = "tri", Duration = 1 };
        var music = ProjectEditor.AddLayer(p, LayerKind.Audio, "Music");
        music.Defaults.Volume = 0.6;
        music.Defaults.Transition = new TransitionSpec { Audio = "qsin", Duration = 2 };
        return p;
    }

    private static Project PictureInPicture()
    {
        var p = new Project { Title = "Picture in picture" };
        ProjectEditor.AddLayer(p, LayerKind.Video, "Screen or main video");
        var camera = ProjectEditor.AddLayer(p, LayerKind.Video, "Camera");
        camera.Defaults.Placement = new Placement { Position = PositionPreset.BottomRight, SizePercent = 28 };
        ProjectEditor.AddLayer(p, LayerKind.Audio, "Extra audio");
        return p;
    }

    private static Project Podcast()
    {
        var p = new Project { Title = "Podcast video" };
        var picture = ProjectEditor.AddLayer(p, LayerKind.Video, "Cover picture");
        picture.Defaults.ImageDuration = 60;
        var voice = ProjectEditor.AddLayer(p, LayerKind.Audio, "Voice");
        voice.Defaults.Audio = new AudioCleanup
        {
            NoiseReduction = NoiseReduction.Light, RemoveRumble = true, Voice = VoicePreset.Clear, Normalize = true,
        };
        var music = ProjectEditor.AddLayer(p, LayerKind.Audio, "Music");
        music.Defaults.Volume = 0.15;
        return p;
    }

    private static Project Vertical()
    {
        var p = new Project { Title = "Vertical short", Width = 1080, Height = 1920 };
        ProjectEditor.AddLayer(p, LayerKind.Video, "Video");
        var top = ProjectEditor.AddLayer(p, LayerKind.Video, "Title or logo");
        top.Defaults.Placement = new Placement { Position = PositionPreset.TopCenter, SizePercent = 60 };
        var music = ProjectEditor.AddLayer(p, LayerKind.Audio, "Music");
        music.Defaults.Volume = 0.3;
        return p;
    }
}
