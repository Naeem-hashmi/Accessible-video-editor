using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using NAudio.Wave;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Plays the project live. FFmpeg renders the mix from the playhead and streams raw sound (and small
/// picture frames) straight into the program, so playback starts at once with no file to prepare.
/// Sound goes to the standard Windows audio output through NAudio; Windows Media Player is not needed.
/// </summary>
public sealed class PreviewService : IDisposable
{
    private static readonly double[] Speeds = [1, 2, 4];
    private const int BytesPerSecond = 48000 * 4;

    private readonly Image _image;
    private readonly DispatcherTimer _timer;
    private Session? _session;
    private int _speedIndex;
    private double _lastTime;
    private Project? _project;
    private RenderScope? _scope;
    private WriteableBitmap? _bitmap;

    public PreviewService(Image image)
    {
        _image = image;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => Tick();
    }

    public FfmpegTools? Tools { get; set; }

    /// <summary>Kept for settings compatibility; live playback has no window to render.</summary>
    public double WindowSeconds { get; set; } = 60;

    public bool IsPlaying => _session is { Started: true };

    public bool IsPreparing => _session is { Started: false };

    public double Speed => Speeds[_speedIndex];

    /// <summary>Timeline position while playing.</summary>
    public event Action<double>? PositionChanged;

    /// <summary>The end of the project was reached.</summary>
    public event Action? ReachedEnd;

    /// <summary>Raised while waiting for the first sound (0 to 1), for "Preparing preview".</summary>
    public event Action<double>? Preparing;

    public double CurrentTime => _session is { } s ? s.Start + s.OutputSeconds * s.Speed : _lastTime;

    /// <summary>Plays from <paramref name="time"/>. Returns an error message, or null when playback started or was cancelled.</summary>
    public async Task<string?> PlayAsync(Project project, RenderScope scope, double time)
    {
        if (Tools is null)
        {
            return "FFmpeg was not found, so the project can't be played. Reinstall the program.";
        }

        var end = Timeline.Duration(scope.Select(project));
        if (end <= 0.05)
        {
            return scope.LayerId is null ? "Nothing to play: all layers are empty or muted." : "This layer is empty.";
        }

        if (time >= end - 0.05)
        {
            return "The playhead is at the end. Press Control Home or Control Shift Left to go to the start.";
        }

        Stop();
        _still?.Cancel();
        _project = project;
        _scope = scope;
        Session session;
        try
        {
            session = Session.Open(Tools, project, scope, time, Speed, GraphBuilder.ScopeHasVideo(project, scope));
        }
        catch (RenderException ex)
        {
            return ex.Message;
        }

        _session = session;
        if (session.HasPicture)
        {
            EnsureBitmap(session.FrameWidth, session.FrameHeight);
        }

        var clock = Stopwatch.StartNew();
        while (!session.HasEnoughSound && !session.SoundEnded && clock.Elapsed.TotalSeconds < 15)
        {
            Preparing?.Invoke(0);
            await Task.Delay(20).ConfigureAwait(true);
            if (!ReferenceEquals(_session, session))
            {
                return null;
            }
        }

        if (!session.HasEnoughSound && session.BufferedBytes == 0)
        {
            var reason = session.Errors;
            Stop();
            AppPaths.Log("Preview failed: " + reason);
            return "The preview could not be played. " + (string.IsNullOrWhiteSpace(reason) ? "" : "FFmpeg said: " + reason.Split('\n').Last().Trim());
        }

        session.StartOutput();
        _timer.Start();
        return null;
    }

    private CancellationTokenSource? _still;

    /// <summary>
    /// Shows the picture at <paramref name="time"/> while paused, like a video player does when you move
    /// through a video. Waits a moment after the last request, so moving quickly doesn't start many renders.
    /// </summary>
    public void ShowFrame(Project project, double time)
    {
        if (Tools is null || IsPlaying || IsPreparing || !GraphBuilder.ScopeHasVideo(project, RenderScope.AllLayers))
        {
            return;
        }

        _still?.Cancel();
        var cancel = new CancellationTokenSource();
        _still = cancel;
        var tools = Tools;
        var (w, h) = PreviewCanvas(project, 960, 540);
        var snapshot = Core.Editing.ProjectSerializer.Clone(project);
        _ = Task.Run(async () =>
        {
            await Task.Delay(150, cancel.Token).ConfigureAwait(false);
            var at = Math.Clamp(time, 0, Math.Max(0, Timeline.ProjectDuration(snapshot) - 0.05));
            var plan = new GraphBuilder(tools.HasEncoder, tools.Text).Build(new RenderRequest
            {
                Project = snapshot, Start = at, End = at + 0.2, Profile = OutputProfile.StreamVideo, OutputPath = "pipe:1", CanvasOverride = (w, h),
            });
            using var stream = FfmpegStream.Start(tools, plan);
            var frame = new byte[w * h * 4];
            if (!stream.ReadExactly(frame, cancel.Token) || cancel.IsCancellationRequested)
            {
                return;
            }

            await _image.Dispatcher.InvokeAsync(() =>
            {
                if (cancel.IsCancellationRequested || IsPlaying)
                {
                    return;
                }

                EnsureBitmap(w, h);
                _bitmap!.WritePixels(new Int32Rect(0, 0, w, h), frame, w * 4, 0);
            });
        }, cancel.Token).ContinueWith(t =>
        {
            if (t.Exception is { } ex && ex.InnerException is not (OperationCanceledException or TaskCanceledException))
            {
                AppPaths.Log("Still frame failed: " + ex.InnerException?.Message);
            }
        }, TaskScheduler.Default);
    }

    /// <summary>The size a preview picture is drawn at: the project's shape, at most <paramref name="maxW"/> by <paramref name="maxH"/>.</summary>
    internal static (int Width, int Height) PreviewCanvas(Project project, double maxW, double maxH)
    {
        var scale = Math.Min(1, Math.Min(maxW / project.Width, maxH / project.Height));
        return ((int)Math.Round(project.Width * scale / 2) * 2, (int)Math.Round(project.Height * scale / 2) * 2);
    }

    /// <summary>Plays faster: 1, 2, then 4 times speed, then back to normal. Restarts the stream at the new speed.</summary>
    public double Faster()
    {
        _speedIndex = (_speedIndex + 1) % Speeds.Length;
        if (IsPlaying && _project is not null && _scope is not null)
        {
            var at = CurrentTime;
            _ = PlayAsync(_project, _scope, at);
        }

        return Speed;
    }

    public void ResetSpeed() => _speedIndex = 0;

    public void Pause() => Stop();

    public void CancelPreparing()
    {
        if (IsPreparing)
        {
            Stop();
        }
    }

    /// <summary>The project changed. Live playback always uses the latest project when it restarts.</summary>
    public void Invalidate()
    {
    }

    private void Stop()
    {
        _timer.Stop();
        if (_session is { } session)
        {
            _lastTime = CurrentTime;
            _session = null;
            session.Dispose();
        }
    }

    private void Tick()
    {
        if (_session is not { Started: true } session)
        {
            return;
        }

        var output = session.OutputSeconds;
        PositionChanged?.Invoke(session.Start + output * session.Speed);
        if (session.HasPicture && _bitmap is not null && session.TakeFrame(output) is { } frame)
        {
            _bitmap.WritePixels(new Int32Rect(0, 0, session.FrameWidth, session.FrameHeight), frame, session.FrameWidth * 4, 0);
        }

        if (session.SoundEnded && session.BufferedBytes == 0 && output >= session.OutputLength - 0.15)
        {
            Stop();
            ReachedEnd?.Invoke();
        }
    }

    private void EnsureBitmap(int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            _image.Source = _bitmap;
        }
    }

    public void Dispose()
    {
        _still?.Cancel();
        Stop();
    }

    /// <summary>One run of live playback: the sound and picture streams and their readers.</summary>
    private sealed class Session : IDisposable
    {
        private readonly FfmpegStream _sound;
        private readonly FfmpegStream? _picture;
        private readonly BufferedWaveProvider _buffer = new(new WaveFormat(48000, 16, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(4),
            ReadFully = true,
            DiscardOnBufferOverflow = false,
        };

        private readonly ConcurrentQueue<(int Index, byte[] Pixels)> _frames = new();
        private readonly CancellationTokenSource _cancel = new();
        private readonly Stopwatch _fallbackClock = new();
        private WaveOutEvent? _output;
        private volatile bool _soundEnded;

        private Session(FfmpegStream sound, FfmpegStream? picture, double start, double speed, double outputLength, int width, int height)
        {
            _sound = sound;
            _picture = picture;
            Start = start;
            Speed = speed;
            OutputLength = outputLength;
            FrameWidth = width;
            FrameHeight = height;
        }

        public double Start { get; }

        public double Speed { get; }

        /// <summary>Length of the stream in playback seconds.</summary>
        public double OutputLength { get; }

        public int FrameWidth { get; }

        public int FrameHeight { get; }

        public bool HasPicture => _picture is not null;

        public bool Started { get; private set; }

        public bool SoundEnded => _soundEnded;

        public int BufferedBytes => _buffer.BufferedBytes;

        public bool HasEnoughSound => _buffer.BufferedBytes >= BytesPerSecond / 6;

        public string Errors => _sound.Errors;

        /// <summary>Seconds of sound played so far (from the sound card, or a clock when there is none).</summary>
        public double OutputSeconds => _output is { } o
            ? Math.Min(OutputLength, o.GetPosition() / (double)BytesPerSecond)
            : Math.Min(OutputLength, _fallbackClock.Elapsed.TotalSeconds);

        public static Session Open(FfmpegTools tools, Project project, RenderScope scope, double time, double speed, bool withPicture)
        {
            var builder = new GraphBuilder(tools.HasEncoder, tools.Text);
            var soundPlan = builder.Build(new RenderRequest
            {
                Project = project, Scope = scope, Start = time, Speed = speed, Profile = OutputProfile.StreamAudio, OutputPath = "pipe:1",
            });
            var (w, h) = PreviewCanvas(project);
            var sound = FfmpegStream.Start(tools, soundPlan);
            FfmpegStream? picture = null;
            if (withPicture)
            {
                try
                {
                    var picturePlan = new GraphBuilder(tools.HasEncoder, tools.Text).Build(new RenderRequest
                    {
                        Project = project, Scope = scope, Start = time, Speed = speed, Profile = OutputProfile.StreamVideo,
                        OutputPath = "pipe:1", CanvasOverride = (w, h),
                    });
                    picture = FfmpegStream.Start(tools, picturePlan);
                }
                catch (RenderException ex)
                {
                    // The sound matters most; play it even if the picture can't be made.
                    AppPaths.Log("Preview picture unavailable: " + ex.Message);
                }
            }

            var session = new Session(sound, picture, time, speed, soundPlan.Duration, w, h);
            session.ReadSound();
            if (picture is not null)
            {
                session.ReadPicture();
            }

            return session;
        }

        public void StartOutput()
        {
            try
            {
                _output = new WaveOutEvent { DesiredLatency = 150, NumberOfBuffers = 3 };
                _output.Init(_buffer);
                _output.Play();
            }
            catch (Exception ex)
            {
                // No sound device (for example on a server): keep time with a clock so the picture still plays.
                AppPaths.Log("No sound output for preview: " + ex.Message);
                _output?.Dispose();
                _output = null;
                _fallbackClock.Start();
            }

            Started = true;
        }

        /// <summary>The newest frame due at <paramref name="outputSeconds"/>, or null if none is new.</summary>
        public byte[]? TakeFrame(double outputSeconds)
        {
            var due = (int)(outputSeconds * OutputProfile.StreamFrameRate);
            byte[]? latest = null;
            while (_frames.TryPeek(out var head) && head.Index <= due)
            {
                if (_frames.TryDequeue(out var frame))
                {
                    latest = frame.Pixels;
                }
            }

            return latest;
        }

        private void ReadSound()
        {
            var token = _cancel.Token;
            Task.Factory.StartNew(() =>
            {
                var chunk = new byte[BytesPerSecond / 20];
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var read = _sound.Output.Read(chunk, 0, chunk.Length);
                        if (read == 0)
                        {
                            break;
                        }

                        while (_buffer.BufferedDuration.TotalSeconds > 2 && !token.IsCancellationRequested)
                        {
                            Thread.Sleep(15);
                        }

                        _buffer.AddSamples(chunk, 0, read);
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                }
                finally
                {
                    _soundEnded = true;
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void ReadPicture()
        {
            var token = _cancel.Token;
            Task.Factory.StartNew(() =>
            {
                var index = 0;
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        var frame = new byte[FrameWidth * FrameHeight * 4];
                        if (!_picture!.ReadExactly(frame, token))
                        {
                            break;
                        }

                        while (_frames.Count > 20 && !token.IsCancellationRequested)
                        {
                            Thread.Sleep(10);
                        }

                        _frames.Enqueue((index++, frame));
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
                {
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private static (int Width, int Height) PreviewCanvas(Project project) => PreviewService.PreviewCanvas(project, 640, 360);

        public void Dispose()
        {
            _cancel.Cancel();
            try
            {
                _output?.Stop();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NAudio.MmException)
            {
            }

            _output?.Dispose();
            _sound.Dispose();
            _picture?.Dispose();
            _cancel.Dispose();
        }
    }
}
