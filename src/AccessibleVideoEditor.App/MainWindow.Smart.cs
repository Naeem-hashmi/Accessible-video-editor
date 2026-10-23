using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AccessibleVideoEditor.App.Dialogs;
using AccessibleVideoEditor.App.Services;
using AccessibleVideoEditor.Core.Audio;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Text;
using Microsoft.Win32;

namespace AccessibleVideoEditor.App;

/// <summary>
/// Smart tools: Quick video, speech to text, editing by transcript, captions, pauses, beats and ducking.
/// Each one says what it found before changing anything, and every change is one undoable step.
/// </summary>
public partial class MainWindow
{
    /// <summary>The installed whisper-cli with the user's speed settings, or null when it is not installed.</summary>
    private WhisperRunner? Whisper
    {
        get
        {
            var runner = WhisperRunner.Locate(AppPaths.WhisperModels);
            if (runner is not null)
            {
                runner.Options = _settings.WhisperOptions;
            }

            return runner;
        }
    }

    /// <summary>Runs slow work off the window thread, with a progress window that can cancel it.</summary>
    private async Task<T?> WithProgressAsync<T>(string title, Func<ProgressWindow, CancellationToken, Task<T>> work)
    {
        using var cancel = new CancellationTokenSource();
        var progress = new ProgressWindow(this, title, text => Say(text, interrupt: false));
        progress.Cancelled += () => cancel.Cancel();
        progress.Show();
        try
        {
            return await work(progress, cancel.Token);
        }
        catch (OperationCanceledException)
        {
            Say("Cancelled.");
            return default;
        }
        finally
        {
            progress.Finish();
            Activate();
        }
    }

    private bool NeedTools(out FfmpegTools tools)
    {
        tools = Tools!;
        if (Tools is null)
        {
            _earcons.Play(EarconEvent.Error);
            Say("FFmpeg is missing, so sound can't be analysed. Reinstall ClipScribe.");
            return false;
        }

        return true;
    }

    private (Layer Layer, int Index, Item Item)? FocusedClip(string why)
    {
        if (_editor.FocusedLayer is { } layer && _editor.FocusedItemIndex is var index and >= 0 &&
            layer.Items[index] is { HasAudio: true, Kind: ItemKind.Video or ItemKind.Audio } item)
        {
            return (layer, index, item);
        }

        _earcons.Play(EarconEvent.Error);
        Say($"Go to a video or sound clip first, to {why}.");
        return null;
    }

    // ------------------------------------------------------------------ speech to text

    /// <summary>Chooses a model (downloading it once if needed). Null if the user cancels.</summary>
    private async Task<WhisperModel?> ChooseModelAsync(WhisperRunner whisper, string language)
    {
        var installed = whisper.InstalledModels();
        var options = WhisperModel.All.Reverse().Select(m =>
            $"{m.Spoken}{(whisper.HasModel(m) ? " Downloaded." : $" Downloads {m.SizeText} once, then works offline.")}").ToList();
        var form = new FormDialog(this, "Speech model");
        form.AddIntro("Speech recognition runs on this computer. Nothing is uploaded. Choose how accurate it should be.");
        var preferred = installed.FirstOrDefault() ?? WhisperModel.Recommended;
        var choice = form.AddChoice("Model", options, WhisperModel.All.Reverse().ToList().IndexOf(preferred));
        if (form.ShowDialog() != true)
        {
            return null;
        }

        var model = WhisperModel.All.Reverse().ToList()[choice()];
        if (model.Id == "base" && language is "ur" or "hi")
        {
            var answer = MessageBox.Show(this,
                "The Fast model often gets Urdu and Hindi words wrong. Accurate is recommended for them. Use Fast anyway?",
                "Speech model", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return null;
            }
        }

        return whisper.HasModel(model) || await DownloadModelAsync(whisper, model) ? model : null;
    }

