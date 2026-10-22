using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Plays the project while editing. A short, low-resolution preview is rendered with FFmpeg from a little
/// before the playhead and played with Windows Media Foundation. Previews are cached, so playing the
/// same part again starts at once.
/// </summary>
public sealed class PreviewService : IDisposable
{
    /// <summary>Preview windows start this much before the playhead so J (jump back) is instant.</summary>
    private const double LeadIn = 10;

    private static readonly double[] Speeds = [1, 2, 4];

    private readonly MediaElement _media;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, string> _cache = new();
    private CancellationTokenSource? _renderCancel;
    private TaskCompletionSource<bool>? _opening;
    private string? _currentKey;
    private double _fileStart;
    private double _fileLength;
    private double _projectEnd;
    private int _speedIndex;

    public PreviewService(MediaElement media)
    {
        _media = media;
        _media.LoadedBehavior = MediaState.Manual;
        _media.UnloadedBehavior = MediaState.Manual;
        _media.ScrubbingEnabled = true;
        _media.MediaOpened += (_, _) => _opening?.TrySetResult(true);
        _media.MediaFailed += (_, e) =>
        {
            AppPaths.Log("Preview playback failed: " + e.ErrorException?.Message);
            _opening?.TrySetResult(false);
        };
        _media.MediaEnded += (_, _) => OnWindowEnded();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) =>
        {
            if (IsPlaying)
            {
                PositionChanged?.Invoke(_fileStart + _media.Position.TotalSeconds);
            }
        };
    }

    public FfmpegTools? Tools { get; set; }

    /// <summary>Seconds rendered per preview window.</summary>
    public double WindowSeconds { get; set; } = 60;

    public bool IsPlaying { get; private set; }

    public bool IsPreparing => _renderCancel is not null;

    public double Speed => Speeds[_speedIndex];

    /// <summary>Timeline position while playing.</summary>
    public event Action<double>? PositionChanged;

    /// <summary>The end of the project was reached.</summary>
    public event Action? ReachedEnd;

    /// <summary>Preview rendering progress, 0 to 1, for announcements.</summary>
    public event Action<double>? Preparing;

    /// <summary>
    /// Plays from <paramref name="time"/>. Returns an error message, or null when playback started
    /// (or was cancelled by a newer request).
    /// </summary>
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
            return "The playhead is at the end. Press Control Home to go to the start.";
        }

        _projectEnd = end;
        var windowStart = Math.Max(0, time - LeadIn);
        var key = CacheKey(project, scope, windowStart);
        var reuse = _currentKey is not null && FitsCurrent(project, scope, time);

        if (!reuse)
        {
            Pause();
            var file = _cache.GetValueOrDefault(key);
            if (file is null || !File.Exists(file))
            {
                file = await RenderAsync(project, scope, windowStart, key).ConfigureAwait(true);
                if (file is null)
                {
                    return null;
                }
            }

            if (!await OpenAsync(file).ConfigureAwait(true))
            {
                return "Windows could not play the preview. Make sure the Media Feature Pack is installed (Windows N editions need it).";
            }

            _currentKey = key;
            _fileStart = windowStart;
            _fileLength = Math.Min(WindowSeconds + LeadIn, end - windowStart);
        }

        _media.Position = TimeSpan.FromSeconds(Math.Max(0, time - _fileStart));
        _media.SpeedRatio = Speed;
        _media.Play();
        IsPlaying = true;
        _timer.Start();
        return null;
    }

    /// <summary>Plays faster: 1, 2, then 4 times speed, then back to normal.</summary>
    public double Faster()
    {
        _speedIndex = (_speedIndex + 1) % Speeds.Length;
        _media.SpeedRatio = Speed;
        return Speed;
    }

    public void ResetSpeed()
    {
        _speedIndex = 0;
        _media.SpeedRatio = 1;
    }

    public void Pause()
    {
        if (IsPlaying)
        {
            _media.Pause();
        }

        IsPlaying = false;
        _timer.Stop();
    }

    /// <summary>Stops any preview being prepared.</summary>
    public void CancelPreparing()
    {
        _renderCancel?.Cancel();
    }

    /// <summary>The project changed: cached previews are kept, but the playing one may be out of date.</summary>
    public void Invalidate() => _currentKey = null;

    public double CurrentTime => _fileStart + _media.Position.TotalSeconds;

    private bool FitsCurrent(Project project, RenderScope scope, double time)
    {
        if (time < _fileStart || time > _fileStart + _fileLength - 0.5)
        {
            return false;
        }

        return _currentKey == CacheKey(project, scope, _fileStart);
    }

    private async Task<string?> RenderAsync(Project project, RenderScope scope, double start, string key)
    {
        _renderCancel?.Cancel();
        var cancel = new CancellationTokenSource();
        _renderCancel = cancel;
        try
        {
            var hasVideo = GraphBuilder.ScopeHasVideo(project, scope);
            var profile = hasVideo ? OutputProfile.PreviewVideo : OutputProfile.PreviewAudio;
            Directory.CreateDirectory(AppPaths.PreviewFolder);
            var output = Path.Combine(AppPaths.PreviewFolder, $"preview-{key[..16]}{profile.Extension}");
            var canvas = PreviewCanvas(project);
            var request = new RenderRequest
            {
                Project = project,
                Scope = scope,
                Start = start,
                End = start + WindowSeconds + LeadIn,
                Profile = profile,
                OutputPath = output,
                CanvasOverride = canvas,
            };
            var plan = new GraphBuilder(Tools!.HasEncoder).Build(request);
            var progress = new Progress<double>(p => Preparing?.Invoke(p));
            await Task.Run(() => RenderRunner.RunAsync(Tools, plan, output, progress, cancel.Token), cancel.Token).ConfigureAwait(true);
            _cache[key] = output;
            TrimCache();
            return output;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (ReferenceEquals(_renderCancel, cancel))
            {
                _renderCancel = null;
            }

            cancel.Dispose();
        }
    }

    private async Task<bool> OpenAsync(string file)
    {
        _opening = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _media.Stop();
        _media.Source = null;
        _media.Source = new Uri(file);
        _media.Play();
        _media.Pause();
        var finished = await Task.WhenAny(_opening.Task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(true);
        return finished == _opening.Task && _opening.Task.Result;
    }

    private void OnWindowEnded()
    {
        var reached = _fileStart + _fileLength;
        Pause();
        _currentKey = null;
        if (reached >= _projectEnd - 0.1)
        {
            ReachedEnd?.Invoke();
        }
        else
        {
            // More of the project follows: let the window ask to continue from here.
            PositionChanged?.Invoke(reached);
            ContinueRequested?.Invoke(reached);
        }
    }

    /// <summary>A preview window ended before the project did; play on from this time.</summary>
    public event Action<double>? ContinueRequested;

    private static (int Width, int Height) PreviewCanvas(Project project)
    {
        const int maxW = 640;
        const int maxH = 360;
        var scale = Math.Min((double)maxW / project.Width, (double)maxH / project.Height);
        scale = Math.Min(1, scale);
        return ((int)Math.Round(project.Width * scale / 2) * 2, (int)Math.Round(project.Height * scale / 2) * 2);
    }

    private string CacheKey(Project project, RenderScope scope, double start)
    {
        var text = $"{ProjectSerializer.ToJson(project, indented: false)}|{scope.LayerId}|{start:0.###}|{WindowSeconds}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private void TrimCache()
    {
        while (_cache.Count > 12)
        {
            var oldest = _cache.First();
            _cache.Remove(oldest.Key);
            TryDelete(oldest.Value);
        }
    }

    public void Dispose()
    {
        _renderCancel?.Cancel();
        _timer.Stop();
        _media.Stop();
        _media.Source = null;
        foreach (var file in _cache.Values)
        {
            TryDelete(file);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
