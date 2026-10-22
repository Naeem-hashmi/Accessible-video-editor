namespace AccessibleVideoEditor.Core.Rendering;

public enum OutputKind
{
    VideoAndAudio,
    AudioOnly,
}

/// <summary>An export or preview format. Width and height of 0 mean "use the project canvas size".</summary>
public sealed record OutputProfile(
    string Id,
    string Name,
    string Description,
    string Extension,
    OutputKind Kind,
    int Width,
    int Height,
    int Crf,
    string X264Preset,
    string AudioCodec,
    string AudioBitrate)
{
    public bool HasVideo => Kind == OutputKind.VideoAndAudio;

    public static OutputProfile YouTube1080 { get; } = new(
        "youtube-1080p", "YouTube and web, 1080p HD (recommended)",
        "1920 by 1080, H.264 high quality, AAC sound. Plays everywhere.",
        ".mp4", OutputKind.VideoAndAudio, 1920, 1080, 18, "medium", "aac", "256k");

    public static OutputProfile YouTube720 { get; } = new(
        "youtube-720p", "720p HD, smaller file",
        "1280 by 720, H.264, AAC sound. Faster to upload.",
        ".mp4", OutputKind.VideoAndAudio, 1280, 720, 20, "medium", "aac", "192k");

    public static OutputProfile Uhd4K { get; } = new(
        "uhd-4k", "4K Ultra HD",
        "3840 by 2160, H.264, AAC sound. Large file, slow to export.",
        ".mp4", OutputKind.VideoAndAudio, 3840, 2160, 18, "medium", "aac", "320k");

    public static OutputProfile Vertical { get; } = new(
        "vertical-1080x1920", "Vertical for Shorts, Reels and TikTok",
        "1080 by 1920 portrait. Landscape video gets black bars above and below.",
        ".mp4", OutputKind.VideoAndAudio, 1080, 1920, 19, "medium", "aac", "256k");

    public static OutputProfile Square { get; } = new(
        "square-1080", "Square for social media",
        "1080 by 1080. Landscape video gets black bars above and below.",
        ".mp4", OutputKind.VideoAndAudio, 1080, 1080, 19, "medium", "aac", "256k");

    public static OutputProfile ProjectSize { get; } = new(
        "project-size", "Same size as the project",
        "Uses the project's own width and height, H.264, AAC sound.",
        ".mp4", OutputKind.VideoAndAudio, 0, 0, 18, "medium", "aac", "256k");

    public static OutputProfile Mp3 { get; } = new(
        "audio-mp3", "Audio only, MP3",
        "MP3 at 320 kilobits per second. For podcasts and music.",
        ".mp3", OutputKind.AudioOnly, 0, 0, 0, "", "libmp3lame", "320k");

    public static OutputProfile Wav { get; } = new(
        "audio-wav", "Audio only, WAV (uncompressed)",
        "16-bit WAV. Best quality for further editing; large file.",
        ".wav", OutputKind.AudioOnly, 0, 0, 0, "", "pcm_s16le", "");

    public static OutputProfile M4a { get; } = new(
        "audio-m4a", "Audio only, M4A (AAC)",
        "AAC at 256 kilobits per second. Small, high quality.",
        ".m4a", OutputKind.AudioOnly, 0, 0, 0, "", "aac", "256k");

    /// <summary>Fast, small preview used for listening while editing.</summary>
    public static OutputProfile PreviewVideo { get; } = new(
        "preview-video", "Preview", "Low resolution preview.",
        ".mp4", OutputKind.VideoAndAudio, 640, 360, 30, "ultrafast", "aac", "160k");

    public static OutputProfile PreviewAudio { get; } = new(
        "preview-audio", "Audio preview", "Audio-only preview.",
        ".m4a", OutputKind.AudioOnly, 0, 0, 0, "", "aac", "160k");

    public static IReadOnlyList<OutputProfile> ExportProfiles { get; } =
        [YouTube1080, YouTube720, Uhd4K, Vertical, Square, ProjectSize, Mp3, Wav, M4a];

    public static OutputProfile? Find(string id) => ExportProfiles.FirstOrDefault(p => p.Id == id);
}