    /// <summary>Downloads a speech model once, with progress. False if it failed or was cancelled.</summary>
    private async Task<bool> DownloadModelAsync(WhisperRunner whisper, WhisperModel model)
    {
        var ok = await WithProgressAsync($"Downloading the {model.Name} speech model, {model.SizeText}", async (progress, token) =>
        {
            await whisper.DownloadAsync(Http, model, new Progress<double>(progress.Report), token);
            return true;
        });
        if (!ok || !whisper.HasModel(model))
        {
            return false;
        }

        Say($"{model.Name} speech model downloaded. It now works without internet.");
        return true;
    }

    /// <summary>One file to transcribe: which parts, and the words it already has outside them.</summary>
    private sealed record TranscribeJob(string Path, double Offset, Transcript? Existing, IReadOnlyList<TimeStretch> Parts);

    /// <summary>Set while a draft is being improved in the background.</summary>
    private CancellationTokenSource? _improving;
    private double _improvingDone;

    private async Task TranscribeAsync()
    {
        if (!NeedTools(out var tools) || _editor.FocusedLayer is not { } layer)
        {
            return;
        }

        if (Whisper is not { } whisper)
        {
            _earcons.Play(EarconEvent.Error);
            Say("Speech recognition is not installed. Reinstall ClipScribe to add it.");
            return;
        }

        var clips = layer.Items.Where(i => i.HasAudio && i.Kind is ItemKind.Video or ItemKind.Audio)
            .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        if (clips.Count == 0)
        {
            _earcons.Play(EarconEvent.Error);
            Say($"{layer.Name} has no clips with sound to transcribe.");
            return;
        }

        if (_improving is not null)
        {
            var answer = MessageBox.Show(this,
                $"The transcript is still being improved in the background ({_improvingDone:P0} done). Stop that and transcribe again?",
                "Transcribe speech", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            _improving.Cancel();
        }

        var form = new FormDialog(this, "Transcribe speech");
        form.AddIntro($"Write down what is said in {clips.Count} {(clips.Count == 1 ? "file" : "files")} on {layer.Name}. For Urdu and Hindi choose the language: automatic detection can mix them up.");
        var languages = Transcript.Languages;
        var lastLanguage = Math.Max(0, languages.ToList().FindIndex(l => l.Code == _settings.SpeechLanguage));
        var language = form.AddChoice("Language spoken", languages.Select(l => l.Name).ToList(), lastLanguage);
        var scope = form.AddChoice("What to transcribe",
        [
            "Only the parts used on the timeline, skipping parts already done (fastest)",
            "Only the parts used on the timeline, all again",
            "The whole files",
        ], 0, "Parts of a file that no clip plays are not needed for captions or for editing by text.");
        var draft = form.AddCheck("Quick draft first, then improve it in the background", _settings.WhisperDraftFirst,
            "The Fast model writes a draft in a fraction of the time so you can start working; the chosen model then replaces it while you edit.");
        if (form.ShowDialog() != true)
        {
            return;
        }

        var code = languages[language()].Code;
        _settings.SpeechLanguage = code;
        _settings.WhisperDraftFirst = draft();
        if (await ChooseModelAsync(whisper, code) is not { } model)
        {
            return;
        }

        var jobs = new List<TranscribeJob>();
        foreach (var clip in clips)
        {
            var existing = _editor.Project.TranscriptFor(clip.Path);
            if (existing is not null && code != "auto" && existing.Language.Length > 0 && existing.Language != code)
            {
                existing = null; // Another language: start again.
            }

            var used = TranscriptCoverage.Used(_editor.Project, clip.Path);
            var job = scope() switch
            {
                0 => new TranscribeJob(clip.Path, clip.AudioOffset, existing, TranscriptCoverage.Missing(existing, used)),
                1 => new TranscribeJob(clip.Path, clip.AudioOffset, existing, used),
                _ => new TranscribeJob(clip.Path, clip.AudioOffset, null, TranscriptCoverage.Whole(clip.SourceDuration)),
            };
            if (job.Parts.Count > 0)
            {
                jobs.Add(job);
            }
        }

        if (jobs.Count == 0)
        {
            _earcons.Play(EarconEvent.Error);
            Say($"Everything used on {layer.Name} is already transcribed. To do it again, choose \"all again\" under What to transcribe.");
            return;
        }

        var fast = WhisperModel.All[0];
        var useDraft = draft() && model != fast;
        if (useDraft && !whisper.HasModel(fast))
        {
            var answer = MessageBox.Show(this,
                $"A quick draft needs the Fast model, a one-time download of {fast.SizeText}. Download it now? Choose No to transcribe with {model.Name} only.",
                "Quick draft", MessageBoxButton.YesNo, MessageBoxImage.Question);
            useDraft = answer == MessageBoxResult.Yes && await DownloadModelAsync(whisper, fast);
        }

        var first = useDraft ? fast : model;
        var seconds = jobs.Sum(j => TranscriptCoverage.Seconds(j.Parts));
        var transcripts = await WithProgressAsync(useDraft ? "Writing a quick draft" : "Transcribing", (progress, token) =>
            RunJobsAsync(whisper, tools, first, code, jobs, new Progress<double>(progress.Report), progress.SetStatus, token));
        if (transcripts is null)
        {
            return;
        }

        var words = transcripts.Sum(t => t.Words.Count);
        var spoken = Transcript.LanguageName(transcripts.Select(t => t.Language).FirstOrDefault(l => l.Length > 0) ?? code);
        var minutes = SpokenLength(seconds);
        var feedback = _editor.Edit(useDraft ? "transcribe draft" : "transcribe", project =>
        {
            foreach (var transcript in transcripts)
            {
                project.Transcripts.RemoveAll(t => string.Equals(t.MediaPath, transcript.MediaPath, StringComparison.OrdinalIgnoreCase));
                project.Transcripts.Add(transcript);
            }

            if (words == 0)
            {
                return EditResult.Fail("No speech was found.");
            }

            var next = useDraft
                ? $"This is a quick draft; the {model.Name} model is improving it in the background and will replace it when done. "
                : "";
            return EditResult.Success($"Transcribed {minutes} of sound: {words} words in {spoken}. {next}Control Shift X opens the transcript to edit by text; Control Shift G makes captions.");
        });
        await ApplyAsync(feedback);
        if (useDraft && words > 0)
        {
            _ = ImproveInBackgroundAsync(whisper, tools, model, code, jobs, transcripts);
        }
    }

    /// <summary>Transcribes each job's parts and returns the finished transcripts, in job order.</summary>
    private static async Task<List<Transcript>> RunJobsAsync(WhisperRunner whisper, FfmpegTools tools, WhisperModel model, string language,
        IReadOnlyList<TranscribeJob> jobs, IProgress<double> progress, Action<string>? status, CancellationToken token)
    {
        var total = Math.Max(0.001, jobs.Sum(j => TranscriptCoverage.Seconds(j.Parts)));
        var done = 0.0;
        var results = new List<Transcript>();
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var length = TranscriptCoverage.Seconds(job.Parts);
            status?.Invoke($"Transcribing {i + 1} of {jobs.Count}: {Path.GetFileName(job.Path)}, {SpokenLength(length)}");
            var before = done;
            results.Add(await whisper.TranscribePartsAsync(tools, model, job.Path, language, job.Parts, job.Existing, job.Offset,
                new Progress<double>(p => progress.Report((before + p * length) / total)), token: token));
            done += length;
        }

        return results;
    }

