using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AccessibleVideoEditor.App.Dialogs;
using AccessibleVideoEditor.App.Services;
using AccessibleVideoEditor.Core.Checks;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Templates;
using AccessibleVideoEditor.Core.Text;
using Microsoft.Win32;

namespace AccessibleVideoEditor.App;

public partial class MainWindow
{
    private const string ProjectFilter = "Accessible Video Editor projects (*.avproj)|*.avproj|All files|*.*";

    // ------------------------------------------------------------------ menu

    private static readonly (string Category, string Header)[] MenuHeaders =
    [
        ("File", "_File"), ("Edit", "_Edit"), ("Add", "_Add"), ("Move around", "_Navigate"), ("Arrange", "A_rrange"),
        ("Cut and mark", "_Cut and mark"), ("Play and listen", "_Play"), ("Information", "_Information"),
    ];

    private void BuildMenu()
    {
        MainMenu.Items.Clear();
        foreach (var (category, header) in MenuHeaders)
        {
            var top = MenuItem(header);
            MenuItem? layerSubmenu = null;
            foreach (var command in CommandCatalog.All.Where(c => c.Category == category))
            {
                var item = CommandMenuItem(command);
                if (command.Id is >= CommandId.Layer1 and <= CommandId.Layer9)
                {
                    if (layerSubmenu is null)
                    {
                        layerSubmenu = MenuItem("Go to _layer 1 to 9");
                        top.Items.Add(layerSubmenu);
                    }

                    layerSubmenu.Items.Add(item);
                }
                else
                {
                    top.Items.Add(item);
                }
            }

            if (category == "File")
            {
                top.Items.Add(new Separator());
                var exit = MenuItem("E_xit", "Alt+F4");
                exit.Click += (_, _) => Close();
                top.Items.Add(exit);
            }

            MainMenu.Items.Add(top);
        }

        var tools = MenuItem("_Tools");
        tools.Items.Add(ActionItem("Open my _sound effects folder", () => OpenFolder(AppPaths.UserSoundEffects)));
        tools.Items.Add(ActionItem("Open my sound _themes folder", () => OpenFolder(AppPaths.UserSoundThemes)));
        tools.Items.Add(ActionItem("_Copy the current sound theme to my themes folder", ExportCurrentTheme));
        tools.Items.Add(ActionItem("Open the _keyboard settings file", OpenKeymapFile));
        tools.Items.Add(ActionItem("Open the _log file", () => OpenFile(AppPaths.LogFile)));
        MainMenu.Items.Add(tools);

        var help = MenuItem("_Help");
        help.Items.Add(CommandMenuItem(CommandCatalog.Get(CommandId.KeyboardHelp)));
        help.Items.Add(ActionItem("_Getting started", ShowGettingStarted));
        help.Items.Add(ActionItem("_About", ShowAbout));
        MainMenu.Items.Add(help);
    }

    private MenuItem CommandMenuItem(CommandInfo command)
    {
        var item = MenuItem(EscapeAccess(command.Title), _keymap.GestureText(command.Id));
        AutomationProperties.SetHelpText(item, command.Help);
        item.ToolTip = command.Help;
        item.Click += async (_, _) =>
        {
            await Dispatcher.InvokeAsync(FocusTimeline);
            await RunAsync(command.Id);
        };
        return item;
    }

    private static MenuItem MenuItem(string header, string? gesture = null)
    {
        var item = new MenuItem { Header = header };
        if (!string.IsNullOrEmpty(gesture))
        {
            item.InputGestureText = gesture;
        }

        return item;
    }

