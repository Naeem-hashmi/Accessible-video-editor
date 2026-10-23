using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AccessibleVideoEditor.App.Services;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Media;
using AccessibleVideoEditor.Core.Online;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Templates;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App;

/// <summary>The library area on the left: media, sounds, online music, text styles and templates.</summary>
public partial class MainWindow
{
    public enum LibraryTab
    {
        Media,
        Sounds,
        OnlineMusic,
        Text,
        Templates,
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private SoundPreviewPlayer? _soundPlayer;

    private SoundPreviewPlayer SoundPlayer => _soundPlayer ??= new SoundPreviewPlayer(() => _preview.Tools);
    private readonly List<string> _importedFiles = new();
    private ListBox _mediaList = null!;
    private ListBox _soundList = null!;
    private TextBox _soundSearch = null!;
    private ListBox _onlineList = null!;
    private TextBox _onlineQuery = null!;
    private ComboBox _onlineCategory = null!;
    private TextBlock _onlineStatus = null!;
    private ListBox _textList = null!;
    private ListBox _templateList = null!;
    private int _onlinePage = 1;
    private OpenverseClient? _openverse;

    internal TemplateLibrary Templates { get; } = new(Path.Combine(AppPaths.Documents, "Templates"));

    private void BuildLibrary()
    {
        LibraryButtons.Children.Add(LibraryButton("Import files...", () => _ = RunAsync(CommandId.ImportToEnd), "Control I"));
        LibraryButtons.Children.Add(LibraryButton("Export video...", () => _ = RunAsync(CommandId.Export), "Control E"));
        LibraryButtons.Children.Add(LibraryButton("Save project", () => _ = RunAsync(CommandId.SaveProject), "Control S"));

        // Media in this project
        var media = TabPanel(LibraryTab.Media, "Media", "Files used in this project and files you imported. Enter adds to the end of the focused layer, Shift Enter at the playhead.");
        _mediaList = LibraryList("Project media");
        _mediaList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return && _mediaList.SelectedItem is ListBoxItem { Tag: string path })
            {
                e.Handled = true;
                _ = AddFilesAsync([path], atPlayhead: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            }
        };
        media.Children.Add(_mediaList);
        media.Children.Add(LibraryButton("Add to end of layer", () => AddSelected(_mediaList, false)));
        media.Children.Add(LibraryButton("Add at playhead", () => AddSelected(_mediaList, true)));

        // Built-in and user sounds
        var sounds = TabPanel(LibraryTab.Sounds, "Sounds", "Real recordings (typing, clicks, bells, applause, rain and more) and built-in sounds, all free for any use. Your own go in Documents, ClipScribe, Sound Effects. Space previews, Enter adds at the playhead.");
        _soundSearch = LabelledBox(sounds, "Search sounds");
        _soundSearch.TextChanged += (_, _) => FillSounds();
        _soundList = LibraryList("Sounds");
        WirePreviewAndAdd(_soundList, entry => (string)entry);
        sounds.Children.Add(_soundList);
        sounds.Children.Add(LibraryButton("Open my sounds folder", () => OpenFolder(AppPaths.UserSoundEffects)));