    /// <summary>
    /// Runs the chosen model over the same parts while the user keeps working, then swaps in its words for
    /// every file whose draft was not corrected in the meantime. Progress shows in the status bar.
    /// </summary>
    private async Task ImproveInBackgroundAsync(WhisperRunner whisper, FfmpegTools tools, WhisperModel model, string language,
        IReadOnlyList<TranscribeJob> jobs, IReadOnlyList<Transcript> drafts)
    {
        using var cancel = new CancellationTokenSource();
        _improving = cancel;
        _improvingDone = 0;
        BackgroundText.Text = $"Improving transcript with {model.Name}: 0%";
        try
        {
            var progress = new Progress<double>(p =>
            {
                _improvingDone = p;
                BackgroundText.Text = $"Improving transcript with {model.Name}: {p:P0}";
            });
            var better = await RunJobsAsync(whisper, tools, model, language, jobs, progress, null, cancel.Token);
            var replace = new List<Transcript>();
            var kept = new List<string>();
            for (var i = 0; i < better.Count; i++)
            {
                var now = _editor.Project.TranscriptFor(better[i].MediaPath);
                if (now is not null && now.SameWords(drafts[i]))
                {
                    replace.Add(better[i]);
                }
                else
                {
                    kept.Add(Path.GetFileName(better[i].MediaPath));
                }
            }

            var note = kept.Count > 0 ? $" {string.Join(", ", kept)} changed since the draft, so your version was kept." : "";
            if (replace.Count == 0)
            {
                Say($"The {model.Name} transcript is ready.{note}");
                return;
            }

            var feedback = _editor.Edit("improve transcript", project =>
            {
                foreach (var transcript in replace)
                {
                    project.Transcripts.RemoveAll(t => string.Equals(t.MediaPath, transcript.MediaPath, StringComparison.OrdinalIgnoreCase));
                    project.Transcripts.Add(transcript);
                }

                return EditResult.Success($"Transcript improved with the {model.Name} model: {replace.Sum(t => t.Words.Count)} words.{note} Control Z puts the draft back.");
            });
            await ApplyAsync(feedback);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WhisperException ex)
        {
            AppPaths.Log("Improving the transcript failed: " + ex.Message);
            Say("Improving the transcript failed, so the quick draft stays. " + ex.Message, interrupt: false);
        }
        finally
        {
            if (_improving == cancel)
            {
                _improving = null;
                BackgroundText.Text = "";
            }
        }
    }