    private MenuItem ActionItem(string header, Action action)
    {
        var item = MenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private static string EscapeAccess(string text) => text.Replace("_", "__");

    // ------------------------------------------------------------------ dispatch

    /// <summary>Commands that need dialogs or files. Returns false for commands the controller handles.</summary>
    private async Task<bool> RunWindowCommandAsync(CommandId command)
    {
        switch (command)
        {
            case CommandId.NewProject: NewProject(); return true;
            case CommandId.OpenProject: OpenProject(); return true;
            case CommandId.SaveProject: SaveProject(saveAs: false); return true;
            case CommandId.SaveProjectAs: SaveProject(saveAs: true); return true;
            case CommandId.Export: await ExportAsync(); return true;
            case CommandId.ProjectSettings: await ShowProjectSettingsAsync(); return true;
            case CommandId.Preferences: ShowPreferences(); return true;
            case CommandId.ItemProperties: await ShowItemOptionsAsync(); return true;
            case CommandId.ImportToEnd: await ImportAsync(atPlayhead: false); return true;
            case CommandId.ImportAtPlayhead: await ImportAsync(atPlayhead: true); return true;
            case CommandId.AddSoundEffect: await AddSoundEffectAsync(); return true;
            case CommandId.AddColorCard: await AddColorCardAsync(); return true;
            case CommandId.LayerProperties: await ShowLayerOptionsAsync(); return true;
            case CommandId.GoToLayer: await GoToLayerAsync(); return true;
            case CommandId.GoToTime: await GoToTimeAsync(); return true;
            case CommandId.SwitchPane: CycleRegion(1); return true;
            case CommandId.PreviousPane: CycleRegion(-1); return true;
            case CommandId.FocusLibrary: FocusRegion(Region.Library); return true;
            case CommandId.FocusTimelineRegion: FocusRegion(Region.Timeline); return true;
            case CommandId.FocusProperties: FocusRegion(Region.Properties); return true;
            case CommandId.FocusPreview: FocusRegion(Region.Preview); return true;
            case CommandId.FocusLayers: FocusRegion(Region.Layers); return true;
            case CommandId.AddText: await AddTextAsync(null); return true;
            case CommandId.EditTextOrRename: await EditTextOrRenameAsync(); return true;
            case CommandId.OnlineMusic: FocusLibrary(LibraryTab.OnlineMusic); return true;
            case CommandId.TemplateManager: FillTemplates(); FocusLibrary(LibraryTab.Templates); return true;
            case CommandId.SaveAsTemplate: SaveAsTemplate(); return true;
            case CommandId.MarkerList: await ShowMarkersAsync(); return true;
            case CommandId.TransitionHere: await TransitionHereAsync(); return true;
            case CommandId.ProjectCheck: await ShowProjectCheckAsync(); return true;
            case CommandId.KeyboardHelp: ShowKeyboardHelp(); return true;
            default: return false;
        }
    }

    // ------------------------------------------------------------------ files

    private bool ConfirmDiscardChanges(string action)
    {
        if (!_editor.Session.IsDirty)
        {
            return true;
        }

        var name = _editor.Session.FilePath is { } p ? Path.GetFileName(p) : _editor.Project.Title;
        var answer = MessageBox.Show(this, $"Save changes to {name} before {action}?", "Accessible Video Editor",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => SaveProject(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void NewProject()
    {
        if (!ConfirmDiscardChanges("starting a new project"))
        {
            return;
        }

        var entries = Templates.List().Select(t => new ListEntry(t.Spoken, t, t.Category)).ToList();
        var dialog = new ListDialog(this, "New project", "Templates", entries, searchable: true,
            intro: "Choose a starting point: a built-in template or one you saved. Press Enter to create the project.");
        dialog.AddAction("C_reate");
        if (dialog.ShowDialog() != true || dialog.Chosen?.Tag is not TemplateInfo template)
        {
            return;
        }

        try
        {
            _shownLayerId = null;
            var feedback = _editor.Load(new ProjectSession(Templates.Create(template)), $"New project from template {template.Name}");
            _ = ApplyAsync(feedback);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _earcons.Play(EarconEvent.Error);
            Say("That template can't be opened. " + ex.Message);
        }
    }

    private void OpenProject()
    {
        if (!ConfirmDiscardChanges("opening another project"))
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Open project",
            Filter = ProjectFilter,
            InitialDirectory = Directory.Exists(AppPaths.Projects) ? AppPaths.Projects : "",
        };
        if (dialog.ShowDialog(this) == true)
        {
            OpenProjectFile(dialog.FileName, announce: true);
        }
    }

    internal bool OpenProjectFile(string path, bool announce)
    {
        try
        {
            var project = ProjectSerializer.Load(path);
            _shownLayerId = null;
            var feedback = _editor.Load(new ProjectSession(project, path), "Opened");
            var missing = GraphBuilder.MissingFiles(project.Layers);
            if (missing.Count > 0)
            {
                feedback = feedback with { Speech = feedback.Speech + $" {missing.Count} media files are missing. Press F7 for details." };
            }

            _settings.AddRecent(path);
            if (announce)
            {
                _ = ApplyAsync(feedback);
            }
            else
            {
                Refresh();
                UpdateTitle();
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            _earcons.Play(EarconEvent.Error);
            Say($"Could not open {Path.GetFileName(path)}. {ex.Message}");
            return false;
        }
    }

    internal bool SaveProject(bool saveAs)
    {
        var path = _editor.Session.FilePath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save project",
                Filter = ProjectFilter,
                DefaultExt = ProjectSerializer.FileExtension,
                FileName = SafeFileName(_editor.Project.Title) + ProjectSerializer.FileExtension,
                InitialDirectory = path is null ? AppPaths.Projects : Path.GetDirectoryName(path) ?? AppPaths.Projects,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            _editor.Session.Save(path);
            _settings.AddRecent(path);
            UpdateTitle();
            _earcons.Play(EarconEvent.Done);
            Say($"Saved {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _earcons.Play(EarconEvent.Error);
            Say($"Could not save. {ex.Message}");
            return false;
        }
    }

    private void Autosave()
    {
        if (!_editor.Session.IsDirty || SelfTestMode || !_settings.AutoSave)
        {
            return;
        }

        try
        {
            var copy = ProjectSerializer.Clone(_editor.Project);
            copy.Title = _editor.Project.Title;
            ProjectSerializer.Save(copy, AppPaths.AutosaveFile);
            File.WriteAllText(AppPaths.AutosaveFile + ".source", _editor.Session.FilePath ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log("Autosave failed: " + ex.Message);
        }
    }

    private bool OfferRecovery()
    {
        var answer = MessageBox.Show(this,
            "Accessible Video Editor did not close normally last time. Recover the unsaved project?",
            "Recover project", MessageBoxButton.YesNo, MessageBoxImage.Question);
        try
        {
            if (answer == MessageBoxResult.Yes)
            {
                var project = ProjectSerializer.Load(AppPaths.AutosaveFile);
                var source = File.Exists(AppPaths.AutosaveFile + ".source") ? File.ReadAllText(AppPaths.AutosaveFile + ".source").Trim() : "";
                _editor.Load(new ProjectSession(project, string.IsNullOrEmpty(source) ? null : source), "Recovered");
                _editor.Session.Execute("recover", _ => EditResult.Success("Recovered."));
                Refresh();
                UpdateTitle();
                return true;
            }

            File.Delete(AppPaths.AutosaveFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            AppPaths.Log("Recovery failed: " + ex.Message);
        }

        return false;
    }

    // ------------------------------------------------------------------ import

    private async Task ImportAsync(bool atPlayhead)
    {
        var dialog = new OpenFileDialog
        {
            Title = atPlayhead ? "Add files at the playhead" : "Add files to the end of the layer",
            Filter = MediaProbe.FileDialogFilter,
            Multiselect = true,
            InitialDirectory = Directory.Exists(_settings.LastFolder) ? _settings.LastFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        };
        if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        _settings.LastFolder = Path.GetDirectoryName(dialog.FileNames[0]) ?? "";
        RememberImported(dialog.FileNames);
        await AddFilesAsync(dialog.FileNames, atPlayhead);
    }

    internal async Task AddFilesAsync(IReadOnlyList<string> paths, bool atPlayhead)
    {
        if (Tools is null)
        {
            _earcons.Play(EarconEvent.Error);
            Say("FFmpeg was not found, so files can't be read. Reinstall the program.");
            return;
        }

        using var busy = Busy();
        if (paths.Count > 3)
        {
            Say($"Reading {paths.Count} files.");
        }

        var infos = new List<MediaInfo>();
        var problems = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                infos.Add(await MediaProbe.ProbeAsync(Tools, path));
            }
            catch (MediaProbeException ex)
            {
                problems.Add(ex.Message);
            }
        }

        RememberImported(infos.Select(i => i.Path));
        var feedback = _editor.AddMedia(infos, atPlayhead);
        if (problems.Count > 0)
        {
            feedback = feedback with
            {
                Speech = (feedback.Speech + " " + string.Join(" ", problems)).Trim(),
                Earcon = infos.Count == 0 ? EarconEvent.Error : feedback.Earcon,
            };
        }

        busy.Dispose();
        await ApplyAsync(feedback);
    }

    private async Task AddSoundEffectAsync()
    {
        var entries = new List<ListEntry>();
        try
        {
            var builtIn = AppPaths.BuiltInSoundEffects;
            foreach (var effect in SoundEffects.BuiltIn)
            {
                entries.Add(new ListEntry($"{effect.Name}. {effect.Description}", Path.Combine(builtIn, effect.FileName), effect.Category));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log("Built-in sounds unavailable: " + ex.Message);
        }

        if (Directory.Exists(AppPaths.UserSoundEffects))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.UserSoundEffects, "*.*", SearchOption.AllDirectories)
                         .Where(f => MediaProbe.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                         .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                var folder = Path.GetRelativePath(AppPaths.UserSoundEffects, Path.GetDirectoryName(file)!);
                var group = folder == "." ? "My sounds" : $"My sounds, {folder}";
                entries.Add(new ListEntry(Path.GetFileNameWithoutExtension(file), file, group));
            }
        }

        var player = new MediaPlayer();
        var dialog = new ListDialog(this, "Add sound effect or music", "Sounds", entries, searchable: true,
            intro: "All built-in sounds are original and free to use in any video. Your own sounds come from Documents, Accessible Video Editor, Sound Effects. Press Enter to add at the playhead, or use Preview to listen.");
        dialog.AddAction("_Add at playhead");
        dialog.AddAction("_Preview", entry =>
        {
            player.Stop();
            player.Open(new Uri((string)entry.Tag!));
            player.Play();
        }, closes: false);
        var ok = dialog.ShowDialog() == true;
        player.Stop();
        player.Close();
        if (ok && dialog.Chosen?.Tag is string path)
        {
            await AddFilesAsync([path], atPlayhead: true);
        }
    }

    private async Task AddColorCardAsync()
    {
        var form = new FormDialog(this, "Add colour card");
        form.AddIntro("A solid colour picture, inserted after the focused item. Use it as a title background or a pause.");
        var names = ColorCatalog.Colors.Select(c => c.Name).ToList();
        var color = form.AddChoice("Colour", names, 0);
        var length = form.AddTime("Length", 3, minimum: Timeline.MinItemDuration);
        if (form.ShowDialog() == true)
        {
            await ApplyAsync(_editor.AddColorCard(names[color()], length()));
        }
    }

    // ------------------------------------------------------------------ navigation dialogs

    private async Task GoToTimeAsync()
    {
        var form = new FormDialog(this, "Go to time");
        var time = form.AddTime("Time", _editor.Playhead, "Use plus or minus to move from the current time, for example +5.");
        if (form.ShowDialog() == true)
        {
            await ApplyAsync(_editor.GoToTime(time()));
        }
    }

    private async Task GoToLayerAsync()
    {
        if (_editor.Project.Layers.Count == 0)
        {
            await ApplyAsync(_editor.GoToLayer(1, listenOnly: false));
            return;
        }

        var form = new FormDialog(this, "Go to layer");
        var number = form.AddNumber("Layer number", _editor.FocusedLayerIndex + 1, 1, _editor.Project.Layers.Count);
        if (form.ShowDialog() == true)
        {
            await ApplyAsync(_editor.GoToLayer((int)Math.Round(number()), listenOnly: false));
        }
    }

    private async Task ShowMarkersAsync()
    {
        List<ListEntry> Entries() => _editor.Project.Markers
            .Select(m => new ListEntry($"{m.Name}, {_editor.Describer.Time(m.Time)}", m.Id))
            .ToList();

        if (_editor.Project.Markers.Count == 0)
        {
            _earcons.Play(EarconEvent.Edge);
            Say("There are no markers. Press B to add one at the playhead.");
            return;
        }

        var dialog = new ListDialog(this, "Markers", "Markers", Entries(),
            intro: "Enter jumps to the marker. Other buttons rename, delete, split or add a transition at the marker.");
        dialog.AddAction("_Go to");
        dialog.AddAction("_Rename or move");
        dialog.AddAction("_Split here");
        dialog.AddAction("_Transition here");
        dialog.AddAction("_Delete", entry =>
        {
            var feedback = _editor.DeleteMarker((Guid)entry.Tag!);
            _earcons.Play(feedback.Earcon ?? EarconEvent.Deleted);
            Say(feedback.Speech);
            dialog.SetEntries(Entries());
        }, closes: false);

        if (dialog.ShowDialog() != true || dialog.Chosen?.Tag is not Guid id)
        {
            Refresh();
            return;
        }

        var marker = _editor.Project.Markers.FirstOrDefault(m => m.Id == id);
        if (marker is null)
        {
            return;
        }

        switch (dialog.ChosenAction)
        {
            case "_Go to":
                await ApplyAsync(_editor.GoToMarker(id));
                break;
            case "_Rename or move":
                var form = new FormDialog(this, "Marker");
                var name = form.AddText("Name", marker.Name);
                var time = form.AddTime("Time", marker.Time);
                if (form.ShowDialog() == true)
                {
                    await ApplyAsync(_editor.UpdateMarker(id, name(), time()));
                }

                break;
            case "_Split here":
                await ApplyAsync(_editor.SplitAt(marker.Time));
                break;
            case "_Transition here":
                if (AskTransition("Transition at " + marker.Name, null) is { } spec)
                {
                    await ApplyAsync(_editor.SetTransitionAt(marker.Time, spec));
                }

                break;
        }
    }

    private async Task TransitionHereAsync()
    {
        if (_editor.FocusedLayer is null)
        {
            await ApplyAsync(_editor.SetTransitionAtPlayhead(new TransitionSpec()));
            return;
        }

        var layer = _editor.FocusedLayer;
        var index = Timeline.ItemAt(layer, _editor.Playhead);
        var current = index > 0 ? layer.Items[index].TransitionIn : null;
        if (AskTransition("Transition at the playhead", current) is { } spec)
        {
            await ApplyAsync(_editor.SetTransitionAtPlayhead(spec));
        }
    }

    /// <summary>Asks for a transition, suited to the focused layer's kind.</summary>
    private TransitionSpec? AskTransition(string title, TransitionSpec? current)
    {
        var isVideo = _editor.FocusedLayer?.Kind != LayerKind.Audio;
        current ??= _editor.FocusedLayer?.Defaults.Transition ?? new TransitionSpec();
        var form = new FormDialog(this, title);
        form.AddIntro("The transition goes into the item that starts at this join, on the focused layer.");
        Func<int>? video = null;
        if (isVideo)
        {
            video = form.AddChoice("Picture transition", TransitionCatalog.Video.Select(Describe).ToList(),
                IndexOf(TransitionCatalog.Video, current.Video));
        }

        var audio = form.AddChoice(isVideo ? "Sound crossfade" : "Crossfade", TransitionCatalog.Audio.Select(Describe).ToList(),
            IndexOf(TransitionCatalog.Audio, current.Audio == TransitionSpec.None && isVideo ? "tri" : current.Audio));
        var duration = form.AddTime("Length", current.Duration, minimum: 0.1);
        if (form.ShowDialog() != true)
        {
            return null;
        }

        return new TransitionSpec
        {
            Video = video is null ? TransitionSpec.None : TransitionCatalog.Video[video()].Value,
            Audio = TransitionCatalog.Audio[audio()].Value,
            Duration = duration(),
        };
    }

    private static string Describe(CatalogEntry entry) => $"{entry.Name}. {entry.Description}";

    private static int IndexOf(IReadOnlyList<CatalogEntry> list, string value) =>
        Math.Max(0, list.ToList().FindIndex(e => e.Value == value));

    // ------------------------------------------------------------------ options dialogs

    private static readonly string[] PositionNames = Enum.GetValues<PositionPreset>().Select(Describer.PositionName).ToArray();

    private static readonly int[] DecibelSteps = Enumerable.Range(-12, 25).ToArray();

    private static string[] DecibelNames(int frequency) =>
        DecibelSteps.Select(db => db == 0 ? "0 decibels (unchanged)" : $"{(db > 0 ? "plus" : "minus")} {Math.Abs(db)} decibels").ToArray();

    internal FormDialog? BuildItemOptions(out Action? apply)
    {
        apply = null;
        if (_editor.FocusedItem is not { } item || _editor.FocusedLayer is not { } layer)
        {
            return null;
        }

        var index = _editor.FocusedItemIndex;
        var isVideoLayer = layer.Kind == LayerKind.Video;
        var form = new FormDialog(this, $"Options for {item.Name}", tabs: true);

        form.AddPage("General");
        var name = form.AddText("Name", item.Name);
        Func<int>? color = null;
        var colorNames = ColorCatalog.Colors.Select(c => c.Name).ToList();
        if (item.Kind == ItemKind.Color)
        {
            color = form.AddChoice("Colour", colorNames, Math.Max(0, colorNames.FindIndex(c => c == item.ColorName)));
        }

        Func<double>? sourceIn = null;
        if (item.IsTimeLimited)
        {
            sourceIn = form.AddTime("Start from, in the file", item.SourceIn,
                $"Where in the file this item begins. The file is {TimeFormat.Words(item.SourceDuration)} long.");
        }

        var duration = form.AddTime("Length", item.Duration, item.IsTimeLimited ? $"At most {TimeFormat.Words(item.AvailableDuration)} from the start point." : null, Timeline.MinItemDuration);
        var gap = form.AddTime("Gap before", item.GapBefore, "Silence or empty picture before this item starts.");
        var fadeIn = form.AddTime("Fade in", item.FadeIn, "Picture and sound fade in over this time.");
        var fadeOut = form.AddTime("Fade out", item.FadeOut, "Picture and sound fade out over this time.");

        form.AddPage("Transition in");
        form.AddIntro(index == 0
            ? "This is the first item on the layer, so a transition into it has no effect."
            : "The transition from the previous item into this one. It plays only when there is no gap between them.");
        Func<int>? videoTransition = null;
        if (isVideoLayer)
        {
            videoTransition = form.AddChoice("Picture transition", TransitionCatalog.Video.Select(Describe).ToList(),
                IndexOf(TransitionCatalog.Video, item.TransitionIn.Video));
        }

        var audioTransition = form.AddChoice(isVideoLayer ? "Sound crossfade" : "Crossfade", TransitionCatalog.Audio.Select(Describe).ToList(),
            IndexOf(TransitionCatalog.Audio, item.TransitionIn.Audio));
        var transitionLength = form.AddTime("Transition length", item.TransitionIn.Duration, minimum: 0.1);

        Func<int>? position = null;
        Func<double>? size = null, opacity = null, customX = null, customY = null;
        Func<double>? cropLeft = null, cropRight = null, cropTop = null, cropBottom = null;
        Func<bool>? fill = null;
        if (isVideoLayer && (item.HasVideo || item.Kind is ItemKind.Text or ItemKind.Color))
        {
            form.AddPage("Picture");
            position = form.AddChoice("Position", PositionNames, (int)item.Placement.Position,
                "Where the picture sits. Full frame fills the screen; the others place a smaller picture.");
            size = form.AddNumber("Size, percent of the frame", item.Placement.SizePercent, 5, 100, "Ignored for full frame.");
            opacity = form.AddNumber("Opacity, percent", item.Placement.OpacityPercent, 0, 100, "100 is solid, 0 is invisible.");
            customX = form.AddNumber("Custom position, percent across", item.Placement.CustomXPercent, 0, 100, "Only for the Custom position: left edge of the picture.");
            customY = form.AddNumber("Custom position, percent down", item.Placement.CustomYPercent, 0, 100, "Only for the Custom position: top edge of the picture.");
            if (item.Kind != ItemKind.Text)
            {
                fill = form.AddCheck("Fill the frame (full frame only): cover it completely, cutting off edges", item.Placement.Fill);
                cropLeft = form.AddNumber("Crop from the left, percent", item.Placement.CropLeft, 0, 90);
                cropRight = form.AddNumber("Crop from the right, percent", item.Placement.CropRight, 0, 90);
                cropTop = form.AddNumber("Crop from the top, percent", item.Placement.CropTop, 0, 90);
                cropBottom = form.AddNumber("Crop from the bottom, percent", item.Placement.CropBottom, 0, 90);
            }
        }

        Func<string>? textContent = null;
        Func<int>? textFont = null, textColor = null, textBackground = null, textIn = null, textOut = null;
        Func<double>? textSize = null, textAnimLength = null;
        Func<bool>? textBold = null, textOutline = null;
        if (item.Kind == ItemKind.Text)
        {
            form.AddPage("Text");
            textContent = form.AddMultilineText("Text", item.Text.Content);
            textFont = form.AddChoice("Font", FontCatalog.Fonts.Select(f => $"{f.Name}. {f.Description}").ToList(),
                FontCatalog.Fonts.ToList().FindIndex(f => f.Name == FontCatalog.Find(item.Text.Font).Name));
            textSize = form.AddNumber("Text size, percent of the frame height", item.Text.SizePercent, 1, 50, "8 is a good title size, 5 a caption.");
            textColor = form.AddChoice("Text colour", colorNames, Math.Max(0, colorNames.FindIndex(c => c == item.Text.ColorName)));
            textBold = form.AddCheck("Bold", item.Text.Bold);
            textOutline = form.AddCheck("Dark outline around the letters (readable on any picture)", item.Text.Outline);
            textBackground = form.AddChoice("Background", Enum.GetValues<TextBackground>().Select(TextAnimationCatalog.BackgroundName).ToList(), (int)item.Text.Background);
            textIn = form.AddChoice("Comes in by", TextAnimationCatalog.All.Select(TextAnimationCatalog.InName).ToList(), (int)item.Text.AnimationIn);
            textOut = form.AddChoice("Goes out by", TextAnimationCatalog.All.Select(TextAnimationCatalog.OutName).ToList(), (int)item.Text.AnimationOut);
            textAnimLength = form.AddTime("Animation length", item.Text.AnimationDuration, minimum: 0.05);
        }

        Func<double>? volume = null;
        Func<bool>? mute = null;
        Func<int>? noise = null, hum = null, low = null, mid = null, high = null, voice = null;
        Func<bool>? rumble = null, normalize = null;
        if (item.HasAudio)
        {
            form.AddPage("Sound");
            volume = form.AddNumber("Volume, percent", Math.Round(item.Volume * 100), 0, 400, "100 is unchanged.");
            mute = form.AddCheck("Mute this item's sound", item.AudioMuted);
            noise = form.AddChoice("Noise reduction", Enum.GetValues<NoiseReduction>().Select(AudioFilters.NoiseReductionName).ToList(), (int)item.Audio.NoiseReduction);
            hum = form.AddChoice("Hum removal", Enum.GetValues<HumFilter>().Select(AudioFilters.HumName).ToList(), (int)item.Audio.Hum);
            rumble = form.AddCheck("Remove rumble and wind (below 80 hertz)", item.Audio.RemoveRumble);
            low = form.AddChoice($"Low, bass at {AudioFilters.LowFrequency} hertz", DecibelNames(AudioFilters.LowFrequency), item.Audio.LowDb + 12);
            mid = form.AddChoice($"Middle, at {AudioFilters.MidFrequency} hertz", DecibelNames(AudioFilters.MidFrequency), item.Audio.MidDb + 12);
            high = form.AddChoice($"High, treble at {AudioFilters.HighFrequency} hertz", DecibelNames(AudioFilters.HighFrequency), item.Audio.HighDb + 12);
            voice = form.AddChoice("Voice preset", Enum.GetValues<VoicePreset>().Select(AudioFilters.VoiceName).ToList(), (int)item.Audio.Voice);
            normalize = form.AddCheck("Even out loudness to a standard level", item.Audio.Normalize);
        }

        apply = () =>
        {
            var edited = item.Clone(newId: false);
            edited.Name = name();
            if (color is not null)
            {
                edited.ColorName = colorNames[color()];
            }

            if (sourceIn is not null)
            {
                edited.SourceIn = sourceIn();
            }

            edited.Duration = duration();
            edited.GapBefore = gap();
            edited.FadeIn = fadeIn();
            edited.FadeOut = fadeOut();
            edited.TransitionIn = new TransitionSpec
            {
                Video = videoTransition is null ? item.TransitionIn.Video : TransitionCatalog.Video[videoTransition()].Value,
                Audio = TransitionCatalog.Audio[audioTransition()].Value,
                Duration = transitionLength(),
            };
            if (position is not null)
            {
                edited.Placement = new Placement
                {
                    Position = (PositionPreset)position(),
                    SizePercent = size!(),
                    OpacityPercent = opacity!(),
                    CustomXPercent = customX!(),
                    CustomYPercent = customY!(),
                    Fill = fill?.Invoke() ?? false,
                    CropLeft = cropLeft?.Invoke() ?? 0,
                    CropRight = cropRight?.Invoke() ?? 0,
                    CropTop = cropTop?.Invoke() ?? 0,
                    CropBottom = cropBottom?.Invoke() ?? 0,
                };
            }

            if (textContent is not null)
            {
                edited.Text = new TextStyle
                {
                    Content = textContent(),
                    Font = FontCatalog.Fonts[textFont!()].Name,
                    SizePercent = textSize!(),
                    ColorName = colorNames[textColor!()],
                    Bold = textBold!(),
                    Outline = textOutline!(),
                    Background = (TextBackground)textBackground!(),
                    AnimationIn = (TextAnimation)textIn!(),
                    AnimationOut = (TextAnimation)textOut!(),
                    AnimationDuration = textAnimLength!(),
                };
            }

            if (volume is not null)
            {
                edited.Volume = volume() / 100;
                edited.AudioMuted = mute!();
                edited.Audio = new AudioCleanup
                {
                    NoiseReduction = (NoiseReduction)noise!(),
                    Hum = (HumFilter)hum!(),
                    RemoveRumble = rumble!(),
                    LowDb = DecibelSteps[low!()],
                    MidDb = DecibelSteps[mid!()],
                    HighDb = DecibelSteps[high!()],
                    Voice = (VoicePreset)voice!(),
                    Normalize = normalize!(),
                };
            }

            _pendingFeedback = _editor.UpdateFocusedItem(edited);
        };
        return form;
    }

    private Feedback? _pendingFeedback;

    private async Task ShowItemOptionsAsync()
    {
        var form = BuildItemOptions(out var apply);
        if (form is null)
        {
            _earcons.Play(EarconEvent.Error);
            Say(_editor.FocusedLayer is null ? "No layers." : "This layer is empty. Press Control I to add files.");
            return;
        }

        if (form.ShowDialog() == true && apply is not null)
        {
            apply();
            await ApplyPending();
        }
    }

    internal FormDialog? BuildLayerOptions(out Action? apply)
    {
        apply = null;
        if (_editor.FocusedLayer is not { } layer)
        {
            return null;
        }

        var d = layer.Defaults;
        var isVideo = layer.Kind == LayerKind.Video;
        var form = new FormDialog(this, $"Options for layer {_editor.FocusedLayerIndex + 1}, {layer.Name}", tabs: true);
        form.AddPage("Layer");
        var name = form.AddText("Name", layer.Name);
        var volume = form.AddNumber("Layer volume, percent", Math.Round(layer.Volume * 100), 0, 400, "Changes the sound of everything on this layer.");

        form.AddPage("New items");
        form.AddIntro("These settings are given to items you add to this layer from now on.");
        var imageLength = isVideo ? form.AddTime("Picture length", d.ImageDuration, minimum: Timeline.MinItemDuration) : null;
        var itemVolume = form.AddNumber("Item volume, percent", Math.Round(d.Volume * 100), 0, 400);
        Func<int>? video = null;
        if (isVideo)
        {
            video = form.AddChoice("Picture transition", TransitionCatalog.Video.Select(Describe).ToList(), IndexOf(TransitionCatalog.Video, d.Transition.Video));
        }

        var audio = form.AddChoice(isVideo ? "Sound crossfade" : "Crossfade", TransitionCatalog.Audio.Select(Describe).ToList(), IndexOf(TransitionCatalog.Audio, d.Transition.Audio));
        var transitionLength = form.AddTime("Transition length", d.Transition.Duration, minimum: 0.1);
        Func<int>? position = null;
        Func<double>? size = null;
        if (isVideo)
        {
            position = form.AddChoice("Position", PositionNames, (int)d.Placement.Position);
            size = form.AddNumber("Size, percent of the frame", d.Placement.SizePercent, 5, 100);
        }

        var noise = form.AddChoice("Noise reduction", Enum.GetValues<NoiseReduction>().Select(AudioFilters.NoiseReductionName).ToList(), (int)d.Audio.NoiseReduction);
        var voice = form.AddChoice("Voice preset", Enum.GetValues<VoicePreset>().Select(AudioFilters.VoiceName).ToList(), (int)d.Audio.Voice);
        var normalize = form.AddCheck("Even out loudness", d.Audio.Normalize);

        apply = () =>
        {
            var defaults = d.Clone();
            if (imageLength is not null)
            {
                defaults.ImageDuration = imageLength();
            }

            defaults.Volume = itemVolume() / 100;
            defaults.Transition = new TransitionSpec
            {
                Video = video is null ? TransitionSpec.None : TransitionCatalog.Video[video()].Value,
                Audio = TransitionCatalog.Audio[audio()].Value,
                Duration = transitionLength(),
            };
            if (position is not null)
            {
                defaults.Placement = new Placement { Position = (PositionPreset)position(), SizePercent = size!(), OpacityPercent = d.Placement.OpacityPercent };
            }

            defaults.Audio.NoiseReduction = (NoiseReduction)noise();
            defaults.Audio.Voice = (VoicePreset)voice();
            defaults.Audio.Normalize = normalize();
            _pendingFeedback = _editor.UpdateFocusedLayer(name(), volume() / 100, defaults);
        };
        return form;
    }

    private async Task ShowLayerOptionsAsync()
    {
        var form = BuildLayerOptions(out var apply);
        if (form is null)
        {
            _earcons.Play(EarconEvent.Error);
            Say("There are no layers. Add one with Control Shift V or Control Shift A.");
            return;
        }

        if (form.ShowDialog() == true && apply is not null)
        {
            apply();
            await ApplyPending();
        }
    }

    private static readonly (string Name, int Width, int Height)[] CanvasSizes =
    [
        ("1920 by 1080, HD landscape", 1920, 1080), ("1280 by 720, smaller HD", 1280, 720),
        ("3840 by 2160, 4K", 3840, 2160), ("1080 by 1920, vertical", 1080, 1920), ("1080 by 1080, square", 1080, 1080),
    ];

    private static readonly double[] FrameRates = [24, 25, 30, 50, 60];

    internal FormDialog BuildProjectSettings(out Action apply)
    {
        var p = _editor.Project;
        var form = new FormDialog(this, "Project settings");
        var title = form.AddText("Title", p.Title);
        var sizes = CanvasSizes.Select(s => s.Name).Append("Custom (use the width and height below)").ToList();
        var current = Array.FindIndex(CanvasSizes, s => s.Width == p.Width && s.Height == p.Height);
        var size = form.AddChoice("Picture size", sizes, current < 0 ? sizes.Count - 1 : current);
        var width = form.AddNumber("Custom width", p.Width, 16, 7680);
        var height = form.AddNumber("Custom height", p.Height, 16, 4320);
        var rateIndex = Array.FindIndex(FrameRates, r => Math.Abs(r - p.FrameRate) < 0.01);
        var rate = form.AddChoice("Frame rate", FrameRates.Select(r => $"{r} frames per second").ToList(), rateIndex < 0 ? 2 : rateIndex);
        apply = () =>
        {
            var chosen = size();
            var (w, h) = chosen < CanvasSizes.Length ? (CanvasSizes[chosen].Width, CanvasSizes[chosen].Height) : ((int)width(), (int)height());
            _pendingFeedback = _editor.UpdateProjectSettings(title(), w, h, FrameRates[rate()]);
        };
        return form;
    }

    private async Task ShowProjectSettingsAsync()
    {
        var form = BuildProjectSettings(out var apply);
        if (form.ShowDialog() == true)
        {
            apply();
            await ApplyPending();
        }
    }

    internal FormDialog BuildPreferences(out Action apply)
    {
        var s = _settings;
        var form = new FormDialog(this, "Preferences", tabs: true);
        form.AddPage("Speech");
        var verbosity = form.AddChoice("How much is spoken", ["Brief", "Normal", "Detailed"], (int)s.Verbosity);
        var timeStyle = form.AddChoice("Times are spoken as", ["Words, like 1 minute 5 seconds", "Clock, like 1:05.0"], (int)s.TimeStyle);
        var speech = form.AddChoice("Speech output", ["Automatic: talk to NVDA or JAWS directly, otherwise Windows", "Windows UI Automation only (for Narrator and others)"], (int)s.SpeechOutput);

        form.AddPage("Sounds");
        var themes = EarconPlayer.AvailableThemes();
        var themeNames = themes.Select(t => $"{t.Name}. {t.Description}").ToList();
        var themeIndex = Math.Max(0, themes.ToList().FindIndex(t => string.Equals(t.Name, s.EarconTheme, StringComparison.OrdinalIgnoreCase)));
        var preview = new EarconPlayer();
        var theme = form.AddChoice("Sound theme", themeNames, themeIndex, "Changing the choice plays a sample.", i =>
        {
            preview.Configure(themes[i].Name, s.EarconVolume);
            preview.Play(EarconEvent.Finished);
        });
        var volume = form.AddNumber("Sound volume, percent", s.EarconVolume, 0, 100);

        form.AddPage("Editing");
        var ripple = form.AddCheck("Ripple editing: later items move with deletes, trims and slides", s.Ripple);
        var slide = form.AddTime("Slide step for Alt J and Alt L", s.SlideStep, minimum: 0.01);
        var fine = form.AddTime("Fine slide step for Alt Shift J and Alt Shift L", s.FineSlideStep, minimum: 0.01);
        var jump = form.AddTime("Jump back step for J", s.JumpStep, minimum: 0.1);
        var autosave = form.AddCheck("Save a recovery copy every 20 seconds", s.AutoSave);

        form.AddPage("Playback and files");
        var previewLength = form.AddTime("Preview length rendered at a time", s.PreviewSeconds, "Longer previews take more time to prepare.", 10);
        var ffmpeg = form.AddText("FFmpeg folder (leave empty to use the built-in copy)", s.FfmpegFolder);

        apply = () =>
        {
            s.Verbosity = (Core.Text.Verbosity)verbosity();
            s.TimeStyle = (TimeStyle)timeStyle();
            s.SpeechOutput = (SpeechOutput)speech();
            s.EarconTheme = themes[theme()].Name;
            s.EarconVolume = (int)Math.Round(volume());
            s.Ripple = ripple();
            s.SlideStep = slide();
            s.FineSlideStep = fine();
            s.JumpStep = jump();
            s.AutoSave = autosave();
            s.PreviewSeconds = previewLength();
            s.FfmpegFolder = ffmpeg().Trim();
            s.Clamp();
            ApplySettings();
        };
        return form;
    }

    private void ShowPreferences()
    {
        var oldFolder = _settings.FfmpegFolder;
        var form = BuildPreferences(out var apply);
        if (form.ShowDialog() != true)
        {
            return;
        }

        apply();
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log("Saving preferences failed: " + ex.Message);
        }

        if (oldFolder != _settings.FfmpegFolder)
        {
            _ = ReloadFfmpegAsync();
        }

        _earcons.Play(EarconEvent.Done);
        Say("Preferences saved.");
    }

    private void ApplySettings()
    {
        _editor.Describer.Verbosity = _settings.Verbosity;
        _editor.Describer.TimeStyle = _settings.TimeStyle;
        _announcer.Mode = _settings.SpeechOutput;
        _earcons.Configure(_settings.EarconTheme, _settings.EarconVolume);
        _preview.WindowSeconds = _settings.PreviewSeconds;
        Refresh();
    }

    private async Task ReloadFfmpegAsync()
    {
        try
        {
            Tools = await FfmpegTools.LocateAsync(_settings.FfmpegFolder);
            _preview.Tools = Tools;
            Say($"Using {Tools.VersionText}.", interrupt: false);
        }
        catch (FfmpegNotFoundException ex)
        {
            Say(ex.Message, interrupt: false);
        }
    }

    private async Task ApplyPending()
    {
        if (_pendingFeedback is { } feedback)
        {
            _pendingFeedback = null;
            await ApplyAsync(feedback);
        }
    }

    // ------------------------------------------------------------------ export

    internal FormDialog BuildExportDialog(out Func<(OutputProfile Profile, bool RangeOnly)> result)
    {
        var profiles = OutputProfile.ExportProfiles;
        var form = new FormDialog(this, "Export");
        var muted = _editor.Project.Layers.Where(l => l.Muted).Select(l => l.Name).ToList();
        var intro = $"The project is {_editor.Describer.Time(_editor.ProjectDuration)} long.";
        if (muted.Count > 0)
        {
            intro += $" Muted layers are left out: {string.Join(", ", muted)}.";
        }

        form.AddIntro(intro);
        var current = Math.Max(0, profiles.ToList().FindIndex(p => p.Id == _settings.ExportProfileId));
        var format = form.AddChoice("Format", profiles.Select(p => $"{p.Name}. {p.Description}").ToList(), current);
        Func<bool>? range = null;
        if (_editor.Project.RangeStart is { } rs && _editor.Project.RangeEnd is { } re && re - rs > 0.1)
        {
            range = form.AddCheck($"Export only the range, {_editor.Describer.Time(rs)} to {_editor.Describer.Time(re)}", false);
        }

        result = () => (profiles[format()], range?.Invoke() ?? false);
        return form;
    }

    private async Task ExportAsync()
    {
        if (Tools is null)
        {
            _earcons.Play(EarconEvent.Error);
            Say("FFmpeg was not found, so the project can't be exported. Reinstall the program.");
            return;
        }

        var form = BuildExportDialog(out var result);
        if (form.ShowDialog() != true)
        {
            return;
        }

        var (profile, rangeOnly) = result();
        _settings.ExportProfileId = profile.Id;
        var save = new SaveFileDialog
        {
            Title = "Export to",
            Filter = $"{profile.Name}|*{profile.Extension}",
            DefaultExt = profile.Extension,
            FileName = SafeFileName(_editor.Project.Title) + profile.Extension,
            InitialDirectory = Environment.GetFolderPath(profile.HasVideo ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyMusic),
        };
        if (save.ShowDialog(this) != true)
        {
            return;
        }

        await ExportToAsync(profile, save.FileName, rangeOnly, showProgress: true);
    }

    /// <summary>Exports and reports the outcome. Returns true on success.</summary>
    internal async Task<bool> ExportToAsync(OutputProfile profile, string path, bool rangeOnly, bool showProgress)
    {
        RenderPlan plan;
        var project = _editor.Project;
        try
        {
            plan = new GraphBuilder(Tools!.HasEncoder, Tools!.Text).Build(new RenderRequest
            {
                Project = project,
                Profile = profile,
                OutputPath = path,
                Start = rangeOnly ? project.RangeStart ?? 0 : 0,
                End = rangeOnly ? project.RangeEnd : null,
            });
        }
        catch (RenderException ex)
        {
            _earcons.Play(EarconEvent.Error);
            Say(ex.Message);
            return false;
        }

        _preview.Pause();
        using var busy = Busy();
        using var cancel = new CancellationTokenSource();
        ProgressWindow? window = null;
        if (showProgress)
        {
            window = new ProgressWindow(this, $"Exporting {Path.GetFileName(path)}", text => Say(text, interrupt: false));
            window.Cancelled += cancel.Cancel;
            window.Show();
        }

        Say($"Exporting {_editor.Describer.Time(plan.Duration)}. Press Escape to cancel.");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var progress = new Progress<double>(p => window?.Report(p));
            await Task.Run(() => RenderRunner.RunAsync(Tools!, plan, path, progress, cancel.Token), cancel.Token);
            window?.Finish();
            var size = new FileInfo(path).Length / 1024.0 / 1024.0;
            _earcons.Play(EarconEvent.Finished);
            var message = $"Export finished in {TimeFormat.Words(stopwatch.Elapsed.TotalSeconds)}. {Path.GetFileName(path)}, {size:0.#} megabytes.";
            try
            {
                if (Credits.WriteFor(project, path) is { } creditsFile)
                {
                    message += $" Credits for the music and sounds are in {Path.GetFileName(creditsFile)}; copy them into your video description.";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppPaths.Log("Writing credits failed: " + ex.Message);
            }

            Say(message);
            if (showProgress && MessageBox.Show(this, message + " Open the folder?", "Export finished", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            window?.Finish();
            _earcons.Play(EarconEvent.Edge);
            Say("Export cancelled. The unfinished file was deleted.");
            return false;
        }
        catch (RenderException ex)
        {
            window?.Finish();
            _earcons.Play(EarconEvent.Error);
            Say("Export failed. " + ex.Message);
            AppPaths.Log("Export failed: " + ex.Message);
            return false;
        }
        finally
        {
            FocusTimeline();
        }
    }

    // ------------------------------------------------------------------ text and templates

    /// <summary>Asks for text and its style, then adds it at the playhead.</summary>
    internal async Task AddTextAsync(TextPreset? preset)
    {
        preset ??= TextPresets.All[0];
        var form = new FormDialog(this, "Add text", tabs: true);
        form.AddPage("Words");
        var presets = TextPresets.All.ToList();
        var content = form.AddMultilineText("Text", "", "The words to show. Several lines are fine.");
        var style = form.AddChoice("Style", presets.Select(p => $"{p.Name}. {p.Description}").ToList(), presets.IndexOf(preset),
            "Sets size, place and animation. You can change each one on the other tabs, and later with Enter on the item.");
        var length = form.AddTime("How long it stays on screen", preset.Duration, minimum: 0.5);

        form.AddPage("Look");
        var colorNames = ColorCatalog.Colors.Select(c => c.Name).ToList();
        var font = form.AddChoice("Font", FontCatalog.Fonts.Select(f => $"{f.Name}. {f.Description}").ToList(),
            FontCatalog.Fonts.ToList().FindIndex(f => f.Name == FontCatalog.Find(preset.Style.Font).Name));
        var color = form.AddChoice("Text colour", colorNames, Math.Max(0, colorNames.IndexOf(preset.Style.ColorName)));
        var useStyleLook = form.AddCheck("Use the style's size, colour, box and animation (untick to use the choices on this tab and the next)", true);
        var size = form.AddNumber("Text size, percent of the frame height", preset.Style.SizePercent, 1, 50);
        var bold = form.AddCheck("Bold", preset.Style.Bold);
        var background = form.AddChoice("Background", Enum.GetValues<TextBackground>().Select(TextAnimationCatalog.BackgroundName).ToList(), (int)preset.Style.Background);

        form.AddPage("Place and motion");
        var position = form.AddChoice("Position", PositionNames, (int)preset.Placement.Position);
        var animIn = form.AddChoice("Comes in by", TextAnimationCatalog.All.Select(TextAnimationCatalog.InName).ToList(), (int)preset.Style.AnimationIn);
        var animOut = form.AddChoice("Goes out by", TextAnimationCatalog.All.Select(TextAnimationCatalog.OutName).ToList(), (int)preset.Style.AnimationOut);
        form.ExtraValidation = () => string.IsNullOrWhiteSpace(content()) ? "Type the text to show on the Words tab." : null;
        if (form.ShowDialog() != true)
        {
            return;
        }

        var chosen = presets[style()];
        TextStyle textStyle;
        Placement placement;
        if (useStyleLook())
        {
            textStyle = chosen.Style.Clone();
            placement = chosen.Placement.Clone();
            textStyle.Font = FontCatalog.Fonts[font()].Name;
            if (colorNames[color()] != preset.Style.ColorName)
            {
                textStyle.ColorName = colorNames[color()];
            }
        }
        else
        {
            textStyle = chosen.Style.Clone();
            textStyle.Font = FontCatalog.Fonts[font()].Name;
            textStyle.ColorName = colorNames[color()];
            textStyle.SizePercent = size();
            textStyle.Bold = bold();
            textStyle.Background = (TextBackground)background();
            textStyle.AnimationIn = (TextAnimation)animIn();
            textStyle.AnimationOut = (TextAnimation)animOut();
            placement = new Placement { Position = (PositionPreset)position() };
        }

        textStyle.Content = content().TrimEnd();
        await ApplyAsync(_editor.AddText(textStyle, length(), placement));
        if (Tools is { } tools && !tools.Text.Available)
        {
            Say("Warning: this copy of FFmpeg can't draw text, so the text won't show in previews or exports.", interrupt: false);
        }
    }

    private async Task EditTextOrRenameAsync()
    {
        if (_editor.FocusedItem is not { } item)
        {
            _earcons.Play(EarconEvent.Error);
            Say(_editor.FocusedLayer is null ? "No layers." : "This layer is empty.");
            return;
        }

        var form = new FormDialog(this, item.Kind == ItemKind.Text ? "Edit text" : "Rename");
        var value = item.Kind == ItemKind.Text ? form.AddMultilineText("Text", item.Text.Content) : form.AddText("Name", item.Name);
        if (form.ShowDialog() != true)
        {
            return;
        }

        var edited = item.Clone(newId: false);
        if (item.Kind == ItemKind.Text)
        {
            if (string.IsNullOrWhiteSpace(value()))
            {
                Say("Text can't be empty. Delete the item instead if you don't want it.");
                return;
            }

            edited.Text.Content = value().TrimEnd();
        }
        else
        {
            edited.Name = value();
        }

        await ApplyAsync(_editor.UpdateFocusedItem(edited));
    }

    private void SaveAsTemplate()
    {
        var form = new FormDialog(this, "Save as template");
        form.AddIntro("Templates keep the layers, their default settings, text and colour cards. Choose whether to keep the media files too; media only works on a computer that has the same files.");
        var name = form.AddText("Template name", _editor.Project.Title);
        var folders = Templates.Categories().ToList();
        var folder = form.AddChoice("Folder", folders, 0);
        var newFolder = form.AddText("Or a new folder name", "", "Leave empty to use the folder chosen above.");
        var description = form.AddText("Description", "", "A sentence to remind you what this template is for.");
        var media = form.AddCheck("Keep the video, picture and sound files in the template", false);
        form.ExtraValidation = () => string.IsNullOrWhiteSpace(name()) ? "Give the template a name." : null;
        if (form.ShowDialog() != true)
        {
            return;
        }

        var category = string.IsNullOrWhiteSpace(newFolder()) ? folders[folder()] : newFolder().Trim();
        try
        {
            var saved = Templates.Save(_editor.Project, name(), category, description(), media());
            FillTemplates();
            _earcons.Play(EarconEvent.Done);
            Say($"Saved template {saved.Name} in {saved.Category}. Find it with Control N or in the library's Templates tab.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _earcons.Play(EarconEvent.Error);
            Say("Could not save the template. " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ information

    private async Task ShowProjectCheckAsync()
    {
        var findings = ProjectChecker.Check(_editor.Project);
        var entries = findings.Select(f => new ListEntry(f.Spoken, f)).ToList();
        var dialog = new ListDialog(this, "Project check", "Findings", entries,
            intro: findings.Count == 1 ? null : $"{findings.Count} findings. Press Enter on one to go to it.");
        dialog.AddAction("_Go to");
        if (dialog.ShowDialog() == true && dialog.Chosen?.Tag is CheckFinding finding)
        {
            if (finding.LayerId is { } layerId)
            {
                _editor.FocusLayer(layerId);
            }

            if (finding.Time is { } time)
            {
                await ApplyAsync(_editor.GoToTime(time));
            }
            else
            {
                await ApplyAsync(new Feedback { Speech = _editor.FocusedItemText() });
            }
        }
    }

    internal ListDialog BuildKeyboardHelp()
    {
        var entries = CommandCatalog.All.Select(c =>
        {
            var keys = _keymap.KeysFor(c.Id);
            var spoken = keys.Count == 0 ? "no key" : string.Join(" or ", keys.Select(k => k.Spoken()));
            return new ListEntry($"{c.Title}: {spoken}. {c.Help}", c, c.Category);
        }).ToList();
        return new ListDialog(this, "Keyboard help", "Commands", entries, searchable: true,
            intro: "Type in the search box to find a command, then Down arrow to the list. Letter keys without Control or Alt work in the timeline and the layer list.");
    }

    private void ShowKeyboardHelp()
    {
        var dialog = BuildKeyboardHelp();
        dialog.AddAction("_Run command");
        if (dialog.ShowDialog() == true && dialog.Chosen?.Tag is CommandInfo command && command.Id != CommandId.KeyboardHelp)
        {
            _ = RunAsync(command.Id);
        }
    }

    private void ShowGettingStarted()
    {
        const string text =
            "Getting started.\n\n" +
            "The window has two lists. The timeline shows the items on one layer, from left to right in time. The layer list shows every layer.\n\n" +
            "Left and Right arrows move between items; the playhead moves to the start of each item. Up and Down arrows move to the layer above or below, at the same time. Alt 1 to Alt 9 jump to layers 1 to 9 and listen to that layer alone; Alt 0 listens to all layers again. Control L goes to any layer number.\n\n" +
            "K or Space plays and pauses, L plays faster, J jumps back. T says the time, W says what is on every layer at the playhead.\n\n" +
            "Control I adds video, picture or audio files. S splits at the playhead, Delete removes an item, Enter opens all options for an item: length, volume, fades, transitions, position and sound clean-up.\n\n" +
            "Left bracket and right bracket mark a range; Shift Delete removes it. B adds a marker; Alt Left and Alt Right jump between markers. Alt J and Alt L slide an item in time; Alt Shift Left and Alt Shift Right change its order.\n\n" +
            "Control E exports the finished video. Control Z undoes anything. F1 lists every key.";
        MessageBox.Show(this, text, "Getting started", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowAbout()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        var ffmpeg = Tools?.VersionText ?? "FFmpeg not found";
        MessageBox.Show(this,
            $"Accessible Video Editor {version}\nA screen-reader-first video editor.\nSpeech: {_announcer.ChannelName}.\n{ffmpeg}.\nLicensed under the GNU General Public License version 3 or later.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ------------------------------------------------------------------ tools

    private void ExportCurrentTheme()
    {
        var theme = EarconPlayer.AvailableThemes().FirstOrDefault(t => t.Name == _earcons.ThemeName) ?? EarconTheme.BuiltIn[0];
        var folder = Path.Combine(AppPaths.UserSoundThemes, theme.Name + " copy");
        var n = 2;
        while (Directory.Exists(folder))
        {
            folder = Path.Combine(AppPaths.UserSoundThemes, $"{theme.Name} copy {n++}");
        }

        try
        {
            theme.Export(folder);
            Say($"Copied the {theme.Name} theme to {Path.GetFileName(folder)} in your Sound Themes folder. Replace the WAV files with your own, then choose it in Preferences.");
            OpenFolder(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("Could not copy the theme. " + ex.Message);
        }
    }

    private void OpenKeymapFile()
    {
        try
        {
            if (!File.Exists(AppPaths.KeymapFile))
            {
                _keymap.Save(AppPaths.KeymapFile);
            }

            OpenFile(AppPaths.KeymapFile);
            Say("Edit the keys, save the file, and restart the program to use them.", interrupt: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("Could not open the keyboard file. " + ex.Message);
        }
    }

    private void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Say("Could not open the folder. " + ex.Message);
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Say("Could not open the file. " + ex.Message);
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "Untitled" : cleaned;
    }
}
