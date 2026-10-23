using System.IO;
using AccessibleVideoEditor.Core.Rendering;
using NAudio.Wave;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Plays one sound file or web address for listening before it is added (Space in the library and in
/// sound lists). FFmpeg decodes it and NAudio plays it, so Windows Media Player is not needed.
/// </summary>
public sealed class SoundPreviewPlayer : IDisposable
{
    private readonly Func<FfmpegTools?> _tools;
    private FfmpegStream? _stream;
    private WaveOutEvent? _output;
    private CancellationTokenSource? _cancel;

    public SoundPreviewPlayer(Func<FfmpegTools?> tools)
    {
        _tools = tools;
    }

    /// <summary>Starts playing <paramref name="source"/>, stopping anything already playing. False if it can't play.</summary>
    public bool Play(string source)
    {
        Stop();
        if (_tools() is not { } tools)
        {
            return false;
        }

        try
        {
            var buffer = new BufferedWaveProvider(new WaveFormat(48000, 16, 2))
            {
                BufferDuration = TimeSpan.FromSeconds(6),
                ReadFully = false,
                DiscardOnBufferOverflow = false,
            };
            var stream = FfmpegStream.Decode(tools, source);
            var cancel = new CancellationTokenSource();
            _stream = stream;
            _cancel = cancel;
            var token = cancel.Token;
            _ = Task.Run(() =>
            {
                var chunk = new byte[19200];
                try
                {
                    int read;
                    while (!token.IsCancellationRequested && (read = stream.Output.Read(chunk, 0, chunk.Length)) > 0)
                    {
                        // Wait for room rather than dropping sound.
                        while (!token.IsCancellationRequested && buffer.BufferedBytes + read > buffer.BufferLength)
                        {
                            Thread.Sleep(20);
                        }

                        buffer.AddSamples(chunk, 0, read);
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                }
            }, token);

            _output = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
            _output.Init(buffer);
            _output.Play();
            return true;
        }
        catch (Exception ex) when (ex is RenderException or IOException or InvalidOperationException or NAudio.MmException or System.ComponentModel.Win32Exception)
        {
            AppPaths.Log("Sound preview failed: " + ex.Message);
            Stop();
            return false;
        }
    }

    public void Stop()
    {
        _cancel?.Cancel();
        try
        {
            _output?.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NAudio.MmException)
        {
        }

        _output?.Dispose();
        _output = null;
        _stream?.Dispose();
        _stream = null;
        _cancel?.Dispose();
        _cancel = null;
    }

    public void Dispose() => Stop();
}