    private static string SpokenLength(double seconds) =>
        seconds >= 90 ? $"{Math.Round(seconds / 60):0} minutes" : $"{Math.Max(1, Math.Round(seconds)):0} seconds";

    private async Task MakeCaptionsAsync()
    {
        if (_editor.FocusedLayer is not { } layer)
        {
            return;
        }

        var speechLayer = layer.Name == Captions.LayerName
            ? _editor.Project.Layers.FirstOrDefault(l => l.Items.Any(i => _editor.Project.TranscriptFor(i.Path) is not null)) ?? layer
            : layer;
        await ApplyAsync(_editor.Edit("make captions", project =>
            Captions.Create(project, project.FindLayer(speechLayer.Id)!)));
    }

    // ------------------------------------------------------------------ edit by transcript

    private async Task TranscriptEditorAsync()
    {
        if (_editor.FocusedLayer is not { } focused)
        {
            return;
        }

        var layerId = focused.Id;
        var document = TranscriptEditing.Build(_editor.Project, focused);
        if (document.IsEmpty)
        {
            _earcons.Play(EarconEvent.Error);
            Say($"{focused.Name} has no transcript yet. Press Control Shift W to transcribe it.");
            return;
        }

        var window = new Window
        {
            Title = $"Transcript of {focused.Name}",
            Owner = this,
            Width = 760,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontSize = 16,
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Text = "One sentence per line. In Cut mode, delete words or lines and press Apply: those parts leave the video. In Fix text mode, correct words for the captions; the video stays the same. Control Space plays from the word at the cursor.",
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var modeLabel = new Label { Content = "_Mode:", Padding = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var mode = new ComboBox { MinWidth = 320, ItemsSource = new[] { "Cut: deleting words cuts the video", "Fix text: correct words, keep the video" }, SelectedIndex = 0 };
        modeLabel.Target = mode;
        AutomationProperties.SetName(mode, "Mode");
        var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        modeRow.Children.Add(modeLabel);
        modeRow.Children.Add(mode);
        DockPanel.SetDock(modeRow, Dock.Top);
        root.Children.Add(modeRow);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var textLabel = new Label { Content = "_Transcript:", Padding = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(textLabel, Dock.Top);
        root.Children.Add(textLabel);
        var text = new TextBox
        {
            Text = document.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = 18,
            FlowDirection = document.Entries.Any(e => e.Word.Text.Any(c => c is >= '؀' and <= 'ۿ')) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
        };
        textLabel.Target = text;
        AutomationProperties.SetName(text, "Transcript");
        root.Children.Add(text);

        Button MakeButton(string label, Action click, bool isDefault = false, bool isCancel = false)
        {
            var b = new Button { Content = label, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4), IsDefault = isDefault, IsCancel = isCancel };
            AutomationProperties.SetName(b, label.Replace("_", ""));
            b.Click += (_, _) => click();
            buttons.Children.Add(b);
            return b;
        }

        void PlayFromCaret()
        {
            // The word under the cursor: count words in the text before the caret.
            var before = text.Text[..Math.Min(text.CaretIndex, text.Text.Length)];
            var index = Math.Clamp(TranscriptEditing.Tokenize(before).Count - (before.Length > 0 && !char.IsWhiteSpace(before[^1]) ? 1 : 0), 0, document.Entries.Count - 1);
            if (_editor.Project.FindLayer(layerId) is not { } layer || document.Entries.Count == 0)
            {
                return;
            }

            var entry = document.Entries[index];
            var item = layer.Items[entry.ItemIndex];
            var time = Timeline.Span(layer, entry.ItemIndex).Start + Math.Max(0, item.ToTimeline(entry.Word.Start));
            _ = PlayFromAsync(time);
            Say($"Playing from {entry.Word.Text}.", interrupt: false);
        }

        void Apply()
        {
            var chosen = mode.SelectedIndex == 1 ? TranscriptMode.FixText : TranscriptMode.Cut;
            var edited = text.Text;
            var feedback = _editor.Edit(chosen == TranscriptMode.Cut ? "cut by transcript" : "fix transcript", project =>
            {
                var layer = project.FindLayer(layerId);
                if (layer is null)
                {
                    return EditResult.Fail("That layer no longer exists.");
                }

                return TranscriptEditing.Apply(project, layer, TranscriptEditing.Build(project, layer), edited, chosen);
            }, chosen == TranscriptMode.Cut ? EarconEvent.Deleted : EarconEvent.Done);
            _ = ApplyAsync(feedback);
            if (_editor.Project.FindLayer(layerId) is { } now)
            {
                document = TranscriptEditing.Build(_editor.Project, now);
                var caret = text.CaretIndex;
                text.Text = document.Text;
                text.CaretIndex = Math.Min(caret, text.Text.Length);
            }
        }

        MakeButton("_Play from cursor", PlayFromCaret);
        MakeButton("_Apply", Apply, isDefault: false);
        MakeButton("_Make captions", () => _ = MakeCaptionsAsync());
        MakeButton("Close", window.Close, isCancel: true);
        text.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                PlayFromCaret();
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                Apply();
            }
        };
        window.Content = root;
        window.Loaded += (_, _) => text.Focus();
        window.Closed += (_, _) => _preview.Pause();
        window.ShowDialog();
        await Task.CompletedTask;
    }

