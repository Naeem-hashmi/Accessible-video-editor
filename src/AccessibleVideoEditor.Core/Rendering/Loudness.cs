using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core.Model;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>
/// Measures how loud a project's mix is (integrated loudness, EBU R128, as YouTube and Instagram measure
/// it), so an export can be levelled to exactly the target in a second pass.
/// </summary>
public static partial class Loudness
{
    /// <summary>The gain in dB that brings the mix to the project's target, or null when no target is set or the mix is silent.</summary>
    public static async Task<double?> MasterGainAsync(FfmpegTools tools, Project project, RenderScope scope, double start = 0, double? end = null,
        CancellationToken token = default)
    {
        if (project.MasterLoudness is not { } target)
        {
            return null;
        }

        var measured = await MeasureAsync(tools, project, scope, start, end, null, token).ConfigureAwait(false);
        if (measured is not { } lufs || lufs <= -70)
        {
            return null;
        }

        // The limiter that keeps 1 dB of headroom takes some loudness off very punchy mixes, so measure the
        // levelled mix once more and correct by the difference.
        var gain = target - lufs;
        var check = await MeasureAsync(tools, project, scope, start, end, gain, token).ConfigureAwait(false);
        if (check is { } after && after > -70)
        {
            gain += Math.Clamp(target - after, -3, 6);
        }

        return gain;
    }

    /// <summary>Integrated loudness of the mix in LUFS (with <paramref name="gainDb"/> applied, if given), or null if FFmpeg reported none.</summary>
    public static async Task<double?> MeasureAsync(FfmpegTools tools, Project project, RenderScope scope, double start = 0, double? end = null,
        double? gainDb = null, CancellationToken token = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"ave-loudness-{Guid.NewGuid():N}.wav");
        try
        {
            var plan = new GraphBuilder(tools.HasEncoder, tools.Text).Build(new RenderRequest
            {
                Project = project, Scope = scope, Start = start, End = end, Profile = OutputProfile.Wav, OutputPath = temp,
                SkipMasterLoudness = gainDb is null, MasterGainDb = gainDb,
            });
            await RenderRunner.RunAsync(tools, plan, temp, token: token).ConfigureAwait(false);
            return await MeasureFileAsync(tools, temp, token).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
            }
        }
    }

    public static async Task<double?> MeasureFileAsync(FfmpegTools tools, string path, CancellationToken token = default)
    {
        var psi = new ProcessStartInfo(tools.FfmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-hide_banner", "-nostats", "-nostdin", "-i", path, "-vn", "-af", "ebur128", "-f", "null", "-" })
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi) ?? throw new RenderException("Could not start FFmpeg.");
        var errors = process.StandardError.ReadToEndAsync(token);
        await process.StandardOutput.ReadToEndAsync(token).ConfigureAwait(false);
        await process.WaitForExitAsync(token).ConfigureAwait(false);
        return Parse(await errors.ConfigureAwait(false));
    }

    /// <summary>Reads "I: -14.2 LUFS" from the summary that FFmpeg's ebur128 filter prints.</summary>
    public static double? Parse(string ebur128Output)
    {
        var summary = ebur128Output.LastIndexOf("Summary:", StringComparison.Ordinal);
        if (summary < 0)
        {
            return null;
        }

        var match = IntegratedPattern().Match(ebur128Output, summary);
        return match.Success && match.Groups[1].Value != "-inf" ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"I:\s*(-?[\d.]+|-inf)\s*LUFS")]
    private static partial Regex IntegratedPattern();
}
