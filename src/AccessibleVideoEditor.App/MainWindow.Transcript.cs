using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AccessibleVideoEditor.App.Dialogs;
using AccessibleVideoEditor.App.Services;
using AccessibleVideoEditor.Core.Ai;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Text;
using Microsoft.Win32;

namespace AccessibleVideoEditor.App;

/// <summary>
/// Transcript files and clean-up: export the words, import an edited copy, find repeats and fillers on this
/// computer, or ask an AI service. Every suggested cut is shown in a list to tick before anything changes.
/// </summary>
public partial class MainWindow
{
    /// <summary>The layer whose speech is meant: the focused one, or the speech under a Captions layer.</summary>
    private (Layer Layer, TranscriptDocument Document)? SpeechDocument()
    {
        if (_editor.FocusedLayer is not { } focused)
        {
            return null;
        }

        var layer = focused.Name == Captions.LayerName
            ? _editor.Project.Layers.FirstOrDefault(l => !TranscriptEditing.Build(_editor.Project, l).IsEmpty) ?? focused
            : focused;
        var document = TranscriptEditing.Build(_editor.Project, layer);
        if (document.IsEmpty)
        {
            _earcons.Play(EarconEvent.Error);
            Say($"{layer.Name} has no transcript yet. Press Control Shift W to transcribe it.");
            return null;
        }

        return (layer, document);
    }

    private async Task ExportTranscriptAsync()
    {
        if (SpeechDocument() is not var (layer, document))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export transcript",
            FileName = $"{Path.GetFileNameWithoutExtension(_editor.Session.FilePath ?? _editor.Project.Title)} transcript",
            Filter = "Plain text to edit (*.txt)|*.txt|SubRip captions (*.srt)|*.srt|WebVTT captions (*.vtt)|*.vtt",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var content = dialog.FilterIndex switch
        {
            2 => TranscriptCleanup.ExportCaptions(_editor.Project, layer, webVtt: false),
            3 => TranscriptCleanup.ExportCaptions(_editor.Project, layer, webVtt: true),
            _ => TranscriptCleanup.ExportText(document),
        };
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _earcons.Play(EarconEvent.Error);
            Say("The transcript could not be saved: " + ex.Message);
            return;
        }