    private async Task PlayFromAsync(double time)
    {
        _preview.Pause();
        _editor.SetPlayheadFromPlayback(time);
        var error = await _preview.PlayAsync(_editor.Project, RenderScope.AllLayers, time);
        if (error is not null)
        {
            Say(error);
        }
    }

    // ------------------------------------------------------------------ pauses

    private async Task ShortenPausesAsync()
    {
        if (!NeedTools(out var tools) || FocusedClip("shorten its pauses") is not var (layer, index, item))
        {
            return;
        }

        var analysis = await WithProgressAsync("Finding pauses", (_, token) =>
            Task.Run(() => SpeechDetector.Analyse(tools, item.Path, item.SourceIn, item.SourceLength, minimumPause: 0.4, audioOffset: item.AudioOffset).Shift(item.SourceIn), token));
        if (analysis is null)
        {
            return;
        }

        var form = new FormDialog(this, "Shorten pauses");
        var longOnes = analysis.Pauses.Where(p => p.Duration > 0.7).ToList();
        form.AddIntro(longOnes.Count == 0
            ? $"No pauses longer than 0.7 seconds were found in {item.Name}."
            : $"{item.Name} has {longOnes.Count} pauses longer than 0.7 seconds, {TimeFormat.Words(longOnes.Sum(p => p.Duration))} in all." +
              (analysis.IsNoisy ? " The recording is noisy, so listen to the result." : ""));
        var longer = form.AddTime("Shorten pauses longer than", 0.7, minimum: 0.2);
        var keep = form.AddChoice("Keep of each pause", ["0.3 seconds (natural)", "0.15 seconds (tight)", "Remove completely (0.05 seconds)"], 0);
        if (longOnes.Count == 0 || form.ShowDialog() != true)
        {
            if (longOnes.Count == 0)
            {
                Say($"No long pauses in {item.Name}.");
            }

            return;
        }

        var kept = keep() switch { 0 => 0.3, 1 => 0.15, _ => 0.05 };
        var cuts = CutEditing.PauseCuts(analysis, kept, longer());
        var itemId = item.Id;
        await ApplyAsync(_editor.Edit("shorten pauses", project =>
        {
            var target = project.FindLayer(layer.Id);
            var at = target?.IndexOf(itemId) ?? -1;
            return target is null || at < 0
                ? EditResult.Fail("That clip no longer exists.")
                : CutEditing.RemoveSourceRanges(target, at, cuts, project.FrameRate, "pauses");
        }, EarconEvent.Deleted));
    }

