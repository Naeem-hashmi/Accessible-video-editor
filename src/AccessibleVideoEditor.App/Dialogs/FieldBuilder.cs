using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AccessibleVideoEditor.Core.Text;

namespace AccessibleVideoEditor.App.Dialogs;

/// <summary>
/// Adds labelled controls to a panel (used by the properties area and the library). Every control gets a
/// visible label tied to it and a UI Automation name, so screen readers read "Volume, percent, edit, 100".
/// </summary>
public sealed class FieldBuilder(Panel panel)
{
    private readonly List<(Control Control, Func<string?> Validate)> _checks = new();

    public Panel Panel { get; } = panel;

    public IReadOnlyList<Control> Controls => _checks.Select(c => c.Control).ToList();

    public TextBlock AddInfo(string text)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
        Panel.Children.Add(block);
        return block;
    }

    public Func<string> AddText(string label, string value, bool multiline = false, string? help = null)
    {
        var box = new TextBox
        {
            Text = value,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 70 : 0,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        };
        Add(label, box, (help ?? "") + (multiline ? " Enter starts a new line; Tab moves on." : ""), () => null);
        return () => box.Text;
    }

    public Func<double> AddTime(string label, double value, double minimum = 0, string? help = null)
    {
        var box = new TextBox { Text = TimeFormat.Clock(value) };
        Add(label, box, (help ?? "") + " Type a time such as 1:30, 90 or 1m 30s.",
            () => TimeFormat.TryParse(box.Text, value, out var t) && t >= minimum ? null : $"{label}: type a time such as 1:30 or 90.");
        return () => TimeFormat.TryParse(box.Text, value, out var t) ? Math.Max(minimum, t) : value;
    }

    public Func<double> AddNumber(string label, double value, double minimum, double maximum, string? help = null)
    {
        var box = new TextBox { Text = value.ToString("0.##", CultureInfo.CurrentCulture) };
        bool Read(out double v) => double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        Add(label, box, (help ?? "") + $" From {minimum} to {maximum}.",
            () => Read(out var v) && v >= minimum && v <= maximum ? null : $"{label}: type a number from {minimum} to {maximum}.");
        return () => Read(out var v) ? Math.Clamp(v, minimum, maximum) : value;
    }

    public Func<bool> AddCheck(string label, bool value, string? help = null)
    {
        var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 4) };
        AutomationProperties.SetName(box, label);
        if (help is not null)
        {
            AutomationProperties.SetHelpText(box, help);
        }

        Panel.Children.Add(box);
        _checks.Add((box, () => null));
        return () => box.IsChecked == true;
    }

    public Func<int> AddChoice(string label, IReadOnlyList<string> options, int selected, string? help = null)
    {
        var combo = new ComboBox();
        foreach (var option in options)
        {
            var entry = new ComboBoxItem { Content = option };
            AutomationProperties.SetName(entry, option);
            combo.Items.Add(entry);
        }

        combo.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Count - 1));
        Add(label, combo, help, () => null);
        return () => Math.Max(0, combo.SelectedIndex);
    }

    public Button AddButton(string text, Action click, string? help = null)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 4, 6, 4), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, text.Replace("_", ""));
        if (help is not null)
        {
            AutomationProperties.SetHelpText(button, help);
        }

        button.Click += (_, _) => click();
        Panel.Children.Add(button);
        return button;
    }

    /// <summary>The first problem, with the control to focus, or null when every field is valid.</summary>
    public (string Message, Control Control)? Validate()
    {
        foreach (var (control, validate) in _checks)
        {
            if (validate() is { } message)
            {
                return (message, control);
            }
        }

        return null;
    }

    private void Add(string label, Control control, string? help, Func<string?> validate)
    {
        var text = new Label { Content = label.Replace("_", "__") + ":", Target = control, Padding = new Thickness(0, 4, 0, 1) };
        AutomationProperties.SetName(control, label);
        AutomationProperties.SetLabeledBy(control, text);
        if (!string.IsNullOrWhiteSpace(help))
        {
            AutomationProperties.SetHelpText(control, help.Trim());
            control.ToolTip = help.Trim();
        }

        if (control is TextBox box)
        {
            box.GotKeyboardFocus += (_, _) =>
            {
                if (!box.AcceptsReturn)
                {
                    box.SelectAll();
                }
            };
        }

        Panel.Children.Add(text);
        Panel.Children.Add(control);
        _checks.Add((control, validate));
    }
}
