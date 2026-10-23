using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App.Dialogs;

/// <summary>
/// A standard, fully labelled options dialog built from simple fields. Every control has a visible label
/// tied to it (so screen readers read "Length, edit, 5 seconds"), optional help text, and Enter / Escape
/// work as OK / Cancel. Many fields can be split over tabs (Control Tab moves between them).
/// </summary>
public sealed class FormDialog : Window
{
    private readonly TabControl? _tabs;
    private readonly List<Field> _fields = new();
    private StackPanel _page;
    private readonly StackPanel _buttons;

    public FormDialog(Window? owner, string title, bool tabs = false)
    {
        Title = title;
        Owner = owner;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 420;
        MaxHeight = SystemParameters.WorkArea.Height * 0.9;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontSize = 15;

        var root = new DockPanel { Margin = new Thickness(14) };
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(_buttons, Dock.Bottom);
        root.Children.Add(_buttons);

        _page = NewPage();
        if (tabs)
        {
            _tabs = new TabControl();
            AutomationProperties.SetName(_tabs, title);
            root.Children.Add(_tabs);
        }
        else
        {
            root.Children.Add(new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        }

        Content = root;
        var ok = AddButton("OK", isDefault: true);
        ok.Click += (_, _) => Accept();
        var cancel = AddButton("Cancel", isCancel: true);
        cancel.Click += (_, _) => DialogResult = false;
        Loaded += (_, _) => FocusFirst();
    }

    /// <summary>Text shown and read at the top of the dialog.</summary>
    public void AddIntro(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = new Thickness(0, 0, 0, 10), Focusable = true };
        AutomationProperties.SetName(block, text);
        KeyboardNavigation.SetIsTabStop(block, true);
        _page.Children.Add(block);
    }