    // ------------------------------------------------------------------ beats

    private async Task FindBeatsAsync()
    {
        if (!NeedTools(out var tools) || FocusedClip("find its beats") is not var (layer, index, item))
        {
            return;
        }

        var length = item.SourceDuration > 0 ? Math.Min(600, item.SourceDuration) : 600;
        var analysis = await WithProgressAsync("Finding beats", (_, token) => Task.Run(() => BeatDetector.Analyse(tools, item.Path, 0, length), token));
        if (analysis is null)
        {
            return;
        }

        var itemId = item.Id;
        await ApplyAsync(_editor.Edit("find beats", project =>
        {
            var target = project.FindLayer(layer.Id);
            var at = target?.IndexOf(itemId) ?? -1;
            if (target is null || at < 0)
            {
                return EditResult.Fail("That clip no longer exists.");
            }

            var result = BeatEditing.SetBeats(target, at, analysis);
            return result.Ok ? EditResult.Success(result.Message + " Shift Left and Shift Right jump between beats; Control Shift U cuts a layer to the beat.", result.FocusItemId) : result;
        }));
    }

    private async Task CutToBeatsAsync()
    {
        if (_editor.FocusedLayer is not { } layer)
        {
            return;
        }

        if (BeatEditing.BeatTimes(_editor.Project).Count < 2)
        {
            _earcons.Play(EarconEvent.Error);
            Say("There are no beats yet. Go to the music and press Control Shift B to find its beats.");
            return;
        }

        var form = new FormDialog(this, "Cut to the beat");
        form.AddIntro($"Make every item on {layer.Name} end on a beat. Clips are never made longer than their file.");
        var every = form.AddChoice("Pictures change every", ["1 beat (very fast)", "2 beats", "4 beats (one bar)", "8 beats (two bars)"], 2);
        if (form.ShowDialog() != true)
        {
            return;
        }

        var beats = every() switch { 0 => 1, 1 => 2, 2 => 4, _ => 8 };
        await ApplyAsync(_editor.Edit("cut to the beat", project => BeatEditing.CutToBeats(project, project.FindLayer(layer.Id)!, beats)));
    }