        _earcons.Play(EarconEvent.Done);
        Say($"Saved {document.Entries.Count} words to {Path.GetFileName(dialog.FileName)}. Edit it anywhere, even in an AI chat: delete words to cut them and correct misheard words, " +
            "but don't move sentences around. Then Alt Shift I imports it.");
    }

    private async Task ImportTranscriptAsync()
    {
        if (SpeechDocument() is not var (layer, document))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import edited transcript",
            Filter = "Transcripts (*.txt;*.srt;*.vtt)|*.txt;*.srt;*.vtt|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string edited;
        try
        {
            edited = TranscriptCleanup.ImportText(await File.ReadAllTextAsync(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _earcons.Play(EarconEvent.Error);
            Say("The file could not be read: " + ex.Message);
            return;
        }

        var (deleted, changed) = TranscriptEditing.Compare(document, edited);
        if (deleted.Count == document.Entries.Count)
        {
            _earcons.Play(EarconEvent.Error);
            Say("None of the words in that file match this transcript. Choose the file exported from this project.");
            return;
        }

        if (deleted.Count == 0 && changed == 0)
        {
            Say("That file has no changes: every word is the same as in the transcript.");
            return;
        }

        // Each run of deleted words is one entry to tick.
        var cuts = new List<CutSuggestion>();
        foreach (var index in deleted)
        {
            if (cuts.Count > 0 && cuts[^1].Last == index - 1 && document.Entries[index].ItemIndex == document.Entries[cuts[^1].First].ItemIndex)
            {
                cuts[^1] = cuts[^1] with { Last = index };
            }
            else
            {
                cuts.Add(new CutSuggestion(index, index, "deleted in the file"));
            }
        }

        var fixes = changed > 0 ? $" It also corrects {changed} {(changed == 1 ? "word" : "words")}; those are fixed in the transcript without cutting." : "";
        var accepted = cuts.Count == 0 ? [] : ReviewCuts(layer, document, cuts, $"Import {Path.GetFileName(dialog.FileName)}",
            $"The edited file deletes {deleted.Count} words in {cuts.Count} places.{fixes} Untick any you want to keep, then choose Cut.");
        if (accepted is null)
        {
            return;
        }

        var layerId = layer.Id;
        var feedback = _editor.Edit("import transcript", project =>
        {
            var target = project.FindLayer(layerId)!;
            var messages = new List<string>();
            if (accepted.Count > 0)
            {
                var cut = TranscriptEditing.Apply(project, target, document, TranscriptEditing.Without(document, accepted.SelectMany(c => Enumerable.Range(c.First, c.Count))), TranscriptMode.Cut);
                if (!cut.Ok)
                {
                    return cut;
                }

                messages.Add(cut.Message);
            }

            if (changed > 0)
            {
                var fixedText = TranscriptEditing.Apply(project, target, TranscriptEditing.Build(project, target), edited, TranscriptMode.FixText);
                if (fixedText.Ok)
                {
                    messages.Add(fixedText.Message);
                }
            }

            return messages.Count == 0 ? EditResult.Fail("Nothing was changed.") : EditResult.Success(string.Join(" ", messages) + " Control Z undoes it.");
        }, EarconEvent.Deleted);
        await ApplyAsync(feedback);
    }

    private async Task FindRepeatsAsync()
    {
        if (SpeechDocument() is not var (layer, document))
        {
            return;
        }

        var found = TranscriptCleanup.Find(document);
        if (found.Count == 0)
        {
            _earcons.Play(EarconEvent.Done);
            Say("No repeats, filler sounds or stray symbols found. Alt Shift A asks the AI assistant, which also catches false starts said differently.");
            return;
        }

        await CutAcceptedAsync(layer, document, found, "Repeats and fillers",
            $"Found {found.Count} things to cut. The earlier try of a repeated phrase is cut and the last one kept. Untick any you want to keep.");
    }

    private async Task AiCleanupAsync()
    {
        if (SpeechDocument() is not var (layer, document))
        {
            return;
        }

        var provider = AiProvider.Find(_settings.AiProvider);
        var connection = new AiConnection(
            _settings.AiBaseUrl.Length > 0 ? _settings.AiBaseUrl : provider.BaseUrl,
            _settings.AiModel.Length > 0 ? _settings.AiModel : provider.DefaultModel,
            SecretStore.Unprotect(_settings.AiKeyProtected));
        if (connection.BaseUrl.Length == 0 || (provider.NeedsKey && string.IsNullOrWhiteSpace(connection.ApiKey)))
        {
            _earcons.Play(EarconEvent.Error);
            Say("The AI assistant is not set up yet. Open Preferences with Control Shift comma, go to the AI assistant tab, choose a service and enter its key. Ollama needs no key.");
            return;
        }

        var transcript = _editor.Project.TranscriptFor(layer.Items[document.Entries[0].ItemIndex].Path);
        var form = new FormDialog(this, "Clean up with AI");
        form.AddIntro($"{document.Entries.Count} words go to {provider.Name.Split(" (")[0]}, model {connection.Model}. Only the words are sent, not the video or sound. " +
            "You check every suggestion before anything is cut.");
        var about = form.AddText("What is the video about? (helps the AI)", _editor.Project.Title);
        var repeats = form.AddCheck("Repeated phrases and false starts", true);
        var fillers = form.AddCheck("Filler sounds and words (um, uh, you know)", true);
        var mistakes = form.AddCheck("Mistakes the speaker corrects straight away", true);
        var symbols = form.AddCheck("Stray symbols and noise", true);
        var extra = form.AddMultilineText("Anything else to cut (optional)", "", "For example: the part where I talk about my old phone.");
        if (form.ShowDialog() != true)
        {
            return;
        }

        var request = new AiCleanupRequest
        {
            About = about().Trim(),
            Repeats = repeats(),
            Fillers = fillers(),
            Mistakes = mistakes(),
            Symbols = symbols(),
            Extra = extra().Trim(),
            Language = transcript?.Language ?? "",
        };
        IReadOnlyList<CutSuggestion>? suggestions;
        try
        {
            suggestions = await WithProgressAsync("Asking the AI assistant", async (progress, token) =>
                await AiAssistant.SuggestCutsAsync(Http, connection, document, request, new Progress<double>(progress.Report), token));
        }
        catch (AiException ex)
        {
            _earcons.Play(EarconEvent.Error);
            Say(ex.Message);
            return;
        }

        if (suggestions is null)
        {
            return;
        }

        if (suggestions.Count == 0)
        {
            _earcons.Play(EarconEvent.Done);
            Say("The AI assistant found nothing to cut.");
            return;
        }

        await CutAcceptedAsync(layer, document, suggestions, "AI suggestions",
            $"The AI assistant suggests {suggestions.Count} cuts. Each was checked against the transcript. Untick any you want to keep.");
    }

    private async Task CutAcceptedAsync(Layer layer, TranscriptDocument document, IReadOnlyList<CutSuggestion> suggestions, string title, string intro)
    {
        if (ReviewCuts(layer, document, suggestions, title, intro) is not { Count: > 0 } accepted)
        {
            return;
        }

        var layerId = layer.Id;
        var edited = TranscriptEditing.Without(document, accepted.SelectMany(c => Enumerable.Range(c.First, c.Count)));
        var feedback = _editor.Edit("clean up transcript", project =>
        {
            var result = TranscriptEditing.Apply(project, project.FindLayer(layerId)!, document, edited, TranscriptMode.Cut);
            return result.Ok ? EditResult.Success($"Cut {accepted.Count} {(accepted.Count == 1 ? "place" : "places")}. {result.Message} Control Z undoes it.", result.FocusItemId) : result;
        }, EarconEvent.Deleted);
        await ApplyAsync(feedback);
    }

    /// <summary>
    /// A list of suggested cuts, each a checkbox (ticked to start with). Enter on a line, or Go to, moves the
    /// playhead there to listen first. Returns the ticked ones, or null if cancelled.
    /// </summary>
    internal List<CutSuggestion>? ReviewCuts(Layer layer, TranscriptDocument document, IReadOnlyList<CutSuggestion> suggestions, string title, string intro)
    {
        var window = new Window
        {
            Title = title,
            Owner = this,
            Width = 720,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontSize = 15,
        };
        var root = new DockPanel { Margin = new Thickness(12) };
        var introText = new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Focusable = true };
        AutomationProperties.SetName(introText, intro);
        DockPanel.SetDock(introText, Dock.Top);
        root.Children.Add(introText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var label = new TextBlock { Text = "Cuts to make (Space ticks or unticks, arrows move, Enter goes there):", Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(label, Dock.Top);
        root.Children.Add(label);
        var panel = new StackPanel();
        KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(panel, "Cuts to make");
        var boxes = new List<CheckBox>();
        foreach (var suggestion in suggestions)
        {
            var text = suggestion.Spoken(_editor.Project, layer, document);
            var box = new CheckBox { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, IsChecked = true, Tag = suggestion, Margin = new Thickness(0, 2, 0, 2) };
            AutomationProperties.SetName(box, text);
            boxes.Add(box);
            panel.Children.Add(box);
        }

        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        void GoTo(CutSuggestion cut)
        {
            _editor.SetPlayheadFromPlayback(cut.TimelineStart(_editor.Project, layer, document));
            Refresh();
            Say($"Playhead at {TimeFormat.Words(_editor.Playhead)}. After this list closes, press Space to listen there.", interrupt: false);
        }

        panel.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is not CheckBox { Tag: CutSuggestion cut } box)
            {
                return;
            }

            var index = boxes.IndexOf(box);
            if (e.Key == Key.Enter)
            {
                GoTo(cut);
                e.Handled = true;
            }
            else if (e.Key is Key.Down or Key.Up)
            {
                boxes[(index + (e.Key == Key.Down ? 1 : boxes.Count - 1)) % boxes.Count].Focus();
                e.Handled = true;
            }
        };

        Button Add(string text, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button { Content = text, MinWidth = 100, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
            AutomationProperties.SetName(button, text.Replace("_", ""));
            buttons.Children.Add(button);
            return button;
        }

        Add("Tick _all").Click += (_, _) => boxes.ForEach(b => b.IsChecked = true);
        Add("Tick _none").Click += (_, _) => boxes.ForEach(b => b.IsChecked = false);
        CheckBox? last = null;
        foreach (var box in boxes)
        {
            box.GotKeyboardFocus += (_, _) => last = box;
        }

        Add("_Go to").Click += (_, _) =>
        {
            if ((last ?? boxes[0]).Tag is CutSuggestion cut)
            {
                GoTo(cut);
            }
        };
        Add("C_ut ticked").Click += (_, _) => window.DialogResult = true;
        Add("Cancel", isCancel: true);
        window.Content = root;
        window.Loaded += (_, _) => boxes[0].Focus();
        if (SelfTestMode)
        {
            return suggestions.ToList();
        }

        return window.ShowDialog() == true
            ? boxes.Where(b => b.IsChecked == true).Select(b => (CutSuggestion)b.Tag).ToList()
            : null;
    }
}
