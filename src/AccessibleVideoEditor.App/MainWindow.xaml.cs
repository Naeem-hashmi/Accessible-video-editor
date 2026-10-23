using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AccessibleVideoEditor.App.Services;
using AccessibleVideoEditor.App.ViewModels;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Input;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Rendering;
using AccessibleVideoEditor.Core.Settings;
using AccessibleVideoEditor.Core.Sound;
using AccessibleVideoEditor.Core.Templates;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App;

/// <summary>
/// The main window. It turns key presses into commands for <see cref="EditorController"/>, keeps the two
/// lists (layers and timeline items) in step with the project, moves keyboard focus, speaks feedback,
/// plays earcons and drives the preview player. All editing rules live in the core library.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Keymap _keymap;
    private readonly Dictionary<(Key, ModifierKeys), CommandId> _keys = new();
    private readonly ObservableCollection<SpokenViewModel> _layers = new();
    private readonly ObservableCollection<SpokenViewModel> _items = new();
    private readonly EditorController _editor;
    private readonly Announcer _announcer;
    private readonly EarconPlayer _earcons = new();
    private readonly PreviewService _preview;
    private readonly DispatcherTimer _autosave;
    private readonly string? _startupFile;
    private Guid? _shownLayerId;
    private bool _syncing;
    private bool _busy;
    private bool _wasPlaying;

    public MainWindow(string? startupFile = null, bool selfTest = false)
    {
        InitializeComponent();
        _startupFile = startupFile;
        SelfTestMode = selfTest;

        try
        {
            AppPaths.EnsureFolders();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log("Could not create folders: " + ex.Message);
        }

        _settings = selfTest ? new AppSettings() : AppSettings.Load();
        _keymap = LoadKeymap();
        _editor = new EditorController(new ProjectSession(ProjectTemplates.Find("basic")!.Create()), _settings);
        _announcer = new Announcer(this, _settings.SpeechOutput);
        _announcer.Spoken += text => StatusText.Text = text;
        _earcons.Configure(_settings.EarconTheme, _settings.EarconVolume);
        _preview = new PreviewService(PreviewImage) { WindowSeconds = _settings.PreviewSeconds };
        _preview.PositionChanged += OnPlaybackPosition;
        _preview.ReachedEnd += OnPlaybackEnded;
        _preview.Preparing += OnPreviewPreparing;

        LayersList.ItemsSource = _layers;
        ItemsList.ItemsSource = _items;
        WireListEvents();
        BuildKeyTable();
        BuildMenu();
        BuildLibrary();
        BuildToolbar();
        PropertiesRegion.PreviewKeyDown += OnPropertiesKeyDown;
        PreviewRegion.GotKeyboardFocus += (_, _) => PreviewRegion.BorderThickness = new Thickness(3);
        PreviewRegion.LostKeyboardFocus += (_, _) => PreviewRegion.BorderThickness = new Thickness(0);
        PreviewRegion.MouseDown += (_, _) => FocusRegion(Region.Preview);

        _autosave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _autosave.Tick += (_, _) => Autosave();

        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += async (_, _) => await OnLoadedAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _soundPlayer?.Dispose();
            _preview.Dispose();
            _announcer.Dispose();
        };
    }

    public bool SelfTestMode { get; }

    internal EditorController Editor => _editor;

    internal FfmpegTools? Tools { get; private set; }

    internal IReadOnlyList<SpokenViewModel> LayerEntries => _layers;

    internal IReadOnlyList<SpokenViewModel> ItemEntries => _items;

    internal Announcer Speech => _announcer;

    internal string StatusTextValue => StatusText.Text;

    internal PreviewService Preview => _preview;

    internal EarconPlayer Earcons => _earcons;

    // ------------------------------------------------------------------ start and stop

    private async Task OnLoadedAsync()
    {
        try
        {
            Tools = await FfmpegTools.LocateAsync(_settings.FfmpegFolder);
            _preview.Tools = Tools;
        }
        catch (FfmpegNotFoundException ex)
        {
            AppPaths.Log(ex.Message);
        }

        Refresh();
        FocusTimeline();

        if (SelfTestMode)
        {
            var code = await SelfTest.RunAsync(this);
            Application.Current.Shutdown(code);
            return;
        }

        var opened = false;
        if (_startupFile is not null && File.Exists(_startupFile))
        {
            opened = OpenProjectFile(_startupFile, announce: false);
        }
        else if (File.Exists(AppPaths.AutosaveFile))
        {
            opened = OfferRecovery();
        }

        var intro = Tools is null
            ? "Accessible Video Editor. Warning: FFmpeg was not found, so playing and exporting will not work. Reinstall the program."
            : "Accessible Video Editor.";
        var where = _editor.FocusedItemText();
        Say($"{intro} {(opened ? "" : "New basic project. ")}{where}. Press F1 for keyboard help.");
        _autosave.Start();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (SelfTestMode)
        {
            return;
        }

        if (!ConfirmDiscardChanges("closing"))
        {
            e.Cancel = true;
            return;
        }

        _preview.CancelPreparing();
        try
        {
            _settings.Save();
            if (File.Exists(AppPaths.AutosaveFile))
            {
                File.Delete(AppPaths.AutosaveFile);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppPaths.Log("Saving settings on exit failed: " + ex.Message);
        }
    }

    private Keymap LoadKeymap()
    {
        try
        {
            var keymap = Keymap.Load(AppPaths.KeymapFile);
            foreach (var problem in keymap.Problems)
            {
                AppPaths.Log("Keymap: " + problem);
            }

            return keymap;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            AppPaths.Log("Keymap file ignored: " + ex.Message);
            return Keymap.Default();
        }
    }

    // ------------------------------------------------------------------ keyboard

    private void BuildKeyTable()
    {
        _keys.Clear();
        foreach (var command in CommandCatalog.All)
        {
            foreach (var chord in _keymap.KeysFor(command.Id))
            {
                var name = chord.Key == "Enter" ? "Return" : chord.Key;
                if (!Enum.TryParse<Key>(name, out var key))
                {
                    AppPaths.Log($"Unknown key name {chord.Key} for {command.Id}.");
                    continue;
                }

                var modifiers = ModifierKeys.None;
                if (chord.Ctrl)
                {
                    modifiers |= ModifierKeys.Control;
                }

                if (chord.Alt)
                {
                    modifiers |= ModifierKeys.Alt;
                }

                if (chord.Shift)
                {
                    modifiers |= ModifierKeys.Shift;
                }

                _keys.TryAdd((key, modifiers), command.Id);
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MainMenu.IsKeyboardFocusWithin || !IsEnabled)
        {
            return;
        }

        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None or Key.DeadCharProcessed)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            return;
        }

        if (_editor.InPositionMode)
        {
            // Position mode owns the keyboard until Enter or Escape, wherever focus is.
            e.Handled = true;
            HandlePositionKey(key, modifiers);
            return;
        }

        if (!_keys.TryGetValue((key, modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)), out var command))
        {
            return;
        }

        if (Keyboard.FocusedElement is TextBox or PasswordBox && IsTextEditingKey(key, modifiers))
        {
            // Inside a text box, copy, paste, undo, select all and word movement keep their usual meaning.
            return;
        }

        var inTimeline = ItemsList.IsKeyboardFocusWithin || PreviewRegion.IsKeyboardFocusWithin;
        var inLayers = LayersList.IsKeyboardFocusWithin;
        var info = CommandCatalog.Get(command);
        if (info.IsTimelineOnly && !inTimeline && !inLayers)
        {
            return;
        }

        if (inLayers)
        {
            switch (command)
            {
                // The layer list moves with its own arrow keys; its selection follows the layer.
                case CommandId.PreviousLayer or CommandId.NextLayer or CommandId.FirstItem or CommandId.LastItem
                    or CommandId.PreviousItem or CommandId.NextItem:
                    return;
                case CommandId.ItemProperties:
                    command = CommandId.SwitchPane;
                    break;
                case CommandId.Delete:
                    command = CommandId.DeleteLayer;
                    break;
            }
        }

        e.Handled = true;
        SyncFocusFromScreen();
        _ = RunAsync(command);
    }

    /// <summary>
    /// Makes sure commands act on exactly the item or layer the screen reader last announced, even if focus
    /// got there by Tab, the mouse or the screen reader's own navigation.
    /// </summary>
    private void SyncFocusFromScreen()
    {
        if (Keyboard.FocusedElement is not ListBoxItem { DataContext: SpokenViewModel vm })
        {
            return;
        }

        if (ItemsList.IsKeyboardFocusWithin && vm.Id != _editor.FocusedItemId)
        {
            _editor.FocusItem(vm.Id);
        }
        else if (LayersList.IsKeyboardFocusWithin && vm.Id != _editor.FocusedLayerId)
        {
            _editor.FocusLayer(vm.Id);
        }
    }

    /// <summary>Keys a text box needs for editing, which must not run editor commands there.</summary>
    private static bool IsTextEditingKey(Key key, ModifierKeys modifiers)
    {
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        if (alt)
        {
            return false;
        }

        if (key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.Delete or Key.Back or Key.Enter or Key.Space)
        {
            return true;
        }

        return ctrl && key is Key.A or Key.C or Key.X or Key.V or Key.Z or Key.Y;
    }

    /// <summary>Runs any command, from a key or a menu.</summary>
    internal async Task RunAsync(CommandId command)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            if (await RunWindowCommandAsync(command))
            {
                return;
            }

            if (_editor.Execute(command) is { } feedback)
            {
                await ApplyAsync(feedback);
            }
        }
        catch (Exception ex)
        {
            AppPaths.Log($"{command} failed: {ex}");
            _earcons.Play(EarconEvent.Error);
            Say($"Sorry, {CommandCatalog.Get(command).Title} failed: {ex.Message}");
        }
    }

    /// <summary>Acts on a controller result: refresh, sound, speech and focus, then playback.</summary>
    internal async Task ApplyAsync(Feedback feedback)
    {
        if (feedback.ProjectChanged)
        {
            _preview.Invalidate();
            UpdateTitle();
        }

        Refresh();
        if (feedback.Earcon is { } earcon)
        {
            _earcons.Play(earcon);
        }
        else if (feedback.Position is { } position)
        {
            _earcons.PlayPosition(position.X, position.Y);
        }

        FocusAndSay(feedback.Speech);
        await HandlePlaybackAsync(feedback.Playback);
    }

    // ------------------------------------------------------------------ lists and focus

    private void WireListEvents()
    {
        ItemsList.GotKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is ListBoxItem { DataContext: SpokenViewModel vm })
            {
                vm.OnFocusChanged(true);
                if (!_syncing && vm.Id != _editor.FocusedItemId)
                {
                    _editor.FocusItem(vm.Id);
                    UpdatePlayheadText();
                }
            }
        };
        ItemsList.LostKeyboardFocus += (_, e) =>
        {
            if (e.OldFocus is ListBoxItem { DataContext: SpokenViewModel vm })
            {
                vm.OnFocusChanged(false);
            }
        };
        ItemsList.SelectionChanged += (_, _) =>
        {
            if (!_syncing && ItemsList.SelectedItem is SpokenViewModel vm && vm.Id != _editor.FocusedItemId)
            {
                _editor.FocusItem(vm.Id);
                UpdatePlayheadText();
            }
        };

        LayersList.GotKeyboardFocus += (_, e) =>
        {
            if (e.NewFocus is ListBoxItem { DataContext: SpokenViewModel vm })
            {
                vm.OnFocusChanged(true);
            }
        };
        LayersList.LostKeyboardFocus += (_, e) =>
        {
            if (e.OldFocus is ListBoxItem { DataContext: SpokenViewModel vm })
            {
                vm.OnFocusChanged(false);
            }
        };
        LayersList.SelectionChanged += (_, _) =>
        {
            if (!_syncing && LayersList.SelectedItem is SpokenViewModel vm && vm.Id != _editor.FocusedLayerId)
            {
                _editor.FocusLayer(vm.Id);
                Refresh();
                _earcons.Play(EarconEvent.LayerChange);
                _ = HandlePlaybackAsync(PlaybackRequest.Refresh);
            }
        };
    }

    /// <summary>Brings both lists in line with the project and the controller's focus.</summary>
    internal void Refresh()
    {
        _syncing = true;
        try
        {
            var project = _editor.Project;
            var describer = _editor.Describer;
            Sync(_layers, project.Layers, l => l.Id, (vm, layer, i) =>
            {
                vm.Icon = Icons.ForLayer(layer.Kind, layer.Muted);
                vm.Update(
                describer.Layer(project, layer),
                $"{i + 1}. {layer.Name} ({Describer.LayerKindName(layer.Kind)}, {layer.Items.Count}){(layer.Muted ? " muted" : "")}");
            });
            LayersList.SelectedItem = _layers.FirstOrDefault(v => v.Id == _editor.FocusedLayerId);

            var layer = _editor.FocusedLayer;
            if (_shownLayerId != layer?.Id)
            {
                _items.Clear();
                _shownLayerId = layer?.Id;
            }

            var items = (IReadOnlyList<Item>?)layer?.Items ?? Array.Empty<Item>();
            Sync(_items, items, i => i.Id, (vm, item, i) =>
            {
                var span = Timeline.Span(layer!, i);
                vm.Icon = Icons.ForItem(item.Kind, layer!.Kind);
                vm.Update(describer.Item(layer!, i),
                    $"{i + 1}. {item.Name}\n{describer.Time(span.Start)}, {describer.Time(item.Duration)} long");
            });
            ItemsList.SelectedItem = _items.FirstOrDefault(v => v.Id == _editor.FocusedItemId);

            var label = layer is null
                ? "Timeline (no layers)"
                : $"Timeline, layer {_editor.FocusedLayerIndex + 1}, {layer.Name}";
            ItemsLabel.Content = "_" + label.Replace("_", "__") + ":";
            if (!ItemsList.IsKeyboardFocused)
            {
                AutomationProperties.SetName(ItemsList, layer is null ? "Timeline, no layers" : _editor.EmptyLayerText(layer));
            }

            UpdatePlayheadText();
            RebuildProperties();
            RefreshLibraryMedia();
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void Sync<T>(ObservableCollection<SpokenViewModel> collection, IReadOnlyList<T> models, Func<T, Guid> id,
        Action<SpokenViewModel, T, int> update)
    {
        var ids = models.Select(id).ToList();
        var keep = ids.ToHashSet();
        for (var i = collection.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(collection[i].Id))
            {
                collection.RemoveAt(i);
            }
        }

        for (var i = 0; i < ids.Count; i++)
        {
            var existing = -1;
            for (var j = i; j < collection.Count; j++)
            {
                if (collection[j].Id == ids[i])
                {
                    existing = j;
                    break;
                }
            }

            if (existing < 0)
            {
                collection.Insert(i, new SpokenViewModel(ids[i]));
            }
            else if (existing != i)
            {
                collection.Move(existing, i);
            }

            update(collection[i], models[i], i);
        }
    }

    /// <summary>
    /// Speaks <paramref name="speech"/> and puts keyboard focus where the controller says it is. When focus
    /// moves, the message is read as part of the newly focused entry so the two never cut each other off.
    /// </summary>
    private void FocusAndSay(string speech)
    {
        if (Toolbar.IsKeyboardFocusWithin)
        {
            // Toolbar buttons keep focus, so the user can press the next one.
            Say(speech);
            return;
        }

        if (PreviewRegion.IsKeyboardFocusWithin)
        {
            // The preview keeps focus; describe the item reached when moving around.
            Say(string.IsNullOrWhiteSpace(speech) ? _editor.FocusedItemText() : speech);
            AutomationProperties.SetName(PreviewRegion, "Preview. " + FrameDescriber.Describe(_editor.Project, _editor.Playhead));
            return;
        }

        if (LibraryRegion.IsKeyboardFocusWithin || PropertiesRegion.IsKeyboardFocusWithin)
        {
            Say(speech);
            return;
        }

        if (LayersList.IsKeyboardFocusWithin)
        {
            var layerVm = _layers.FirstOrDefault(v => v.Id == _editor.FocusedLayerId);
            if (layerVm is null)
            {
                Say(speech);
                return;
            }

            FocusEntry(LayersList, layerVm, speech);
            return;
        }

        var vm = _items.FirstOrDefault(v => v.Id == _editor.FocusedItemId);
        if (vm is null)
        {
            // Empty layer: the list itself takes focus and carries the message in its name.
            if (ItemsList.IsKeyboardFocused)
            {
                Say(speech);
            }
            else
            {
                var name = _editor.FocusedLayer is { } layer ? _editor.EmptyLayerText(layer) : "Timeline, no layers";
                AutomationProperties.SetName(ItemsList, string.IsNullOrWhiteSpace(speech) || speech.Contains(name, StringComparison.Ordinal) ? name : $"{speech} {name}");
                ItemsList.Focus();
            }

            return;
        }

        FocusEntry(ItemsList, vm, speech);
    }

    private void FocusEntry(ListBox list, SpokenViewModel vm, string speech)
    {
        list.SelectedItem = vm;
        list.ScrollIntoView(vm);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(vm) is not ListBoxItem container)
        {
            Say(speech);
            return;
        }

        if (container.IsKeyboardFocused)
        {
            Say(speech);
            return;
        }

        if (!string.IsNullOrWhiteSpace(speech) && !speech.Equals(vm.Description, StringComparison.Ordinal))
        {
            vm.SetPrefix(speech);
        }

        _syncing = true;
        try
        {
            container.Focus();
        }
        finally
        {
            _syncing = false;
        }
    }

    internal void FocusTimeline()
    {
        if (_items.FirstOrDefault(v => v.Id == _editor.FocusedItemId) is { } vm &&
            ItemsList.ItemContainerGenerator.ContainerFromItem(vm) is ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            ItemsList.Focus();
        }
    }

    private void FocusLayerList()
    {
        LayersList.UpdateLayout();
        if (_layers.FirstOrDefault(v => v.Id == _editor.FocusedLayerId) is { } vm &&
            LayersList.ItemContainerGenerator.ContainerFromItem(vm) is ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            LayersList.Focus();
        }
    }

    internal void Say(string text, bool interrupt = true) => _announcer.Say(text, interrupt);

    private void UpdatePlayheadText()
    {
        PlayheadText.Text =
            $"{TimeFormat.Clock(_editor.Playhead)} / {TimeFormat.Clock(_editor.ProjectDuration)}  |  {_editor.ListeningText}  |  ripple {(_settings.Ripple ? "on" : "off")}";
    }

    private void UpdateTitle()
    {
        var name = _editor.Session.FilePath is { } path ? Path.GetFileNameWithoutExtension(path) : _editor.Project.Title;
        Title = $"{name}{(_editor.Session.IsDirty ? " (not saved)" : "")} - Accessible Video Editor";
    }

    // ------------------------------------------------------------------ playback

    private async Task HandlePlaybackAsync(PlaybackRequest request)
    {
        switch (request)
        {
            case PlaybackRequest.Toggle:
                if (_preview.IsPreparing)
                {
                    _preview.CancelPreparing();
                    Say("Preview cancelled.");
                }
                else if (_preview.IsPlaying)
                {
                    PausePlayback("Paused");
                }
                else
                {
                    await StartPlaybackAsync();
                }

                break;

            case PlaybackRequest.Faster:
                if (!_preview.IsPlaying)
                {
                    _preview.ResetSpeed();
                    await StartPlaybackAsync();
                }
                else
                {
                    var speed = _preview.Faster();
                    Say(speed <= 1 ? "Normal speed." : $"{speed:0} times speed.");
                }

                break;

            case PlaybackRequest.Stop:
                if (_preview.IsPreparing)
                {
                    _preview.CancelPreparing();
                    Say("Preview cancelled.");
                }
                else if (_preview.IsPlaying)
                {
                    PausePlayback("Stopped");
                }

                _preview.ResetSpeed();
                break;

            case PlaybackRequest.Seek:
            case PlaybackRequest.Refresh:
                if (_preview.IsPlaying)
                {
                    await StartPlaybackAsync(quiet: true);
                }

                break;
        }
    }

    private async Task StartPlaybackAsync(bool quiet = false)
    {
        _wasPlaying = _preview.IsPlaying;
        _preparingStarted = Stopwatch.StartNew();
        _preparingSpoken = 0;
        var error = await _preview.PlayAsync(_editor.Project, _editor.CurrentScope, _editor.Playhead);
        _preparingStarted = null;
        if (error is not null)
        {
            _earcons.Play(EarconEvent.Error);
            Say(error);
        }
        else if (_preview.IsPlaying && !_wasPlaying && !quiet)
        {
            _earcons.Play(EarconEvent.PlayStart);
        }
    }

    private void PausePlayback(string word)
    {
        _preview.Pause();
        _editor.SetPlayheadFromPlayback(_preview.CurrentTime);
        UpdatePlayheadText();
        _earcons.Play(EarconEvent.PlayStop);
        Say($"{word} at {_editor.Describer.Time(_editor.Playhead)}.");
    }

    private void OnPlaybackPosition(double time)
    {
        _editor.SetPlayheadFromPlayback(time);
        UpdatePlayheadText();
    }

    private void OnPlaybackEnded()
    {
        _editor.SetPlayheadFromPlayback(_editor.ProjectDuration);
        UpdatePlayheadText();
        _earcons.Play(EarconEvent.PlayStop);
        Say("End.");
    }

    private async Task ContinuePlaybackAsync(double time)
    {
        _editor.SetPlayheadFromPlayback(time);
        await StartPlaybackAsync(quiet: true);
    }

    private Stopwatch? _preparingStarted;
    private int _preparingSpoken;

    private void OnPreviewPreparing(double fraction)
    {
        if (_preparingStarted is null)
        {
            return;
        }

        var seconds = _preparingStarted.Elapsed.TotalSeconds;
        if (_preparingSpoken == 0 && seconds > 0.6)
        {
            _preparingSpoken = 1;
            Say("Preparing preview.", interrupt: false);
        }
        else if (seconds > 2.5 && fraction >= _preparingSpoken * 0.25 && _preparingSpoken < 4)
        {
            Say($"{(int)(fraction * 100)} percent.", interrupt: false);
            _preparingSpoken = (int)(fraction * 4) + 1;
        }
    }

    // ------------------------------------------------------------------ busy guard

    /// <summary>Blocks other commands while an import, export or dialog is running.</summary>
    private IDisposable Busy() => new BusyScope(this);

    private sealed class BusyScope : IDisposable
    {
        private readonly MainWindow _window;

        public BusyScope(MainWindow window)
        {
            _window = window;
            _window._busy = true;
        }

        public void Dispose() => _window._busy = false;
    }
}