    // ------------------------------------------------------------------ ducking

    private async Task AutoDuckAsync()
    {
        if (!NeedTools(out var tools) || _editor.FocusedLayer is not { } layer)
        {
            return;
        }

        if (layer.Kind != LayerKind.Audio)
        {
            _earcons.Play(EarconEvent.Error);
            Say("Go to the music layer first. Auto-ducking makes that layer quieter when someone speaks on the others.");
            return;
        }

        var form = new FormDialog(this, "Lower music under speech");
        var has = layer.Items.Sum(i => i.VolumeDips.Count);
        form.AddIntro($"Find speech on the other layers and make {layer.Name} quieter there, with smooth fades." +
                      (has > 0 ? $" It is already lowered in {has} places; this replaces that." : ""));
        var amount = form.AddChoice("Lower by", ["6 decibels (a little)", "12 decibels (recommended)", "18 decibels (a lot)", "Remove ducking (full volume)"], 1);
        if (form.ShowDialog() != true)
        {
            return;
        }

        if (amount() == 3)
        {
            await ApplyAsync(_editor.Edit("remove ducking", project => Ducking.Clear(project.FindLayer(layer.Id)!)));
            return;
        }

        var level = amount() switch { 0 => 0.5, 1 => 0.25, _ => 0.125 };
        var project = _editor.Project;
        var speech = await WithProgressAsync("Finding speech", (_, token) => Task.Run(() => Ducking.FindSpeech(tools, project, layer, token), token));
        if (speech is null)
        {
            return;
        }

        await ApplyAsync(_editor.Edit("lower music under speech", p => Ducking.Apply(p.FindLayer(layer.Id)!, speech, level)));
    }

    // ------------------------------------------------------------------ quick video

