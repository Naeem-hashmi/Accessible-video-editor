using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AccessibleVideoEditor.Core.Rendering;

public sealed class FfmpegNotFoundException(string message) : Exception(message);

/// <summary>Locations and abilities of the ffmpeg and ffprobe programs.</summary>
public sealed partial class FfmpegTools
{
    private FfmpegTools(string ffmpegPath, string ffprobePath, int majorVersion, string versionText, IReadOnlySet<string> encoders)
    {
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
        MajorVersion = majorVersion;
        VersionText = versionText;
        Encoders = encoders;
    }

    public string FfmpegPath { get; }

    public string FfprobePath { get; }

    /// <summary>Major version, or 0 when it can't be read (development builds).</summary>
    public int MajorVersion { get; }

    public string VersionText { get; }

    public IReadOnlySet<string> Encoders { get; }

    /// <summary>
    /// FFmpeg 7 reads a filter graph from a file with "-/filter_complex"; older versions use
    /// "-filter_complex_script" (deprecated in 7).
    /// </summary>
    public string FilterScriptOption => MajorVersion is 0 or >= 7 ? "-/filter_complex" : "-filter_complex_script";

    public bool HasEncoder(string name) => Encoders.Contains(name);

    /// <summary>
    /// Finds ffmpeg: an explicit folder, the FFMPEG_PATH environment variable (folder or ffmpeg file), an
    /// "ffmpeg" folder next to the program, the program folder itself, then the system PATH.
    /// </summary>
    public static async Task<FfmpegTools> LocateAsync(string? preferredFolder = null, CancellationToken token = default)
    {
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var probeExe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";

        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredFolder))
        {
            folders.Add(preferredFolder);
        }

        var env = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(env))
        {
            folders.Add(File.Exists(env) ? Path.GetDirectoryName(env)! : env);
        }

        folders.Add(Path.Combine(AppContext.BaseDirectory, "ffmpeg"));
        folders.Add(AppContext.BaseDirectory);
        folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        foreach (var folder in folders)
        {
            var ffmpeg = Path.Combine(folder, exe);
            var ffprobe = Path.Combine(folder, probeExe);
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                return await InspectAsync(ffmpeg, ffprobe, token).ConfigureAwait(false);
            }
        }

        throw new FfmpegNotFoundException(
            "FFmpeg was not found. Reinstall Accessible Video Editor, or put ffmpeg and ffprobe in the program's ffmpeg folder.");
    }

    public static async Task<FfmpegTools> InspectAsync(string ffmpegPath, string ffprobePath, CancellationToken token = default)
    {
        var version = await RunAsync(ffmpegPath, ["-hide_banner", "-version"], token).ConfigureAwait(false);
        var encoderList = await RunAsync(ffmpegPath, ["-hide_banner", "-encoders"], token).ConfigureAwait(false);
        var firstLine = version.Split('\n').FirstOrDefault()?.Trim() ?? "";
        var match = VersionPattern().Match(firstLine);
        var major = match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
        return new FfmpegTools(ffmpegPath, ffprobePath, major, firstLine, ParseEncoders(encoderList));
    }

    internal static HashSet<string> ParseEncoders(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            // Lines look like " V....D libx264              libx264 H.264 ...".
            var m = EncoderLine().Match(line);
            if (m.Success)
            {
                set.Add(m.Groups[1].Value);
            }
        }

        return set;
    }

    private static async Task<string> RunAsync(string file, string[] args, CancellationToken token)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi) ?? throw new FfmpegNotFoundException($"Could not start {file}.");
        var output = p.StandardOutput.ReadToEndAsync(token);
        var error = p.StandardError.ReadToEndAsync(token);
        await p.WaitForExitAsync(token).ConfigureAwait(false);
        return await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
    }

    [GeneratedRegex(@"ffmpeg version n?(\d+)\.")]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^\s[VAS][\.F][\.S][\.X][\.B][\.D]\s+(\S+)")]
    private static partial Regex EncoderLine();
}
