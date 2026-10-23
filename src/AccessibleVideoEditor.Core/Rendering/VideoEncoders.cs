using System.Diagnostics;
using System.Globalization;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>How to trade export time against quality and file size.</summary>
public enum ExportSpeed
{
    /// <summary>x264 "medium": the best picture for the file size, slowest.</summary>
    BestQuality,

    /// <summary>x264 "veryfast" at slightly higher quality settings: about three times faster, a little larger file.</summary>
    Balanced,

    /// <summary>The video chip's own encoder (Intel Quick Sync, NVIDIA or AMD) when it works, else x264 "superfast".</summary>
    Fastest,
}

/// <summary>The H.264 encoder to use for one export, and its speed setting.</summary>
public sealed record VideoEncoder(string Name, string X264Preset = "medium", int QualityBoost = 0)
{
    public bool IsHardware => VideoEncoders.HardwareNames.Contains(Name);

    public string Spoken => Name switch
    {
        "h264_qsv" => "the Intel video chip (Quick Sync)",
        "h264_nvenc" => "the NVIDIA graphics card",
        "h264_amf" => "the AMD graphics chip",
        _ => $"the processor (x264, {X264Preset})",
    };
}

/// <summary>
/// Picks the video encoder for an export. Hardware encoders are tried with a tiny test encode first, because
/// FFmpeg lists them even on computers without the chip or driver, and a failed one must never break an export.
/// </summary>
public static class VideoEncoders
{
    /// <summary>Hardware H.264 encoders, in the order they are preferred.</summary>
    public static IReadOnlyList<string> HardwareNames { get; } = ["h264_qsv", "h264_nvenc", "h264_amf"];

    /// <summary>The hardware encoders that really work on this computer, best first.</summary>
    public static async Task<IReadOnlyList<string>> DetectAsync(FfmpegTools tools, CancellationToken token = default)
    {
        var working = new List<string>();
        foreach (var name in HardwareNames.Where(tools.HasEncoder))
        {
            if (await WorksAsync(tools, name, token).ConfigureAwait(false))
            {
                working.Add(name);
            }
        }

        return working;
    }

    private static async Task<bool> WorksAsync(FfmpegTools tools, string name, CancellationToken token)
    {
        var psi = new ProcessStartInfo(tools.FfmpegPath)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-hide_banner", "-nostdin", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=0.5" })
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var arg in Args(new VideoEncoder(name), crf: 20, frameRate: 30))
        {
            psi.ArgumentList.Add(arg);
        }

        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            _ = process.StandardOutput.ReadToEndAsync(token);
            _ = process.StandardError.ReadToEndAsync(token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>The encoder for a speed choice. <paramref name="hardware"/> lists encoders proven to work.</summary>
    public static VideoEncoder Choose(ExportSpeed speed, IReadOnlyList<string> hardware) => speed switch
    {
        ExportSpeed.BestQuality => new VideoEncoder("libx264", "medium"),
        ExportSpeed.Balanced => new VideoEncoder("libx264", "veryfast", QualityBoost: 1),
        _ when hardware.Count > 0 => new VideoEncoder(hardware[0]),
        _ => new VideoEncoder("libx264", "superfast", QualityBoost: 2),
    };

    /// <summary>The processor encoder to fall back on when a hardware one fails during an export.</summary>
    public static VideoEncoder Fallback => new("libx264", "superfast", QualityBoost: 2);

    /// <summary>
    /// FFmpeg arguments for the encoder at the quality of <paramref name="crf"/> (x264's scale: lower is
    /// better). Each hardware encoder gets its own constant-quality mode, so files look alike.
    /// </summary>
    public static IReadOnlyList<string> Args(VideoEncoder encoder, int crf, double frameRate)
    {
        var rate = frameRate.ToString("0.###", CultureInfo.InvariantCulture);
        var q = Math.Max(1, crf - encoder.QualityBoost).ToString(CultureInfo.InvariantCulture);
        return encoder.Name switch
        {
            "h264_qsv" => ["-c:v", "h264_qsv", "-preset", "faster", "-global_quality", (crf + 2).ToString(CultureInfo.InvariantCulture), "-look_ahead", "0",
                "-pix_fmt", "nv12", "-r", rate],
            "h264_nvenc" => ["-c:v", "h264_nvenc", "-preset", "p4", "-tune", "hq", "-rc", "vbr", "-cq", (crf + 2).ToString(CultureInfo.InvariantCulture), "-b:v", "0",
                "-pix_fmt", "yuv420p", "-r", rate],
            "h264_amf" => ["-c:v", "h264_amf", "-quality", "balanced", "-rc", "cqp", "-qp_i", q, "-qp_p", (crf + 2).ToString(CultureInfo.InvariantCulture),
                "-pix_fmt", "nv12", "-r", rate],
            _ => ["-c:v", "libx264", "-preset", encoder.X264Preset, "-crf", q, "-pix_fmt", "yuv420p", "-r", rate],
        };
    }
}
