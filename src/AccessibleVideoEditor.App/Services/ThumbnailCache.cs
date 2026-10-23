using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.App.Services;

/// <summary>
/// Small pictures for the drawn timeline, made by FFmpeg in the background (two at a time) and kept for
/// reuse. Asking for one that is not ready yet returns null and starts making it; <see cref="Ready"/> fires
/// when it arrives so the timeline can draw it.
/// </summary>
public sealed class ThumbnailCache
{
    public const int Width = 64;
    public const int Height = 36;
    private const int Keep = 800;

    private readonly Func<FfmpegTools?> _tools;
    private readonly Dictionary<(string Path, double Time), ImageSource?> _images = new();
    private readonly Queue<(string Path, double Time)> _order = new();
    private readonly HashSet<(string Path, double Time)> _pending = new();
    private readonly SemaphoreSlim _workers = new(2);

    public ThumbnailCache(Func<FfmpegTools?> tools) => _tools = tools;

    /// <summary>Raised on the window thread when a new picture is ready.</summary>
    public event Action? Ready;

    public ImageSource? Get(string path, double time)
    {
        var key = (path, Math.Round(time, 2));
        if (_images.TryGetValue(key, out var image))
        {
            return image;
        }

        if (_pending.Add(key) && _tools() is { } tools && File.Exists(path))
        {
            var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            _ = Task.Run(async () =>
            {
                await _workers.WaitAsync().ConfigureAwait(false);
                ImageSource? made = null;
                try
                {
                    if (FfmpegStream.Frame(tools, key.path, key.Item2, Width, Height) is { } pixels)
                    {
                        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
                        bitmap.Freeze();
                        made = bitmap;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    AppPaths.Log("Thumbnail failed: " + ex.Message);
                }
                finally
                {
                    _workers.Release();
                }

                await dispatcher.InvokeAsync(() => Store(key, made));
            });
        }

        return null;
    }

    private void Store((string Path, double Time) key, ImageSource? image)
    {
        _pending.Remove(key);
        _images[key] = image;
        _order.Enqueue(key);
        while (_order.Count > Keep)
        {
            _images.Remove(_order.Dequeue());
        }

        Ready?.Invoke();
    }
}
