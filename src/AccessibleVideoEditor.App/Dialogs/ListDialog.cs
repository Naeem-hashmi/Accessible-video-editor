using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace AccessibleVideoEditor.App.Dialogs;

/// <summary>An entry in a <see cref="ListDialog"/>.</summary>
public sealed record ListEntry(string Text, object? Tag = null, string? Group = null);

/// <summary>
/// A list to choose from, with optional "type to filter" search box and action buttons. Enter runs the
/// first (default) action; Escape closes. Used for markers, sounds, templates, help and check results.
/// </summary>
public sealed class ListDialog : Window
{
    private readonly ListBox _list = new() { MinWidth = 520, MinHeight = 300, MaxHeight = 520 };
    private readonly TextBox? _search;
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
    private IReadOnlyList<ListEntry> _entries;

    public ListDialog(Window? owner, string title, string listLabel, IReadOnlyList<ListEntry> entries, bool searchable = false, string? intro = null)
    {
        Title = title;
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        FontSize = 15;
        _entries = entries;

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(_buttons, Dock.Bottom);
        root.Children.Add(_buttons);

        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        if (intro is not null)
        {
            var block = new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, Margin = new Thickness(0, 0, 0, 8) };
            top.Children.Add(block);
        }

        if (searchable)
        {
            _search = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
            var searchLabel = new Label { Content = "_Search:", Target = _search, Padding = new Thickness(0, 0, 0, 2) };
            AutomationProperties.SetName(_search, "Search");
            AutomationProperties.SetHelpText(_search, "Type to filter the list, then press Down arrow to go to the results.");
            AutomationProperties.SetLabeledBy(_search, searchLabel);
            _search.TextChanged += (_, _) => Fill();
            _search.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Down && _list.Items.Count > 0)
                {
                    FocusList();
                    e.Handled = true;
                }
            };
            top.Children.Add(searchLabel);
            top.Children.Add(_search);
        }

        var label = new Label { Content = "_" + listLabel + ":", Target = _list, Padding = new Thickness(0, 0, 0, 2) };
        top.Children.Add(label);
        root.Children.Add(top);

        AutomationProperties.SetName(_list, listLabel);
        AutomationProperties.SetLabeledBy(_list, label);
        TextSearch.SetTextPath(_list, "Content");
        _list.MouseDoubleClick += (_, _) => RunDefault();
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                RunDefault();
                e.Handled = true;
            }
            else if (e.Key == Key.Space && _actions.FirstOrDefault(a => !a.Closes) is { Text: { } } preview)
            {
                // Space runs the first action that keeps the dialog open, such as Preview.
                Run(preview.Text, preview.Run, preview.Closes);
                e.Handled = true;
            }
        };
        root.Children.Add(_list);
        Content = root;

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        AutomationProperties.SetName(close, "Close");
        close.Click += (_, _) => Close();
        _buttons.Children.Add(close);

        Fill();
        Loaded += (_, _) =>
        {
            if (_search is not null)
            {
                _search.Focus();
            }
            else
            {
                FocusList();
            }
        };
    }

    /// <summary>The entry chosen when the dialog closed through an action.</summary>
    public ListEntry? Chosen { get; private set; }

    /// <summary>The action chosen, by its button text.</summary>
    public string? ChosenAction { get; private set; }

    /// <summary>
    /// Adds an action button. Actions that <paramref name="closes"/> end the dialog; others (like Preview)
    /// run and keep it open. The first action added is the Enter key.
    /// </summary>
    public void AddAction(string text, Action<ListEntry>? run = null, bool closes = true)
    {
        var button = new Button { Content = text, MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        AutomationProperties.SetName(button, text.Replace("_", ""));
        button.Click += (_, _) => Run(text, run, closes);
        _buttons.Children.Insert(_buttons.Children.Count - 1, button);
        _actions.Add((text, run, closes));
    }

    private readonly List<(string Text, Action<ListEntry>? Run, bool Closes)> _actions = new();

    /// <summary>Replaces the entries (for example after a delete) and keeps the position.</summary>
    public void SetEntries(IReadOnlyList<ListEntry> entries)
    {
        var index = _list.SelectedIndex;
        _entries = entries;
        Fill();
        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = Math.Clamp(index, 0, _list.Items.Count - 1);
            FocusList();
        }
    }

    private void RunDefault()
    {
        if (_actions.Count > 0)
        {
            var (text, run, closes) = _actions[0];
            Run(text, run, closes);
        }
    }

    private void Run(string text, Action<ListEntry>? run, bool closes)
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: ListEntry entry })
        {
            return;
        }

        if (closes)
        {
            Chosen = entry;
            ChosenAction = text;
            DialogResult = true;
            return;
        }

        run?.Invoke(entry);
    }

    private void Fill()
    {
        var filter = _search?.Text.Trim() ?? "";
        _list.Items.Clear();
        foreach (var entry in _entries)
        {
            if (filter.Length > 0 && !entry.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase) &&
                !(entry.Group ?? "").Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            var text = entry.Group is null ? entry.Text : $"{entry.Text} ({entry.Group})";
            var item = new ListBoxItem { Content = text, Tag = entry };
            AutomationProperties.SetName(item, text);
            _list.Items.Add(item);
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        AutomationProperties.SetHelpText(_list, _list.Items.Count == 1 ? "1 entry" : $"{_list.Items.Count} entries");
    }

    private void FocusList()
    {
        if (_list.Items.Count == 0)
        {
            _list.Focus();
            return;
        }

        if (_list.SelectedIndex < 0)
        {
            _list.SelectedIndex = 0;
        }

        _list.UpdateLayout();
        if (_list.ItemContainerGenerator.ContainerFromIndex(_list.SelectedIndex) is ListBoxItem container)
        {
            container.Focus();
        }
        else
        {
            _list.Focus();
        }
    }
}
