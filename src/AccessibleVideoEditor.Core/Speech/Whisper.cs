using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Rendering;

namespace AccessibleVideoEditor.Core.Speech;

public sealed class WhisperException(string message) : Exception(message);

/// <summary>A Whisper speech model that can be downloaded once and then used offline.</summary>
public sealed record WhisperModel(string Id, string Name, string FileName, long Bytes, string Description)
{
    public string Url => "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + FileName;

    public string SizeText => Bytes >= 1_000_000_000
        ? $"{Bytes / 1e9:0.0} GB"
        : $"{Math.Round(Bytes / 1e6):0} MB";

    public string Spoken => $"{Name}, {SizeText}. {Description}";

    /// <summary>The models offered, smallest first.</summary>
    public static IReadOnlyList<WhisperModel> All { get; } =
    [
        new("base", "Fast", "ggml-base.bin", 147_951_465,
            "Quick, good for clear English. Not recommended for Urdu or Hindi."),
        new("small", "Balanced", "ggml-small.bin", 487_601_967,
            "Better for accents and other languages, about three times slower than Fast."),
        new("large-v3-turbo", "Accurate (recommended)", "ggml-large-v3-turbo-q5_0.bin", 574_041_195,
            "Best for Urdu, Hindi and noisy recordings. Slower; works on any modern computer."),
    ];

    public static WhisperModel Recommended => All[^1];

    public static WhisperModel? Find(string id) => All.FirstOrDefault(m => m.Id == id);
}

/// <summary>
/// Runs whisper.cpp (MIT licence) on this computer: no internet and no upload. FFmpeg first turns the
/// clip's sound into 16 kHz mono, then whisper-cli writes the words with their times as JSON.
/// </summary>
public sealed partial class WhisperRunner
{
    public WhisperRunner(string cliPath, string modelsFolder)
    {
        CliPath = cliPath;
        ModelsFolder = modelsFolder;
    }

    public string CliPath { get; }

    public string ModelsFolder { get; }

    /// <summary>Settings used when a call gives none.</summary>
    public WhisperOptions Options { get; set; } = WhisperOptions.Default;

    /// <summary>The Silero voice activity model (under 1 MB, MIT licence) next to whisper-cli, if installed.</summary>
    public string? VadModelPath { get; set; }

    /// <summary>Where the last run ran: "processor", or the graphics chip's name.</summary>
    public string LastDevice { get; private set; } = "processor";

