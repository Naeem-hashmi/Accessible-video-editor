using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AccessibleVideoEditor.App.Services;
using AccessibleVideoEditor.App.ViewModels;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Online;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Templates;
using AccessibleVideoEditor.Core.Audio;

namespace AccessibleVideoEditor.App;

/// <summary>
/// Runs with "--self-test log.txt" on a real Windows desktop (used by continuous integration). It drives
/// the actual window: keys through the real key handler, real FFmpeg, every dialog, and the UI Automation
/// names screen readers read. Exit code 0 means every check passed.
/// </summary>
internal static class SelfTest
{
    public static string LogPath { get; set; } = "self-test.log";

    public static async Task<int> RunAsync(MainWindow window)
    {
        var log = new List<string>();
        var failures = 0;

        void Check(bool condition, string what)
        {
            log.Add($"{(condition ? "PASS" : "FAIL")} {what}");
            if (!condition)
            {
                failures++;
            }
        }

        async Task Step(string name, Func<Task> body)
        {
            try
            {
                await body();
            }
            catch (Exception ex)
            {
                Check(false, $"{name} threw {ex.GetType().Name}: {ex.Message}");
                log.Add(ex.ToString());
            }
        }

        var folder = Path.Combine(Path.GetTempPath(), "ave-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);

        await Step("ffmpeg", () =>
        {
            Check(window.Tools is not null, "FFmpeg was found next to the program");
            log.Add("FFmpeg: " + window.Tools?.VersionText);
            Check(window.Tools?.HasEncoder("libx264") == true, "FFmpeg has the H.264 encoder");
            return Task.CompletedTask;
        });

        await Step("template and lists", async () =>
        {
            await window.ApplyAsync(window.Editor.Load(new ProjectSession(ProjectTemplates.Find("youtube")!.Create()), "Self test"));
            Check(window.LayerEntries.Count == 4, "YouTube template shows 4 layers in the layer list");
            Check(window.LayerEntries.All(v => v.Spoken.StartsWith("Layer ", StringComparison.Ordinal)), "Layer entries have spoken names");
        });

        string? video = null;
        await Step("media files", async () =>
        {
            video = Path.Combine(folder, "test clip.mp4");
            var png = Path.Combine(folder, "logo.png");
            await RunFfmpeg(window.Tools!, "-f", "lavfi", "-i", "testsrc=size=640x360:rate=30:duration=4", "-f", "lavfi", "-i",
                "sine=frequency=440:duration=4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest", video);
            await RunFfmpeg(window.Tools!, "-f", "lavfi", "-i", "color=c=red:size=200x200", "-frames:v", "1", png);

            await window.AddFilesAsync([video, video, png], atPlayhead: false);
            var main = window.Editor.Project.Layers[0];
            Check(main.Items.Count == 3, "Three files were added to the main video layer");
            Check(window.ItemEntries.Count == 3, "The timeline list shows three items");
            Check(window.ItemEntries.All(v => !string.IsNullOrWhiteSpace(v.Description)), "Every timeline item has a description");
        });

        await Step("keyboard", async () =>
        {
            window.Topmost = true;
            window.Activate();
            await Settle();
            var main = window.Editor.Project.Layers[0];
            window.Editor.FocusItem(main.Items[0].Id);
            window.Refresh();
            window.FocusTimeline();
            await Settle();

            // Windows only gives keyboard focus to the active window. A build machine may refuse to bring
            // the window to the front; then keys are sent as commands instead of key presses.
            var realKeys = window.IsActive && window.ItemEntries.Count > 0 && Keyboard.FocusedElement is ListBoxItem;
            log.Add(realKeys ? "Window is active: sending real key presses." : "SKIP real key presses: the window is not active on this machine; running commands directly.");

            async Task Press(Key key, CommandId command)
            {
                if (realKeys)
                {
                    PressKey(window, key);
                    await Settle();
                }
                else
                {
                    await window.RunAsync(command);
                }
            }

            await Press(Key.Right, CommandId.NextItem);
            Check(window.Editor.FocusedItemIndex == 1, "Right arrow moves to the next item");
            if (realKeys)
            {
                Check(Keyboard.FocusedElement is ListBoxItem { DataContext: SpokenViewModel { Id: var id } } && id == main.Items[1].Id,
                    "Keyboard focus follows to the second item");
                var focused = Keyboard.FocusedElement as UIElement;
                var name = focused is null ? "" : UIElementAutomationPeer.CreatePeerForElement(focused)?.GetName() ?? "";
                log.Add("Screen reader name of focused item: " + name);
                Check(name.Contains("2 of 3", StringComparison.Ordinal), "The focused item's UI Automation name says its position");
            }

            var entry = window.ItemEntries.FirstOrDefault(v => v.Id == window.Editor.FocusedItemId);
            Check(entry?.Description.Contains("2 of 3", StringComparison.Ordinal) == true, "The timeline entry describes its position");

            await Press(Key.Down, CommandId.NextLayer);
            Check(window.Editor.FocusedLayerIndex == 1, "Down arrow moves to layer 2");

            await Press(Key.Up, CommandId.PreviousLayer);
            Check(window.Editor.FocusedLayerIndex == 0, "Up arrow moves back to layer 1");

            await window.RunAsync(CommandId.Layer3);
            Check(window.Editor.FocusedLayerIndex == 2 && !window.Editor.ListenAll, "Alt 3 focuses layer 3 and listens to it alone");
            await window.RunAsync(CommandId.ListenAllLayers);
            Check(window.Editor.ListenAll, "Alt 0 listens to all layers");
            await window.RunAsync(CommandId.Layer1);

            window.Editor.GoToTime(2);
            window.Refresh();
            window.FocusTimeline();
            await Settle();
            await Press(Key.OemOpenBrackets, CommandId.SetRangeStart);
            Check(window.Editor.Project.RangeStart is { } rs && Math.Abs(rs - 2) < 0.01, "Left bracket sets the range start");

            await Press(Key.S, CommandId.Split);
            Check(window.Editor.Project.Layers[0].Items.Count == 4, "S splits the item at the playhead");

            await Press(Key.Delete, CommandId.Delete);
            Check(window.Editor.Project.Layers[0].Items.Count == 3, "Delete removes the focused item");

            await window.RunAsync(CommandId.Undo);
            Check(window.Editor.Project.Layers[0].Items.Count == 4, "Control Z undoes the delete");

            await window.RunAsync(CommandId.MoveItemLater);
            await window.RunAsync(CommandId.SlideLater);
            await window.RunAsync(CommandId.AddMarker);
            await window.RunAsync(CommandId.NextMarker);
            await window.RunAsync(CommandId.ToggleMute);
            Check(window.Editor.Project.Layers[0].Muted, "M mutes the focused layer");
            await window.RunAsync(CommandId.ToggleMute);
            Check(window.ItemEntries.Count == window.Editor.FocusedLayer!.Items.Count, "The timeline list matches the layer after edits");
            window.Topmost = false;
        });

        await Step("dialogs", () =>
        {
            window.Editor.FocusLayer(window.Editor.Project.Layers[0].Id);
            window.Refresh();
            var dialogs = new List<Window>
            {
                window.BuildItemOptions(out _)!,
                window.BuildLayerOptions(out _)!,
                window.BuildProjectSettings(out _),
                window.BuildPreferences(out _),
                window.BuildExportDialog(out _),
                window.BuildKeyboardHelp(),
            };
            foreach (var dialog in dialogs)
            {
                Check(dialog is not null, $"Dialog {dialog?.Title} can be built");
                dialog!.Show();
                dialog.UpdateLayout();
                var unnamed = CountUnnamedControls(dialog);
                Check(unnamed == 0, $"Every control in \"{dialog.Title}\" has a screen reader name ({unnamed} without)");
                dialog.Close();
            }

            return Task.CompletedTask;
        });

        await Step("speech and sounds", () =>
        {
            window.Speech.Say("Self test speech.");
            foreach (var theme in EarconTheme.BuiltIn)
            {
                window.Earcons.Configure(theme.Name, 10);
                foreach (var e in Enum.GetValues<EarconEvent>())
                {
                    window.Earcons.Play(e);
                }
            }

            window.Earcons.Configure("Soft", 60);
            Check(true, "Speech and all earcon themes ran: " + window.Speech.ChannelName);
            return Task.CompletedTask;
        });

        await Step("text, position mode, regions, sound keys, templates", async () =>
        {
            Check(window.Tools?.Text.Available == true, "The bundled FFmpeg can draw text");
            window.Editor.GoToTime(1);
            await window.ApplyAsync(window.Editor.AddText(new TextStyle { Content = "Self test title", AnimationIn = TextAnimation.SlideFromLeft }, 2,
                new Placement { Position = PositionPreset.BottomCenter }));
            var text = window.Editor.FocusedItem;
            Check(text?.Kind == ItemKind.Text, "Control Shift T adds a text item");
            Check(window.Editor.FocusedLayer?.Name == "Text", "Text goes on its own layer above the video");

            window.Activate();
            window.FocusTimeline();
            await Settle();
            var realKeys = window.IsActive && Keyboard.FocusedElement is ListBoxItem;
            await window.RunAsync(CommandId.PositionMode);
            Check(window.Editor.InPositionMode, "P enters position mode");
            if (realKeys)
            {
                PressKey(window, Key.Right);
                await Settle();
                PressKey(window, Key.Enter);
                await Settle();
            }
            else
            {
                await window.ApplyAsync(window.Editor.Position(PositionAction.Right));
                await window.ApplyAsync(window.Editor.Position(PositionAction.Done));
            }

            Check(!window.Editor.InPositionMode, "Enter leaves position mode");
            Check(window.Editor.FocusedItem?.Placement.Position == PositionPreset.Custom, "Right arrow in position mode moved the text");

            await window.RunAsync(CommandId.DescribePicture);
            Check(window.StatusTextValue.Contains("Self test title", StringComparison.Ordinal), "Shift W describes the picture, including the text");

            await window.RunAsync(CommandId.FocusProperties);
            await Settle();
            Check(CountUnnamedControls(window.PropertiesRegion) == 0, "Every control in the properties area has a screen reader name");
            await window.RunAsync(CommandId.FocusLibrary);
            await Settle();
            Check(CountUnnamedControls(window.LibraryRegion) == 0, "Every control in the library has a screen reader name");
            if (realKeys)
            {
                Check(window.CurrentRegion == Region.Library, "Control 1 moves to the library");
            }

            await window.RunAsync(CommandId.FocusPreview);
            await Settle();
            var previewName = System.Windows.Automation.AutomationProperties.GetName(window.PreviewRegion);
            log.Add("Preview name: " + previewName);
            Check(previewName.StartsWith("Preview. At ", StringComparison.Ordinal), "The preview area describes the picture");
            await window.RunAsync(CommandId.FocusTimelineRegion);

            window.Editor.FocusLayer(window.Editor.Project.Layers[0].Id);
            window.Refresh();
            var before = window.Editor.FocusedItem?.Volume ?? 0;
            await window.RunAsync(CommandId.ItemVolumeUp);
            Check(Math.Abs((window.Editor.FocusedItem?.Volume ?? 0) - before - 0.1) < 0.001, "Control Up raises the item volume by 10 percent");

            var saved = window.Templates.Save(window.Editor.Project, "Self test template", "Self test", "", includeMedia: false);
            window.FillTemplates();
            Check(window.Templates.List().Any(t => t.Name == saved.Name && t.Category == "Self test"), "A project can be saved as a template in a folder");
            var fromTemplate = window.Templates.Create(saved);
            Check(fromTemplate.Layers.Any(l => l.Items.Any(i => i.Kind == ItemKind.Text)), "The template keeps the text");
            window.Templates.Delete(saved);
            Directory.Delete(window.Templates.FolderFor("Self test"), recursive: true);

            try
            {
                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var result = await new OpenverseClient(http).SearchAsync("piano", OnlineAudioCategory.Music);
                log.Add($"Openverse returned {result.Items.Count} usable results of {result.TotalCount}.");
                Check(result.Items.All(r => r.License is "cc0" or "pdm" or "by"), "Online results are only public domain or CC BY");
            }
            catch (Exception ex)
            {
                log.Add("WARN online search not checked (network): " + ex.Message);
            }
        });

        await Step("save and open", () =>
        {
            var path = Path.Combine(folder, "self test" + ProjectSerializer.FileExtension);
            window.Editor.Session.Save(path);
            var before = ProjectSerializer.ToJson(window.Editor.Project);
            Check(window.OpenProjectFile(path, announce: false), "The saved project opens again");
            Check(ProjectSerializer.ToJson(window.Editor.Project) == before, "The reopened project is identical");
            return Task.CompletedTask;
        });

        await Step("export", async () =>
        {
            var output = Path.Combine(folder, "export.mp4");
            var ok = await window.ExportToAsync(OutputProfile.YouTube720, output, rangeOnly: false, showProgress: false);
            Check(ok && File.Exists(output), "Export to 720p MP4 finished");
            var info = await MediaProbe.ProbeAsync(window.Tools!, output);
            Check(info.Width == 1280 && info.Height == 720 && info.HasAudio, "The exported file is 1280 by 720 with sound");
            Check(Math.Abs(info.Duration - Timeline.Duration(window.Editor.Project.Layers.Where(l => !l.Muted))) < 0.2,
                $"The exported length matches the project ({info.Duration:0.00} seconds)");
        });

        await Step("preview render", async () =>
        {
            var output = Path.Combine(folder, "preview.mp4");
            var plan = new GraphBuilder(window.Tools!.HasEncoder, window.Tools!.Text).Build(new RenderRequest
            {
                Project = window.Editor.Project,
                Profile = OutputProfile.PreviewVideo,
                OutputPath = output,
                CanvasOverride = (640, 360),
                End = 5,
            });
            await RenderRunner.RunAsync(window.Tools, plan, output);
            Check(File.Exists(output), "A preview renders");
        });

        await Step("layout: ribbon, play controls, tracks, layers, inspector", async () =>
        {
            Check(window.Ribbon.Items.Count == 8, $"The ribbon has 8 tabs ({window.Ribbon.Items.Count})");
            Check(CountUnnamedControls(window.Ribbon) == 0, "Every ribbon button has a screen reader name");
            var ribbonButtons = CountButtons(window.Ribbon);
            log.Add($"Ribbon buttons: {ribbonButtons}");
            Check(ribbonButtons >= 50, "The ribbon has the editing, transition, animation, audio, caption and export buttons");
            Check(window.Transport.Children.Count == 8 && CountUnnamedControls(window.Transport) == 0, "The play controls are named buttons");
            Check(window.Tracks.ActualWidth > 100 && window.Tracks.ActualHeight >= TimelineTracks.RulerHeight + TimelineTracks.RowHeight,
                $"The visual timeline is drawn ({window.Tracks.ActualWidth:0} by {window.Tracks.ActualHeight:0})");

            // Left and Right on a layer go through its items and keep focus on the layer.
            var layer = window.Editor.Project.Layers[0];
            window.Editor.FocusItem(layer.Items[0].Id);
            window.Refresh();
            window.FocusLayerList();
            await Settle();
            await window.RunAsync(CommandId.NextItem);
            await Settle();
            Check(window.Editor.FocusedItemIndex == 1, "Right arrow on a layer moves to its next item");
            Check(window.LayersList.IsKeyboardFocusWithin || !window.IsActive, "Focus stays on the layer");
            if (window.IsActive)
            {
                Check(window.PropertiesTitle.Text.StartsWith("Layer ", StringComparison.Ordinal), $"The inspector shows the layer while the Layers list has focus: {window.PropertiesTitle.Text}");
            }
            else
            {
                log.Add("SKIP layer inspector check: the window is not active, so the Layers list can't hold keyboard focus.");
            }

            // A text item shows text style options.
            var textLayer = window.Editor.Project.Layers.FirstOrDefault(l => l.Items.Any(i => i.Kind == ItemKind.Text));
            if (textLayer is not null)
            {
                window.Editor.FocusLayer(textLayer.Id);
                window.Editor.FocusItem(textLayer.Items.First(i => i.Kind == ItemKind.Text).Id);
                window.Refresh();
                window.FocusTimeline();
                await Settle();
                window.RebuildInspector();
                Check(window.PropertiesTitle.Text.StartsWith("Text:", StringComparison.Ordinal), $"The inspector shows text options: {window.PropertiesTitle.Text}");
                var names = NamesIn(window.PropertiesPanel);
                Check(names.Contains("Bold") && names.Contains("Text colour") && names.Contains("Background behind the text"), "Text options include bold, colour and background");
                Check(CountUnnamedControls(window.PropertiesPanel) == 0, "Every inspector control has a screen reader name");
            }
            else
            {
                Check(false, "A text item exists for the inspector check");
            }
        });

        await Step("themes and still preview", async () =>
        {
            static double Lightness(object? brush) => brush is System.Windows.Media.SolidColorBrush b
                ? (0.2126 * b.Color.R + 0.7152 * b.Color.G + 0.0722 * b.Color.B) / 255 : -1;
            if (SystemParameters.HighContrast)
            {
                log.Add("SKIP theme colours: High Contrast is on, which always uses the system colours.");
            }
            else
            {
                ThemeService.Apply(Application.Current, ThemeService.DarkChoice);
                await Settle();
                var dark = Lightness(Application.Current.Resources["Ave.Window"]);
                ThemeService.Apply(Application.Current, ThemeService.LightChoice);
                await Settle();
                var light = Lightness(Application.Current.Resources["Ave.Window"]);
                log.Add($"Window background lightness: dark {dark:0.00}, light {light:0.00}");
                Check(dark is >= 0 and < 0.35 && light > 0.75, "Dark and light themes change the window colours");
                ThemeService.Apply(Application.Current, ThemeService.SystemChoice);
                await Settle();
            }

            // Paused, the preview shows the picture at the playhead.
            window.Preview.ShowFrame(window.Editor.Project, 1.0);
            var clock = Stopwatch.StartNew();
            System.Windows.Media.Imaging.WriteableBitmap? bitmap = null;
            while (clock.Elapsed.TotalSeconds < 15)
            {
                await Task.Delay(250);
                if (window.PreviewImage.Source is System.Windows.Media.Imaging.WriteableBitmap { PixelWidth: > 0 } b && HasPicture(b))
                {
                    bitmap = b;
                    break;
                }
            }

            Check(bitmap is not null, $"A paused preview shows the picture at the playhead ({clock.Elapsed.TotalSeconds:0.0} s)");
            Check(bitmap is { PixelWidth: >= 480 }, $"The still preview is sharp ({bitmap?.PixelWidth} by {bitmap?.PixelHeight})");
        });

        await Step("beats", async () =>
        {
            var pulse = Path.Combine(AppPaths.BuiltInSoundEffects, "Upbeat pulse.wav");
            var beats = BeatDetector.Analyse(window.Tools!, pulse);
            Check(beats.HasSteadyBeat && beats.Bpm is > 119 and < 121, $"The built-in upbeat pulse is found at 120 BPM ({beats.Bpm:0.0})");
            var layer = window.Editor.Project.Layers.First(l => l.Kind == LayerKind.Audio);
            await window.ApplyAsync(window.Editor.Edit("add music", p =>
            {
                var target = p.FindLayer(layer.Id)!;
                target.Items.Add(new Item { Kind = ItemKind.Audio, Path = pulse, Name = "Upbeat pulse", HasAudio = true, Duration = 10, SourceDuration = 30 });
                return BeatEditing.SetBeats(target, target.Items.Count - 1, beats);
            }));
            window.Editor.SetPlayheadFromPlayback(0.01);
            await window.RunAsync(CommandId.NextBeat);
            var first = BeatEditing.BeatTimes(window.Editor.Project).FirstOrDefault(b => b > 0.02);
            Check(Math.Abs(window.Editor.Playhead - first) < 1e-6, $"Alt B jumps to the next beat ({window.Editor.Playhead:0.000} s)");
        });

        await Step("offline speech recognition", async () =>
        {
            var speech = Environment.GetEnvironmentVariable("AVE_SELFTEST_SPEECH");
            var models = Environment.GetEnvironmentVariable("AVE_WHISPER_MODELS") ?? AppPaths.WhisperModels;
            var whisper = WhisperRunner.Locate(models);
            Check(whisper is not null, "whisper-cli is installed next to the program");
            Check(whisper?.VadModelPath is not null, "The voice activity model for skipping silence is installed");
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "whisper", "ggml-vulkan.dll")), "The graphics-chip (Vulkan) backend is installed");
            if (whisper is null || speech is null || !File.Exists(speech) || !whisper.HasModel(WhisperModel.All[0]))
            {
                log.Add("SKIP transcription: no speech sample or Fast model on this machine.");
                return;
            }

            var clock = Stopwatch.StartNew();
            var transcript = await whisper.TranscribeAsync(window.Tools!, WhisperModel.All[0], speech, "en");
            var text = string.Join(" ", transcript.Words.Select(w => w.Text));
            log.Add($"Heard in {clock.Elapsed.TotalSeconds:0.0} s on {whisper.LastDevice} ({whisper.Options.Spoken}): {text}");
            Check(text.Contains("hello", StringComparison.OrdinalIgnoreCase) && text.Contains("video", StringComparison.OrdinalIgnoreCase),
                "Windows speech is transcribed offline with word times");
            Check(transcript.Words.All(w => w.End >= w.Start), "Word times are in order");

            // Only a used part: words only from that part, in file time, and the part is remembered.
            var part = new Core.Audio.TimeStretch(1.5, Math.Min(6, transcript.Words[^1].End));
            var partial = await whisper.TranscribePartsAsync(window.Tools!, WhisperModel.All[0], speech, "en", [part]);
            log.Add($"Heard between {part.Start} and {part.End:0.0} s: {string.Join(" ", partial.Words.Select(w => w.Text))}");
            Check(partial.Words.Count > 0 && partial.Words.All(w => (w.Start + w.End) / 2 >= part.Start && (w.Start + w.End) / 2 < part.End),
                "Transcribing only a used part keeps words from that part, in file time");
            Check(TranscriptCoverage.Missing(partial, [part]).Count == 0, "A transcribed part is not done again");
        });

        await Step("real recorded sounds", () =>
        {
            var sounds = RecordedSounds.Load(AppPaths.Recordings);
            Check(sounds.Count >= 20, $"Real recordings are installed ({sounds.Count} found)");
            Check(sounds.All(s => s.License is "CC0" or "Public domain"), "Every recording is public domain or CC0");
            Check(sounds.Any(s => s.Category == "Typing") && sounds.Any(s => s.Category == "Clicks"), "Typing and click recordings are included");
            if (sounds.Count > 0 && window.Tools is { } tools)
            {
                using var stream = FfmpegStream.Decode(tools, RecordedSounds.FullPath(AppPaths.Recordings, sounds[0]), maxSeconds: 1);
                var buffer = new byte[65536];
                long total = 0;
                int read;
                while ((read = stream.Output.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                }

                Check(total > 0, "A recording decodes for Space preview without Windows Media Player");
            }

            var player = new SoundPreviewPlayer(() => window.Tools);
            log.Add("Sound preview player on this machine: " + (sounds.Count > 0 && player.Play(RecordedSounds.FullPath(AppPaths.Recordings, sounds[0])) ? "played" : "no sound device"));
            player.Dispose();
            return Task.CompletedTask;
        });

        await Step("live preview", async () =>
        {
            var clock = Stopwatch.StartNew();
            var error = await window.Preview.PlayAsync(window.Editor.Project, RenderScope.AllLayers, 0);
            var startup = clock.Elapsed.TotalSeconds;
            log.Add($"Live preview started in {startup:0.00} seconds{(error is null ? "" : ": " + error)}.");
            Check(error is null, "Live preview starts without Windows Media Player");
            Check(startup < 5, "Live preview starts within 5 seconds");
            await Task.Delay(1500);
            var at = window.Preview.CurrentTime;
            log.Add($"After 1.5 seconds the preview is at {at:0.00} seconds.");
            Check(window.Preview.IsPlaying && at > 0.5, "The preview clock moves while playing");
            window.Preview.Pause();
            Check(!window.Preview.IsPlaying, "Pause stops the preview");
            var fromMiddle = await window.Preview.PlayAsync(window.Editor.Project, RenderScope.AllLayers, 3);
            Check(fromMiddle is null && window.Preview.CurrentTime >= 3, "The preview can start from the middle");
            window.Preview.Pause();
        });

        await Step("selection, stretch, speed and options menus", async () =>
        {
            var main = window.Editor.Project.Layers.First(l => l.Items.Count >= 2);
            window.Editor.FocusLayer(main.Id);
            window.Editor.FocusItem(main.Items[0].Id);
            window.Refresh();
            window.FocusTimeline();
            await Settle();
            await window.RunAsync(CommandId.SelectNext);
            Check(window.Editor.SelectedIndexes.Count == 2, $"Shift Right selects two items ({window.Editor.SelectedIndexes.Count})");
            Check(window.ItemEntries.Count(e => e.Description.StartsWith("Selected,", StringComparison.Ordinal)) == 2, "Selected items say so in the list");
            var menu = window.ShowOptionsMenu();
            await Settle();
            Check(menu is { IsOpen: true } && menu.Items.OfType<MenuItem>().Any(m => (m.Header as string ?? "").Contains("2 selected items", StringComparison.Ordinal)),
                "The Applications key menu offers settings for the selected items");
            if (menu is not null)
            {
                menu.IsOpen = false;
            }

            await window.RunAsync(CommandId.Stop);
            Check(!window.Editor.HasSelection, "Escape clears the selection");

            var clip = main.Items.FindIndex(i => i.Kind is ItemKind.Video or ItemKind.Audio);
            if (clip >= 0)
            {
                window.Editor.FocusItem(window.Editor.Project.Layers.First(l => l.Id == main.Id).Items[clip].Id);
                var before = window.Editor.FocusedItem!.Duration;
                await window.RunAsync(CommandId.Faster);
                var item = window.Editor.FocusedItem!;
                Check(Math.Abs(item.Speed - 1.25) < 1e-6 && Math.Abs(item.Duration - before / 1.25) < 0.01, $"A clip plays faster and gets shorter ({item.Speed}x, {item.Duration:0.00} s)");
                await window.RunAsync(CommandId.Undo);
                menu = window.ShowOptionsMenu();
                await Settle();
                Check(menu is not null && menu.Items.OfType<MenuItem>().Any(m => (m.Header as string ?? "").StartsWith("All _settings", StringComparison.Ordinal)),
                    "The options menu for one item opens with All settings");
                if (menu is not null)
                {
                    menu.IsOpen = false;
                }
            }

            var focused = window.Editor.FocusedItem!;
            var layerNow = window.Editor.FocusedLayer!;
            var start = Timeline.Span(layerNow, window.Editor.FocusedItemIndex).Start;
            await window.RunAsync(CommandId.SlideLaterFine);
            await window.RunAsync(CommandId.SlideLaterFine, repeating: true, holdFactor: 10);
            var moved = Timeline.Span(window.Editor.FocusedLayer!, window.Editor.FocusedItemIndex).Start - start;
            Check(Math.Abs(moved - 1.1) < 0.01, $"Holding Alt Shift L slides further each step ({moved:0.00} s)");
            await window.RunAsync(CommandId.Undo);
            var back = Timeline.Span(window.Editor.FocusedLayer!, window.Editor.FocusedItemIndex).Start;
            Check(Math.Abs(back - start) < 0.01, "One undo takes back the whole held slide");
            Check(focused.Id == window.Editor.FocusedItem!.Id, "Focus stays on the item");
        });

        await Step("timeline follows focus and shows pictures", async () =>
        {
            var main = window.Editor.Project.Layers.First(l => l.Items.Any(i => i.Kind == ItemKind.Video && File.Exists(i.Path)));
            var video = main.Items.First(i => i.Kind == ItemKind.Video && File.Exists(i.Path));
            ImageSource? picture = null;
            for (var i = 0; i < 100 && picture is null; i++)
            {
                picture = window.Tracks.Thumbnails!.Get(video.Path, 0.5);
                await Task.Delay(100);
            }

            Check(picture is { Width: > 0 }, "Timeline pictures are made for video clips");

            window.Tracks.PixelsPerSecond = 400;
            window.Editor.FocusLayer(main.Id);
            window.Editor.FocusItem(main.Items[0].Id);
            window.Refresh();
            await Settle();
            window.TracksScroll.ScrollToHorizontalOffset(0);
            window.Editor.FocusItem(main.Items[^1].Id);
            window.Refresh();
            await Settle();
            var span = Timeline.Span(window.Editor.FocusedLayer!, window.Editor.FocusedItemIndex);
            var x = window.Tracks.XOf(span.Start);
            var offset = window.TracksScroll.HorizontalOffset;
            Check(x >= offset && x <= offset + window.TracksScroll.ViewportWidth,
                $"The timeline scrolls to the focused item (item at {x:0} px, view {offset:0} to {offset + window.TracksScroll.ViewportWidth:0})");
            window.Tracks.PixelsPerSecond = 40;
        });

        await Step("every command runs without crashing", async () =>
        {
            foreach (var command in Enum.GetValues<CommandId>())
            {
                if (window.Editor.Execute(command) is { } feedback)
                {
                    await window.ApplyAsync(feedback with { Playback = PlaybackRequest.None });
                }
            }

            Check(true, "All controller commands ran");
        });

        log.Add(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} CHECK(S) FAILED");
        await File.WriteAllLinesAsync(LogPath, log);
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }

        return failures == 0 ? 0 : 1;
    }

    private static void PressKey(MainWindow window, Key key)
    {
        var target = Keyboard.FocusedElement as IInputElement ?? window;
        var source = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("The window is not shown.");
        var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(down);
        if (!down.Handled)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        }
    }

    private static async Task Settle()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        await Task.Delay(50);
    }

    /// <summary>Counts focusable controls whose UI Automation name is empty.</summary>
    /// <summary>True if the bitmap is not all one colour (a real frame, not an empty black area).</summary>
    private static bool HasPicture(System.Windows.Media.Imaging.WriteableBitmap bitmap)
    {
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var first = BitConverter.ToInt32(pixels, 0);
        for (var i = 0; i < pixels.Length; i += 4 * 97)
        {
            if (BitConverter.ToInt32(pixels, i) != first)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountButtons(DependencyObject root) =>
        LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>().Sum(c => (c is Button ? 1 : 0) + CountButtons(c));

    private static HashSet<string> NamesIn(DependencyObject root)
    {
        var names = new HashSet<string>();
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Control control && UIElementAutomationPeer.CreatePeerForElement(control)?.GetName() is { Length: > 0 } name)
            {
                names.Add(name);
            }

            names.UnionWith(NamesIn(child));
        }

        return names;
    }

    private static int CountUnnamedControls(DependencyObject root)
    {
        var count = 0;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Control { Focusable: true } control and (TextBox or ComboBox or CheckBox or Button or ListBox or Slider))
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(control);
                if (string.IsNullOrWhiteSpace(peer?.GetName()))
                {
                    count++;
                    AppPaths.Log("Unnamed control: " + control.GetType().Name);
                }
            }

            count += CountUnnamedControls(child);
        }

        return count;
    }

    private static async Task RunFfmpeg(FfmpegTools tools, params string[] args)
    {
        var psi = new ProcessStartInfo(tools.FfmpegPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var error = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException("ffmpeg failed: " + error);
        }
    }
}