        // Openverse
        var online = TabPanel(LibraryTab.OnlineMusic, "Free music online",
            "Searches Openverse for music and sound effects that are public domain or CC BY. Both may be used in any video, including monetised ones; CC BY needs a credit, which is saved with the project and written next to your export. Needs an internet connection.");
        _onlineQuery = LabelledBox(online, "Search for");
        _onlineQuery.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                _ = SearchOnlineAsync(newSearch: true);
            }
        };
        _onlineCategory = new ComboBox();
        foreach (var name in new[] { "Music", "Sound effects" })
        {
            var entry = new ComboBoxItem { Content = name };
            AutomationProperties.SetName(entry, name);
            _onlineCategory.Items.Add(entry);
        }

        _onlineCategory.SelectedIndex = 0;
        AddLabelled(online, "Kind", _onlineCategory);
        online.Children.Add(LibraryButton("Search", () => _ = SearchOnlineAsync(newSearch: true)));
        _onlineStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        online.Children.Add(_onlineStatus);
        _onlineList = LibraryList("Results");
        _onlineList.PreviewKeyDown += (_, e) =>
        {
            if (_onlineList.SelectedItem is not ListBoxItem { Tag: OnlineAudio audio })
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                PreviewUri(new Uri(audio.Url), audio.Title);
            }
            else if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                _ = DownloadAndAddAsync(audio);
            }
        };
        online.Children.Add(_onlineList);
        online.Children.Add(LibraryButton("Preview", () =>
        {
            if (_onlineList.SelectedItem is ListBoxItem { Tag: OnlineAudio audio })
            {
                PreviewUri(new Uri(audio.Url), audio.Title);
            }
        }));
        online.Children.Add(LibraryButton("Download and add at playhead", () =>
        {
            if (_onlineList.SelectedItem is ListBoxItem { Tag: OnlineAudio audio })
            {
                _ = DownloadAndAddAsync(audio);
            }
        }));
        online.Children.Add(LibraryButton("Open the source page", () =>
        {
            if (_onlineList.SelectedItem is ListBoxItem { Tag: OnlineAudio audio })
            {
                OpenUrl(audio.LandingUrl);
            }
        }));
        online.Children.Add(LibraryButton("More results", () => _ = SearchOnlineAsync(newSearch: false)));

        // Text styles
        var text = TabPanel(LibraryTab.Text, "Text", "Ready-made text styles. Enter adds text in that style at the playhead.");
        _textList = LibraryList("Text styles");
        foreach (var preset in TextPresets.All)
        {
            _textList.Items.Add(Entry($"{preset.Name}. {preset.Description}", preset));
        }

        _textList.SelectedIndex = 0;
        _textList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return && _textList.SelectedItem is ListBoxItem { Tag: TextPreset preset })
            {
                e.Handled = true;
                _ = AddTextAsync(preset);
            }
        };
        text.Children.Add(_textList);
        text.Children.Add(LibraryButton("Add text in this style", () =>
        {
            if (_textList.SelectedItem is ListBoxItem { Tag: TextPreset preset })
            {
                _ = AddTextAsync(preset);
            }
        }));

        // Templates
        var templates = TabPanel(LibraryTab.Templates, "Templates", "Built-in templates and your own, by folder. Enter starts a new project from the template.");
        _templateList = LibraryList("Templates");
        _templateList.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return && _templateList.SelectedItem is ListBoxItem { Tag: TemplateInfo t })
            {
                e.Handled = true;
                NewProjectFrom(t);
            }
            else if (e.Key == Key.Delete && _templateList.SelectedItem is ListBoxItem { Tag: TemplateInfo d })
            {
                e.Handled = true;
                DeleteTemplate(d);
            }
        };
        templates.Children.Add(_templateList);
        templates.Children.Add(LibraryButton("New project from template", () => WithTemplate(NewProjectFrom)));
        templates.Children.Add(LibraryButton("Save this project as a template...", () => _ = RunAsync(CommandId.SaveAsTemplate)));
        templates.Children.Add(LibraryButton("Rename...", () => WithTemplate(RenameTemplate)));
        templates.Children.Add(LibraryButton("Move to folder...", () => WithTemplate(MoveTemplate)));
        templates.Children.Add(LibraryButton("Delete", () => WithTemplate(DeleteTemplate)));
        templates.Children.Add(LibraryButton("Import a template file...", ImportTemplate));
        templates.Children.Add(LibraryButton("Open the templates folder", () => OpenFolder(Templates.Root)));

        FillSounds();
        FillTemplates();
        RefreshLibraryMedia();
    }

    /// <summary>Moves to the library, optionally to one tab, and focuses its first control.</summary>
    internal void FocusLibrary(LibraryTab? tab)
    {
        if (tab is { } t)
        {
            LibraryTabs.SelectedIndex = (int)t;
        }

        LibraryTabs.UpdateLayout();
        if (LibraryTabs.SelectedItem is TabItem { Content: ScrollViewer { Content: StackPanel panel } } item)
        {
            var target = panel.Children.OfType<Control>().FirstOrDefault(c => c is TextBox or ListBox or ComboBox);
            if (target is ListBox list && list.Items.Count > 0)
            {
                list.UpdateLayout();
                (list.ItemContainerGenerator.ContainerFromIndex(Math.Max(0, list.SelectedIndex)) as ListBoxItem ?? (Control)list).Focus();
            }
            else
            {
                (target ?? (Control)item).Focus();
            }

            Say($"Library, {AutomationProperties.GetName(item)}.", interrupt: false);
        }
    }

    internal void RefreshLibraryMedia()
    {
        if (_mediaList is null)
        {
            return;
        }

        var selected = (_mediaList.SelectedItem as ListBoxItem)?.Tag as string;
        var paths = _editor.Project.Layers.SelectMany(l => l.Items).Where(i => !string.IsNullOrEmpty(i.Path)).Select(i => i.Path)
            .Concat(_importedFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var signature = string.Join("|", paths);
        if (signature == (_mediaList.Tag as string))
        {
            return;
        }

        _mediaList.Tag = signature;
        _mediaList.Items.Clear();
        foreach (var path in paths)
        {
            var uses = _editor.Project.Layers.SelectMany(l => l.Items).Count(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            var missing = File.Exists(path) ? "" : ", missing";
            _mediaList.Items.Add(Entry($"{Path.GetFileName(path)}, used {uses} {(uses == 1 ? "time" : "times")}{missing}", path));
        }

        if (_mediaList.Items.Count == 0)
        {
            _mediaList.Items.Add(Entry("No media yet. Press Control I to import files.", null));
        }

        var index = paths.FindIndex(p => p == selected);
        _mediaList.SelectedIndex = Math.Max(0, index);
    }

    internal void RememberImported(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!_importedFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _importedFiles.Add(path);
            }
        }
    }

    private void FillSounds()
    {
        var filter = _soundSearch.Text.Trim();
        _soundList.Items.Clear();
        foreach (var sound in RecordedSounds.Load(AppPaths.Recordings))
        {
            var text = "Real recording: " + sound.Spoken;
            if (filter.Length == 0 || text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                _soundList.Items.Add(Entry(text, RecordedSounds.FullPath(AppPaths.Recordings, sound)));
            }
        }

        try
        {
            var builtIn = AppPaths.BuiltInSoundEffects;
            foreach (var effect in SoundEffects.BuiltIn)
            {
                var text = $"{effect.Name}, {effect.Category}. {effect.Description}";
                if (filter.Length == 0 || text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                {
                    _soundList.Items.Add(Entry(text, Path.Combine(builtIn, effect.FileName)));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log("Built-in sounds unavailable: " + ex.Message);
        }

        if (Directory.Exists(AppPaths.UserSoundEffects))
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.UserSoundEffects, "*.*", SearchOption.AllDirectories)
                         .Where(f => MediaProbe.AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())))
            {
                var text = $"{Path.GetFileNameWithoutExtension(file)}, my sounds";
                if (filter.Length == 0 || text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
                {
                    _soundList.Items.Add(Entry(text, file));
                }
            }
        }

        if (_soundList.Items.Count > 0)
        {
            _soundList.SelectedIndex = 0;
        }
    }

    internal void FillTemplates()
    {
        _templateList.Items.Clear();
        foreach (var template in Templates.List())
        {
            _templateList.Items.Add(Entry(template.Spoken, template));
        }

        if (_templateList.Items.Count > 0)
        {
            _templateList.SelectedIndex = 0;
        }
    }

    // ------------------------------------------------------------------ online music

    internal async Task SearchOnlineAsync(bool newSearch)
    {
        _openverse ??= new OpenverseClient(Http);
        var query = _onlineQuery.Text.Trim();
        _onlinePage = newSearch ? 1 : _onlinePage + 1;
        var category = _onlineCategory.SelectedIndex == 1 ? OnlineAudioCategory.SoundEffect : OnlineAudioCategory.Music;
        _onlineStatus.Text = "Searching...";
        Say($"Searching for {query}.", interrupt: false);
        try
        {
            var result = await _openverse.SearchAsync(query, category, _onlinePage);
            if (newSearch)
            {
                _onlineList.Items.Clear();
            }

            foreach (var audio in result.Items)
            {
                _onlineList.Items.Add(Entry(audio.Spoken, audio));
            }

            var message = result.Items.Count == 0
                ? newSearch ? $"Nothing found for {query}. Try other words." : "No more results."
                : $"{result.Items.Count} results{(newSearch ? $" of {result.TotalCount}" : " more")}. Space previews, Enter downloads and adds at the playhead.";
            _onlineStatus.Text = message;
            Say(message);
            if (_onlineList.Items.Count > 0)
            {
                _onlineList.SelectedIndex = newSearch ? 0 : Math.Max(0, _onlineList.Items.Count - result.Items.Count);
                _onlineList.UpdateLayout();
                (_onlineList.ItemContainerGenerator.ContainerFromIndex(_onlineList.SelectedIndex) as ListBoxItem)?.Focus();
            }
        }
        catch (Exception ex) when (ex is OnlineSearchException or HttpRequestException or TaskCanceledException)
        {
            var message = ex is OnlineSearchException ? ex.Message : "Could not reach Openverse. Check the internet connection and try again.";
            _onlineStatus.Text = message;
            _earcons.Play(EarconEvent.Error);
            Say(message);
        }
    }

    private async Task DownloadAndAddAsync(OnlineAudio audio)
    {
        _openverse ??= new OpenverseClient(Http);
        _soundPlayer?.Stop();
        Say($"Downloading {audio.Title}.", interrupt: false);
        try
        {
            var folder = Path.Combine(AppPaths.UserSoundEffects, "Downloaded");
            var path = await _openverse.DownloadAsync(audio, folder);
            await AddFilesAsync([path], atPlayhead: true);
            if (!audio.IsPublicDomain)
            {
                Say($"Credit saved: {audio.Credit}", interrupt: false);
            }
        }
        catch (Exception ex) when (ex is OnlineSearchException or HttpRequestException or TaskCanceledException or IOException)
        {
            _earcons.Play(EarconEvent.Error);
            Say(ex is OnlineSearchException ? ex.Message : "The download failed. Try again or choose another result.");
        }
    }

    // ------------------------------------------------------------------ templates

    private void WithTemplate(Action<TemplateInfo> action)
    {
        if (_templateList.SelectedItem is ListBoxItem { Tag: TemplateInfo t })
        {
            action(t);
        }
    }

    private void NewProjectFrom(TemplateInfo template)
    {
        if (!ConfirmDiscardChanges("starting a new project"))
        {
            return;
        }

        try
        {
            var project = Templates.Create(template);
            _shownLayerId = null;
            _ = ApplyAsync(_editor.Load(new Core.Editing.ProjectSession(project), $"New project from template {template.Name}"));
            FocusTimeline();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _earcons.Play(EarconEvent.Error);
            Say("That template can't be opened. " + ex.Message);
        }
    }

    private void RenameTemplate(TemplateInfo template)
    {
        if (template.IsBuiltIn)
        {
            Say("Built-in templates can't be renamed. Start a project from it and save that as your own template.");
            return;
        }

        var form = new Dialogs.FormDialog(this, "Rename template");
        var name = form.AddText("New name", template.Name);
        if (form.ShowDialog() == true)
        {
            TemplateAction(() => Templates.Rename(template, name()), $"Renamed to {name()}.");
        }
    }

    private void MoveTemplate(TemplateInfo template)
    {
        if (template.IsBuiltIn)
        {
            Say("Built-in templates can't be moved.");
            return;
        }

        var form = new Dialogs.FormDialog(this, "Move template");
        var folders = Templates.Categories().ToList();
        var folder = form.AddChoice("Folder", folders, Math.Max(0, folders.IndexOf(template.Category)));
        var newFolder = form.AddText("Or a new folder name", "");
        if (form.ShowDialog() == true)
        {
            var target = string.IsNullOrWhiteSpace(newFolder()) ? folders[folder()] : newFolder().Trim();
            TemplateAction(() => Templates.Move(template, target), $"Moved to {target}.");
        }
    }

    private void DeleteTemplate(TemplateInfo template)
    {
        if (template.IsBuiltIn)
        {
            Say("Built-in templates can't be deleted.");
            return;
        }

        if (MessageBox.Show(this, $"Delete the template {template.Name}?", "Delete template", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            TemplateAction(() => Templates.Delete(template), $"Deleted template {template.Name}.");
        }
    }

    private void ImportTemplate()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a template",
            Filter = $"Templates (*{TemplateLibrary.FileExtension})|*{TemplateLibrary.FileExtension}",
        };
        if (dialog.ShowDialog(this) == true)
        {
            TemplateAction(() => Templates.Import(dialog.FileName, TemplateLibrary.DefaultCategory), "Template imported into My templates.");
        }
    }

    private void TemplateAction(Action action, string done)
    {
        try
        {
            action();
            FillTemplates();
            _earcons.Play(EarconEvent.Done);
            Say(done);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
        {
            _earcons.Play(EarconEvent.Error);
            Say(ex.Message);
        }
    }

    // ------------------------------------------------------------------ helpers

    private void AddSelected(ListBox list, bool atPlayhead)
    {
        if (list.SelectedItem is ListBoxItem { Tag: string path })
        {
            _ = AddFilesAsync([path], atPlayhead);
        }
    }

    private void WirePreviewAndAdd(ListBox list, Func<object, string> path)
    {
        list.PreviewKeyDown += (_, e) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: { } tag })
            {
                return;
            }

            if (e.Key == Key.Space)
            {
                e.Handled = true;
                PreviewUri(new Uri(path(tag)), null);
            }
            else if (e.Key is Key.Enter or Key.Return)
            {
                e.Handled = true;
                _soundPlayer?.Stop();
                _ = AddFilesAsync([path(tag)], atPlayhead: true);
            }
        };
    }

    private void PreviewUri(Uri uri, string? name)
    {
        if (!SoundPlayer.Play(uri.IsFile ? uri.LocalPath : uri.AbsoluteUri))
        {
            Say("This sound can't be previewed.");
        }
        else if (name is not null)
        {
            Say($"Playing {name}. Space again restarts; Enter adds it.", interrupt: false);
        }
    }

    private StackPanel TabPanel(LibraryTab tab, string header, string intro)
    {
        var panel = new StackPanel { Margin = new Thickness(4) };
        panel.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        // An icon above the name, like the activity bar in VS Code.
        var icon = tab switch
        {
            LibraryTab.Media => Icons.Video,
            LibraryTab.Sounds => Icons.Music,
            LibraryTab.OnlineMusic => Icons.Search,
            LibraryTab.Text => Icons.Text,
            _ => Icons.Templates,
        };
        var head = new StackPanel { Width = 64 };
        var glyph = IconBlock(icon, 22);
        glyph.Margin = new Thickness(0, 2, 0, 2);
        head.Children.Add(glyph);
        head.Children.Add(new TextBlock { Text = header, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
        var item = new TabItem { Header = head, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false } };
        AutomationProperties.SetName(item, header);
        AutomationProperties.SetHelpText(item, intro);
        LibraryTabs.Items.Insert(Math.Min((int)tab, LibraryTabs.Items.Count), item);
        return panel;
    }

    private static ListBox LibraryList(string name)
    {
        var list = new ListBox { MinHeight = 160, MaxHeight = 360, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(list, name);
        TextSearch.SetTextPath(list, "Content");
        return list;
    }

    private static ListBoxItem Entry(string text, object? tag)
    {
        var item = new ListBoxItem { Content = text, Tag = tag };
        AutomationProperties.SetName(item, text);
        return item;
    }

    private static TextBox LabelledBox(Panel panel, string label)
    {
        var box = new TextBox();
        AddLabelled(panel, label, box);
        return box;
    }

    private static void AddLabelled(Panel panel, string label, Control control)
    {
        var text = new Label { Content = label + ":", Target = control, Padding = new Thickness(0, 4, 0, 1) };
        AutomationProperties.SetName(control, label);
        AutomationProperties.SetLabeledBy(control, text);
        panel.Children.Add(text);
        panel.Children.Add(control);
    }

    private static Button LibraryButton(string text, Action click, string? shortcut = null)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 2, 6, 2), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, text.Replace("_", "").Replace("...", ""));
        if (shortcut is not null)
        {
            AutomationProperties.SetAcceleratorKey(button, shortcut);
        }

        button.Click += (_, _) => click();
        return button;
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Say("Could not open the web page. " + ex.Message);
        }
    }
}