    /// <summary>Starts a new tab (when the dialog has tabs).</summary>
    public void AddPage(string header)
    {
        if (_tabs is null)
        {
            return;
        }

        _page = NewPage();
        var tab = new TabItem
        {
            Header = header,
            Content = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false },
        };
        AutomationProperties.SetName(tab, header);
        _tabs.Items.Add(tab);
    }

    public Func<string> AddText(string label, string value, string? help = null)
    {
        var box = new TextBox { Text = value, MinWidth = 260 };
        Add(label, box, help);
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        return () => box.Text;
    }

    /// <summary>A text box for several lines. Enter starts a new line; Tab moves to the next control.</summary>
    public Func<string> AddMultilineText(string label, string value, string? help = null)
    {
        var box = new TextBox
        {
            Text = value,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 360,
            MinHeight = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Add(label, box, ((help ?? "") + " Enter starts a new line. Press Tab to move on, and Tab again to reach OK.").Trim());
        return () => box.Text;
    }

    /// <summary>A time or duration typed in the user's own words ("1:30", "90", "1m 30s").</summary>
    public Func<double> AddTime(string label, double value, string? help = null, double minimum = 0)
    {
        var box = new TextBox { Text = TimeFormat.Clock(value), MinWidth = 160 };
        var field = Add(label, box, (help ?? "") + (help is null ? "" : " ") + "Type a time such as 1:30, 90 or 1m 30s.");
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        field.Validate = () => TimeFormat.TryParse(box.Text, value, out var parsed) && parsed >= minimum
            ? null
            : $"{label}: type a time such as 1:30, 90, or 1m 30s{(minimum > 0 ? $", at least {TimeFormat.Words(minimum)}" : "")}.";
        return () => TimeFormat.TryParse(box.Text, value, out var parsed) ? parsed : value;
    }

    public Func<double> AddNumber(string label, double value, double minimum, double maximum, string? help = null)
    {
        var box = new TextBox { Text = value.ToString("0.##", CultureInfo.CurrentCulture), MinWidth = 120 };
        var field = Add(label, box, (help ?? "") + $" From {minimum.ToString(CultureInfo.CurrentCulture)} to {maximum.ToString(CultureInfo.CurrentCulture)}.");
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        bool TryRead(out double result)
        {
            var text = box.Text.Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        field.Validate = () => TryRead(out var v) && v >= minimum && v <= maximum
            ? null
            : $"{label}: type a number from {minimum.ToString(CultureInfo.CurrentCulture)} to {maximum.ToString(CultureInfo.CurrentCulture)}.";
        return () => TryRead(out var v) ? Math.Clamp(v, minimum, maximum) : value;
    }

    public Func<bool> AddCheck(string label, bool value, string? help = null)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(box, label);
        if (help is not null)
        {
            AutomationProperties.SetHelpText(box, help);
            box.ToolTip = help;
        }

        _page.Children.Add(box);
        _fields.Add(new Field(box));
        return () => box.IsChecked == true;
    }

    /// <summary>A drop-down choice. Returns the chosen index.</summary>
    public Func<int> AddChoice(string label, IReadOnlyList<string> options, int selected, string? help = null, Action<int>? changed = null)
    {
        var combo = new ComboBox { MinWidth = 300, IsEditable = false };
        foreach (var option in options)
        {
            var entry = new ComboBoxItem { Content = option };
            AutomationProperties.SetName(entry, option);
            combo.Items.Add(entry);
        }

        combo.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
        if (changed is not null)
        {
            combo.SelectionChanged += (_, _) => changed(combo.SelectedIndex);
        }

        Add(label, combo, help);
        return () => Math.Max(0, combo.SelectedIndex);
    }

    public Button AddButton(string text, bool isDefault = false, bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 96,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        AutomationProperties.SetName(button, text.Replace("_", ""));
        _buttons.Children.Insert(Math.Max(0, _buttons.Children.Count - (isDefault || isCancel ? 0 : 2)), button);
        return button;
    }

    /// <summary>Called after validation succeeds; return an error message to keep the dialog open.</summary>
    public Func<string?>? ExtraValidation { get; set; }

    private void Accept()
    {
        foreach (var field in _fields)
        {
            var error = field.Validate?.Invoke();
            if (error is not null)
            {
                ShowError(error, field.Control);
                return;
            }
        }

        if (ExtraValidation?.Invoke() is { } problem)
        {
            ShowError(problem, null);
            return;
        }

        DialogResult = true;
    }

    private void ShowError(string message, Control? control)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        if (control is not null)
        {
            if (_tabs is not null)
            {
                foreach (TabItem tab in _tabs.Items)
                {
                    if (tab.Content is ScrollViewer { Content: StackPanel panel } && panel.Children.Contains(control.Parent as UIElement ?? control))
                    {
                        _tabs.SelectedItem = tab;
                    }
                }
            }

            control.Focus();
        }
    }

    private Field Add(string label, Control control, string? help)
    {
        var row = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var text = new Label { Content = "_" + label.Replace("_", "__") + ":", Target = control, Padding = new Thickness(0, 0, 0, 2) };
        AutomationProperties.SetName(control, label);
        AutomationProperties.SetLabeledBy(control, text);
        if (!string.IsNullOrWhiteSpace(help))
        {
            AutomationProperties.SetHelpText(control, help.Trim());
            control.ToolTip = help.Trim();
        }

        row.Children.Add(text);
        row.Children.Add(control);
        _page.Children.Add(row);
        var field = new Field(control);
        _fields.Add(field);
        return field;
    }

    private static StackPanel NewPage() => new() { Margin = new Thickness(4), MinWidth = 380 };

    private void FocusFirst()
    {
        var first = _fields.FirstOrDefault()?.Control;
        if (_tabs is not null && _tabs.Items.Count > 0)
        {
            _tabs.SelectedIndex = 0;
            _tabs.UpdateLayout();
        }

        (first ?? (Control)_buttons.Children[0]).Focus();
        if (first is null)
        {
            Keyboard.Focus((IInputElement)_buttons.Children[0]);
        }
    }

    private sealed class Field(Control control)
    {
        public Control Control { get; } = control;

        public Func<string?>? Validate { get; set; }
    }
}
