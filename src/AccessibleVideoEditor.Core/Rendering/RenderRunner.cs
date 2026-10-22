using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>Runs a <see cref="RenderPlan"/> with ffmpeg, reporting progress from 0 to 1.</summary>
public static class RenderRunner
{
    /// <summary>
    /// Renders the plan. On failure or cancellation the partial output file is deleted.
    /// </summary>
    /// <exception cref="RenderException">FFmpeg reported an error.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static async Task RunAsync(FfmpegTools tools, RenderPlan plan, string outputPath, IProgress<double>? progress = null,
        CancellationToken token = default)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ave-graph-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(scriptPath, plan.FilterGraph, new UTF8Encoding(false), token).ConfigureAwait(false);
        try
        {
            var psi = new ProcessStartInfo(tools.FfmpegPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var arg in plan.BuildArguments(scriptPath, tools.FilterScriptOption))
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var errors = new StringBuilder();
            var errorsLock = new object();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (errorsLock)
                {
                    if (errors.Length < 20_000)
                    {
                        errors.AppendLine(e.Data);
                    }
                }
            };
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line && TryParseProgress(line, plan.Duration, out var fraction))
                {
                    progress?.Report(fraction);
                }
            };

            if (!process.Start())
            {
                throw new RenderException("Could not start FFmpeg.");
            }

            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            try
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                TryDelete(outputPath);
                throw;
            }

            // Make sure the asynchronous readers have drained.
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                TryDelete(outputPath);
                string text;
                lock (errorsLock)
                {
                    text = errors.ToString();
                }

                throw new RenderException(Summarize(text));
            }

            progress?.Report(1);
        }
        finally
        {
            TryDelete(scriptPath);
        }
    }

    /// <summary>Reads an "out_time_us=" line from ffmpeg's -progress output.</summary>
    public static bool TryParseProgress(string line, double totalSeconds, out double fraction)
    {
        fraction = 0;
        const string key = "out_time_us=";
        const string legacyKey = "out_time_ms=";
        string? value = null;
        if (line.StartsWith(key, StringComparison.Ordinal))
        {
            value = line[key.Length..];
        }
        else if (line.StartsWith(legacyKey, StringComparison.Ordinal))
        {
            // Despite its name this is also microseconds.
            value = line[legacyKey.Length..];
        }

        if (value is null || totalSeconds <= 0 ||
            !long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
        {
            return false;
        }

        fraction = Math.Clamp(micros / 1_000_000.0 / totalSeconds, 0, 1);
        return true;
    }

    private static string Summarize(string ffmpegErrors)
    {
        var lines = ffmpegErrors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return "FFmpeg stopped with an error.";
        }

        if (lines.Any(l => l.Contains("No space left", StringComparison.OrdinalIgnoreCase)))
        {
            return "The disk is full. Free some space and try again.";
        }

        if (lines.Any(l => l.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)))
        {
            return "Windows would not let the file be written. Choose another folder, or close the file if another program has it open.";
        }

        return "FFmpeg stopped with an error: " + string.Join(" ", lines.TakeLast(3));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