    private async Task QuickVideoAsync()
    {
        if (!NeedTools(out var tools) || !ConfirmDiscardChanges("making a quick video"))
        {
            return;
        }

        // Step 1: the files.
        Say("Step 1 of 3: choose your videos and pictures.", interrupt: false);
        var open = new OpenFileDialog
        {
            Title = "Quick video, step 1 of 3: choose videos and pictures",
            Multiselect = true,
            Filter = "Videos and pictures|*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.heic|All files|*.*",
        };
        if (open.ShowDialog(this) != true || open.FileNames.Length == 0)
        {
            return;
        }

        var infos = new List<MediaInfo>();
        foreach (var path in open.FileNames)
        {
            try
            {
                infos.Add(await MediaProbe.ProbeAsync(tools, path));
            }
            catch (Exception ex) when (ex is IOException or RenderException or InvalidDataException)
            {
                Say($"{Path.GetFileName(path)} can't be used: {ex.Message}", interrupt: false);
            }
        }

        if (infos.Count == 0)
        {
            return;
        }

        // Steps 2 and 3: style, music, title, captions.
        var form = new FormDialog(this, "Quick video, steps 2 and 3");
        var pictures = infos.Count(i => i.Kind == ItemKind.Image);
        form.AddIntro($"{infos.Count} files: {infos.Count - pictures} videos and {pictures} pictures. Step 2: choose a style. Step 3: give it a title, then press OK.");
        var styles = Enum.GetValues<QuickStyle>().ToList();
        var anySpeech = infos.Any(i => i.Kind == ItemKind.Video && i.HasAudio);
        var style = form.AddChoice("Style", styles.Select(s => QuickStyleRules.For(s).Spoken).ToList(), anySpeech && pictures == 0 ? styles.IndexOf(QuickStyle.Talking) : 0);
        var musicChoices = new List<(string Name, string? Path)> { ("No music", null) };
        musicChoices.AddRange(SoundEffects.BuiltIn.Where(e => e.Category == "Music beds")
            .Select(e => ($"{e.Name}. {e.Description}", (string?)Path.Combine(AppPaths.BuiltInSoundEffects, e.FileName))));
        musicChoices.Add(("Choose a music file...", ""));
        var music = form.AddChoice("Music", musicChoices.Select(m => m.Name).ToList(), Math.Min(2, musicChoices.Count - 1));
        var title = form.AddText("Title", Path.GetFileNameWithoutExtension(infos[0].Path));
        var whisper = Whisper;
        var canCaption = whisper is not null && whisper.InstalledModels().Count > 0;
        var captions = form.AddCheck("Add captions from the speech", canCaption && anySpeech,
            canCaption ? "Speech is turned into captions on this computer." : "Transcribe once with Control Shift W to download a speech model; after that captions work here too.");
        if (form.ShowDialog() != true)
        {
            return;
        }

        var musicPath = musicChoices[music()].Path;
        if (musicPath == "")
        {
            var pick = new OpenFileDialog { Title = "Choose music", Filter = "Sound files|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg;*.opus|All files|*.*" };
            musicPath = pick.ShowDialog(this) == true ? pick.FileName : null;
        }

        var chosenStyle = styles[style()];
        // A real recorded whoosh if one is installed, otherwise the built-in one.
        var recordings = RecordedSounds.Load(AppPaths.Recordings);
        var whoosh = recordings.FirstOrDefault(s => s.Name == "Soft swipe") ?? recordings.FirstOrDefault(s => s.Category == "Whooshes and swipes");
        var whooshPath = whoosh is null ? Path.Combine(AppPaths.BuiltInSoundEffects, "Whoosh.wav") : RecordedSounds.FullPath(AppPaths.Recordings, whoosh);
        var whooshLength = whoosh is null ? 0.6 : Math.Min(0.8, whoosh.Seconds);
        var result = await WithProgressAsync("Making your video", async (progress, token) =>
        {
            progress.SetStatus("Listening to the clips and the music");
            var musicInfo = musicPath is null ? null : await MediaProbe.ProbeAsync(tools, musicPath);
            var transcripts = new List<Transcript>();
            if (captions() && whisper is not null && whisper.InstalledModels().FirstOrDefault() is { } model)
            {
                var speaking = infos.Where(i => i.Kind == ItemKind.Video && i.HasAudio).ToList();
                for (var i = 0; i < speaking.Count; i++)
                {
                    progress.SetStatus($"Writing captions, {i + 1} of {speaking.Count}");
                    transcripts.Add(await whisper.TranscribeAsync(tools, model, speaking[i].Path, _settings.SpeechLanguage, progress: new Progress<double>(progress.Report),
                        audioOffset: speaking[i].AudioOffset, token: token));
                }
            }

            progress.SetStatus("Putting it together");
            return await Task.Run(() =>
            {
                var request = QuickVideo.Analyse(tools, new QuickVideoRequest
                {
                    Files = infos, Style = chosenStyle, Title = title(), Music = musicInfo, Transcripts = transcripts, AddCaptions = captions(),
                    WhooshPath = File.Exists(whooshPath) ? whooshPath : null,
                    WhooshLength = whooshLength,
                }, token);
                return QuickVideo.Build(request);
            }, token);
        });
        var (project, summary) = result;
        if (project is null)
        {
            return; // cancelled
        }

        _shownLayerId = null;
        await ApplyAsync(_editor.Load(new ProjectSession(project), summary + " Press K to watch it, or Control E to export."));
    }
}
