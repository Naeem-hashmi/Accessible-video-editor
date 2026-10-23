using System.Diagnostics;
using System.Text;

namespace AccessibleVideoEditor.Core.Rendering;

/// <summary>
/// A running FFmpeg whose standard output is the media itself (raw sound or raw frames), for live
/// playback that starts at once instead of waiting for a file to be rendered.
/// </summary>
public sealed class FfmpegStream : IDisposable
{
    private readonly Process _process;
    private readonly string _workFolder;
    private readonly StringBuilder _errors = new();
    private bool _disposed;

    private FfmpegStream(Process process, string workFolder)
    {
        _process = process;
        _workFolder = workFolder;
    }

    /// <summary>The raw media. Reading blocks until FFmpeg has produced more; 0 bytes means the end.</summary>
    public Stream Output => _process.StandardOutput.BaseStream;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public int? ExitCode => HasExited ? _process.ExitCode : null;

    /// <summary>The last error lines FFmpeg printed, if any.</summary>
    public string Errors
    {
        get
        {
            lock (_errors)
            {
                return _errors.ToString().Trim();
            }
        }
    }

    public static FfmpegStream Start(FfmpegTools tools, RenderPlan plan)
    {
        var workFolder = Path.Combine(Path.GetTempPath(), $"ave-stream-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workFolder);
        var utf8 = new UTF8Encoding(false);
        var script = Path.Combine(workFolder, "graph.txt");
        File.WriteAllText(script, plan.FilterGraph, utf8);
        foreach (var (name, file) in plan.AuxiliaryFiles)
        {
            var target = Path.Combine(workFolder, name);
            if (file.CopyFrom is { } source)
            {
                File.Copy(source, target, overwrite: true);
            }
            else
            {
                File.WriteAllText(target, file.Content ?? "", utf8);
            }
        }

        return Launch(tools, plan.BuildArguments(script, tools.FilterScriptOption), workFolder);
    }

    /// <summary>
    /// Decodes the sound of one file or web address to raw 16-bit stereo 48 kHz, for listening to a sound
    /// before adding it. Plays at most <paramref name="maxSeconds"/>.
    /// </summary>
    public static FfmpegStream Decode(FfmpegTools tools, string source, double maxSeconds = 300)
    {
        var workFolder = Path.Combine(Path.GetTempPath(), $"ave-stream-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workFolder);
        string[] args =
        [
            "-hide_banner", "-nostdin", "-loglevel", "error", "-i", source, "-vn",
            "-t", maxSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-ac", "2", "-ar", "48000", "-c:a", "pcm_s16le", "-f", "s16le", "pipe:1",
        ];
        return Launch(tools, args, workFolder);
    }

    private static FfmpegStream Launch(FfmpegTools tools, IEnumerable<string> arguments, string workFolder)
    {
        var psi = new ProcessStartInfo(tools.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workFolder,
        };
        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = psi };
        var stream = new FfmpegStream(process, workFolder);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (stream._errors)
            {
                if (stream._errors.Length < 8000)
                {
                    stream._errors.AppendLine(e.Data);
                }
            }
        };

        if (!process.Start())
        {
            throw new RenderException("Could not start FFmpeg.");
        }

        process.BeginErrorReadLine();
        return stream;
    }

    /// <summary>Reads exactly <paramref name="buffer"/>.Length bytes, or returns false at the end of the stream.</summary>
    public bool ReadExactly(byte[] buffer, CancellationToken token)
    {
        var filled = 0;
        while (filled < buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = Output.Read(buffer, filled, buffer.Length - filled);
            if (read == 0)
            {
                return false;
            }

            filled += read;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        _process.Dispose();
        try
        {
            Directory.Delete(_workFolder, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