    /// <summary>
    /// Finds whisper-cli: the WHISPER_CLI environment variable, then a "whisper" folder next to the program.
    /// Returns null when it is not installed.
    /// </summary>
    public static WhisperRunner? Locate(string modelsFolder)
    {
        var exe = OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli";
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("WHISPER_CLI") is { Length: > 0 } env)
        {
            candidates.Add(env);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "whisper", exe));
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null)
        {
            return null;
        }

        var vad = Environment.GetEnvironmentVariable("WHISPER_VAD_MODEL") is { Length: > 0 } envVad && File.Exists(envVad)
            ? envVad
            : new[] { Path.GetDirectoryName(found)!, modelsFolder }
                .Where(Directory.Exists)
                .SelectMany(d => Directory.EnumerateFiles(d, "ggml-silero*.bin"))
                .FirstOrDefault();
        return new WhisperRunner(found, modelsFolder) { VadModelPath = vad };
    }

    public string ModelPath(WhisperModel model) => Path.Combine(ModelsFolder, model.FileName);

    public bool HasModel(WhisperModel model) =>
        File.Exists(ModelPath(model)) && new FileInfo(ModelPath(model)).Length == model.Bytes;

    /// <summary>The models already downloaded, best first.</summary>
    public IReadOnlyList<WhisperModel> InstalledModels() => WhisperModel.All.Reverse().Where(HasModel).ToList();

    /// <summary>Downloads a model once (resuming is not needed: it is written to a temporary file first).</summary>
    public async Task DownloadAsync(HttpClient http, WhisperModel model, IProgress<double>? progress = null, CancellationToken token = default)
    {
        Directory.CreateDirectory(ModelsFolder);
        var target = ModelPath(model);
        var temp = target + ".download";
        using (var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new WhisperException($"The speech model could not be downloaded ({(int)response.StatusCode}). Check the internet connection and try again.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var file = File.Create(temp);
            var buffer = new byte[1 << 16];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                total += read;
                progress?.Report(Math.Min(1, total / (double)model.Bytes));
            }
        }

        if (new FileInfo(temp).Length != model.Bytes)
        {
            File.Delete(temp);
            throw new WhisperException("The speech model download was incomplete. Try again.");
        }

        File.Move(temp, target, overwrite: true);
    }

    /// <summary>
    /// Transcribes part of a media file. Word times are in file seconds. <paramref name="language"/> is a
    /// Whisper code such as en, hi or ur, or "auto".
    /// </summary>
    public async Task<Transcript> TranscribeAsync(FfmpegTools ffmpeg, WhisperModel model, string mediaPath, string language,
        double start = 0, double? length = null, IProgress<double>? progress = null, WhisperOptions? options = null, double audioOffset = 0,
        CancellationToken token = default)
    {
        options ??= Options;
        if (!HasModel(model))
        {
            throw new WhisperException($"The {model.Name} speech model is not downloaded yet.");
        }

        var work = Path.Combine(Path.GetTempPath(), "ave-whisper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // 1. The sound as 16 kHz mono WAV, which is what Whisper reads.
            var wav = Path.Combine(work, "speech.wav");
            var extract = new List<string> { "-hide_banner", "-nostdin", "-loglevel", "error", "-y" };
            if (start > 0)
            {
                extract.AddRange(["-ss", Num(start)]);
            }

            extract.AddRange(["-i", mediaPath, "-vn"]);
            if (Rendering.AudioAlignment.Align(audioOffset, start) is { } delay)
            {
                // Sound stored later than the picture: keep word times in picture time.
                extract.AddRange(["-af", delay]);
            }
            if (length is { } l)
            {
                extract.AddRange(["-t", Num(l)]);
            }

            extract.AddRange(["-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", wav]);
            var (code, error) = await RunAsync(ffmpeg.FfmpegPath, extract, null, token).ConfigureAwait(false);
            if (code != 0 || !File.Exists(wav))
            {
                throw new WhisperException("The sound could not be read for transcription: " + error.Trim());
            }

            // 2. Whisper, one word per segment.
            var output = Path.Combine(work, "words");
            var args = new List<string>
            {
                "-m", ModelPath(model), "-f", wav, "-l", language, "-ml", "1", "-sow", "-oj", "-of", output, "-pp",
                "-t", options.EffectiveThreads.ToString(CultureInfo.InvariantCulture),
                "-bs", Math.Clamp(options.BeamSize, 1, 8).ToString(CultureInfo.InvariantCulture),
                "-bo", Math.Clamp(options.BeamSize, 1, 8).ToString(CultureInfo.InvariantCulture),
            };
            if (!options.UseGpu)
            {
                args.Add("-ng");
            }

            if (options.SkipSilence && VadModelPath is { } vad)
            {
                args.AddRange(["--vad", "-vm", vad]);
            }
            if (InitialPrompt(language) is { } prompt)
            {
                args.AddRange(["--prompt", prompt]);
            }

            var deviceLines = new List<string>();
            (code, error) = await RunAsync(CliPath, args, line =>
            {
                if (line.Contains("whisper_backend_init_gpu", StringComparison.Ordinal) || line.StartsWith("ggml_vulkan: ", StringComparison.Ordinal))
                {
                    lock (deviceLines)
                    {
                        deviceLines.Add(line);
                    }
                }

                var match = ProgressPattern().Match(line);
                if (match.Success)
                {
                    progress?.Report(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) / 100.0);
                }
            }, token).ConfigureAwait(false);
            var json = output + ".json";
            if ((code != 0 || !File.Exists(json)) && options.UseGpu)
            {
                // A graphics driver problem must never stop transcription: try again on the processor.
                return await TranscribeAsync(ffmpeg, model, mediaPath, language, start, length, progress, options with { UseGpu = false }, audioOffset, token)
                    .ConfigureAwait(false);
            }

            if (code != 0 || !File.Exists(json))
            {
                throw new WhisperException("Speech recognition failed: " + Tail(error));
            }

            LastDevice = DeviceFrom(deviceLines);

            var transcript = Parse(await File.ReadAllTextAsync(json, Encoding.UTF8, token).ConfigureAwait(false), start);
            transcript.MediaPath = mediaPath;
            transcript.Model = model.Id;
            if (language != "auto" || transcript.Language.Length == 0)
            {
                transcript.Language = language == "auto" ? "" : language;
            }

            // 3. Tighten word edges to where the sound really is.
            var analysis = SpeechDetector.Analyse(ffmpeg, wav, 0, length ?? 36000, minimumPause: 0.15).Shift(start);
            TranscriptTiming.Refine(transcript.Words, analysis);
            return transcript;
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Transcribes only <paramref name="parts"/> of a file (file time) and adds the words to a copy of
    /// <paramref name="existing"/> (or a new transcript). Whisper also hears a second either side of each part
    /// so words at the edges come out whole; only the words inside the part are kept.
    /// </summary>
    public async Task<Transcript> TranscribePartsAsync(FfmpegTools ffmpeg, WhisperModel model, string mediaPath, string language,
        IReadOnlyList<Audio.TimeStretch> parts, Transcript? existing = null, double audioOffset = 0, IProgress<double>? progress = null,
        WhisperOptions? options = null, CancellationToken token = default)
    {
        var result = existing?.Clone() ?? new Transcript { MediaPath = mediaPath, Language = language == "auto" ? "" : language };
        var total = Math.Max(0.001, TranscriptCoverage.Seconds(parts));
        var done = 0.0;
        foreach (var part in parts)
        {
            token.ThrowIfCancellationRequested();
            var from = Math.Max(0, part.Start - TranscriptCoverage.Context);
            var to = part.End + TranscriptCoverage.Context;
            var before = done;
            var heard = await TranscribeAsync(ffmpeg, model, mediaPath, language, from, to - from,
                progress is null ? null : new Progress<double>(p => progress.Report((before + p * part.Duration) / total)),
                options, audioOffset, token).ConfigureAwait(false);
            TranscriptCoverage.Merge(result, heard, part);
            done += part.Duration;
            progress?.Report(done / total);
        }

        result.MediaPath = mediaPath;
        return result;
    }

    /// <summary>
    /// A short prompt in the right script. Without it, Whisper sometimes writes Hindi speech in Urdu letters
    /// (they sound alike). Urdu gets no prompt: in tests it made Whisper stop early.
    /// </summary>
    internal static string? InitialPrompt(string language) => language switch
    {
        "hi" => "नमस्ते। यह हिंदी में है।",
        _ => null,
    };

    /// <summary>Reads whisper-cli's JSON (one word per segment). <paramref name="offset"/> is added to all times.</summary>
    public static Transcript Parse(string json, double offset = 0)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var transcript = new Transcript();
        if (root.TryGetProperty("result", out var result) && result.TryGetProperty("language", out var lang))
        {
            transcript.Language = lang.GetString() ?? "";
        }

        if (!root.TryGetProperty("transcription", out var segments))
        {
            return transcript;
        }

        foreach (var segment in segments.EnumerateArray())
        {
            var text = (segment.GetProperty("text").GetString() ?? "").Trim();
            if (text.Length == 0 || text.StartsWith("[_", StringComparison.Ordinal) || text is "[BLANK_AUDIO]" or "[Music]")
            {
                continue;
            }

            var offsets = segment.GetProperty("offsets");
            var from = offsets.GetProperty("from").GetDouble() / 1000 + offset;
            var to = offsets.GetProperty("to").GetDouble() / 1000 + offset;
            if (transcript.Words.Count > 0 && IsPunctuationOnly(text))
            {
                transcript.Words[^1].Text += text;
                continue;
            }

            transcript.Words.Add(new TranscriptWord { Text = text, Start = from, End = Math.Max(from, to) });
        }

        return transcript;
    }

    private static bool IsPunctuationOnly(string text) => text.All(c => char.IsPunctuation(c) || char.IsSymbol(c));

    private static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Tail(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", lines.TakeLast(3));
    }

    private static async Task<(int Code, string Errors)> RunAsync(string exe, IEnumerable<string> args, Action<string>? onLine, CancellationToken token)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        var errors = new StringBuilder();
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            onLine?.Invoke(e.Data);
            lock (errors)
            {
                if (errors.Length < 20_000)
                {
                    errors.AppendLine(e.Data);
                }
            }
        };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onLine?.Invoke(e.Data);
            }
        };
        if (!process.Start())
        {
            throw new WhisperException($"Could not start {Path.GetFileName(exe)}.");
        }

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        lock (errors)
        {
            return (process.ExitCode, errors.ToString());
        }
    }

    [GeneratedRegex(@"progress\s*=\s*(\d+)%")]
    private static partial Regex ProgressPattern();

    /// <summary>whisper.cpp says "whisper_backend_init_gpu: using Vulkan0 backend" when a graphics chip is used.</summary>
    [GeneratedRegex(@"whisper_backend_init_gpu: using (.+?) backend")]
    private static partial Regex DevicePattern();

    /// <summary>"ggml_vulkan: 0 = Intel(R) Iris(R) Xe Graphics (Intel Corporation) | uma: 1 | ..." names Vulkan device 0.</summary>
    [GeneratedRegex(@"^ggml_vulkan: (\d+) = (.+?)(?: \(|\s*\|)")]
    private static partial Regex VulkanDevicePattern();

    /// <summary>The device whisper.cpp's log says it used, as <see cref="LastDevice"/> reports it.</summary>
    internal static string DeviceFrom(IEnumerable<string> lines)
    {
        var device = "processor";
        var names = new Dictionary<string, string>();
        foreach (var line in lines)
        {
            if (VulkanDevicePattern().Match(line) is { Success: true } listed)
            {
                names["Vulkan" + listed.Groups[1].Value] = listed.Groups[2].Value.Replace("(R)", "").Replace("(TM)", "").Trim();
            }

            if (DevicePattern().Match(line) is { Success: true } used)
            {
                var id = used.Groups[1].Value.Trim();
                device = names.TryGetValue(id, out var name) ? name : id;
            }
        }

        return device;
    }
}
