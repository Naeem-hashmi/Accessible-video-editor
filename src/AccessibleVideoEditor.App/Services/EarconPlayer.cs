using System.IO;
using System.Media;
using AccessibleVideoEditor.Core.Sound;

namespace AccessibleVideoEditor.App.Services;

/// <summary>Plays the short interface sounds of the chosen theme, at the chosen volume.</summary>
public sealed class EarconPlayer
{
    private readonly Dictionary<EarconEvent, byte[]> _cache = new();
    private SoundPlayer? _player;
    private EarconTheme _theme = EarconTheme.BuiltIn[0];
    private int _volume = 60;

    /// <summary>Built-in themes plus folders in Documents\Accessible Video Editor\Sound Themes.</summary>
    public static IReadOnlyList<EarconTheme> AvailableThemes()
    {
        var themes = new List<EarconTheme>(EarconTheme.BuiltIn);
        if (Directory.Exists(AppPaths.UserSoundThemes))
        {
            foreach (var folder in Directory.GetDirectories(AppPaths.UserSoundThemes).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    themes.Add(EarconTheme.FromFolder(folder));
                }
                catch (IOException ex)
                {
                    AppPaths.Log($"Could not load sound theme {folder}: {ex.Message}");
                }
            }
        }

        return themes;
    }

    public string ThemeName => _theme.Name;

    public void Configure(string themeName, int volume)
    {
        _theme = AvailableThemes().FirstOrDefault(t => string.Equals(t.Name, themeName, StringComparison.OrdinalIgnoreCase))
                 ?? EarconTheme.BuiltIn[0];
        _volume = Math.Clamp(volume, 0, 100);
        _cache.Clear();
    }

    public void Play(EarconEvent e)
    {
        if (_volume == 0)
        {
            return;
        }

        if (!_cache.TryGetValue(e, out var wav))
        {
            var original = _theme.Get(e);
            if (original is null)
            {
                return;
            }

            wav = ScaleVolume(original, _volume / 100.0);
            _cache[e] = wav;
        }

        try
        {
            _player?.Stop();
            _player = new SoundPlayer(new MemoryStream(wav, writable: false));
            _player.Play();
        }
        catch (Exception ex)
        {
            // Interface sounds must never interrupt work (for example on a computer without a sound device).
            AppPaths.Log("Earcon failed: " + ex.Message);
        }
    }

    /// <summary>Scales 16-bit PCM WAV data; other formats are returned unchanged.</summary>
    internal static byte[] ScaleVolume(byte[] wav, double factor)
    {
        if (wav.Length < 44 || Math.Abs(factor - 1) < 0.001)
        {
            return wav;
        }

        var copy = (byte[])wav.Clone();
        var position = 12;
        var bitsPerSample = 0;
        while (position + 8 <= copy.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(copy, position, 4);
            var size = BitConverter.ToInt32(copy, position + 4);
            var body = position + 8;
            if (size < 0 || body + size > copy.Length)
            {
                size = copy.Length - body;
            }

            if (id == "fmt " && size >= 16)
            {
                var format = BitConverter.ToInt16(copy, body);
                bitsPerSample = format == 1 ? BitConverter.ToInt16(copy, body + 14) : 0;
            }
            else if (id == "data")
            {
                if (bitsPerSample != 16)
                {
                    return wav;
                }

                for (var i = body; i + 1 < body + size; i += 2)
                {
                    var sample = (short)(copy[i] | (copy[i + 1] << 8));
                    var scaled = (short)Math.Clamp(Math.Round(sample * factor), short.MinValue, short.MaxValue);
                    copy[i] = (byte)(scaled & 0xFF);
                    copy[i + 1] = (byte)((scaled >> 8) & 0xFF);
                }

                return copy;
            }

            position = body + size + (size % 2);
        }

        return wav;
    }
}
