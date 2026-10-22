using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace AccessibleVideoEditor.App.Dialogs;

/// <summary>Shows export progress with a Cancel button. Progress is also spoken every 10 percent.</summary>
public sealed class ProgressWindow : Window
{
    private readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 100, Height = 22, MinWidth = 380 };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Action<string> _speak;
    private int _lastSpoken = -1;

    public ProgressWindow(Window owner, string title, Action<string> speak)
    {
        Title = title;
        Owner = owner;
        _speak = speak;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontSize = 15;

        AutomationProperties.SetName(_bar, title);
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 96, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
        AutomationProperties.SetName(cancel, "Cancel");
        AutomationProperties.SetHelpText(cancel, "Stop and delete the unfinished file.");
        cancel.Click += (_, _) => Cancelled?.Invoke();
        Closing += (_, e) =>
        {
            if (!_finished)
            {
                e.Cancel = true;
                Cancelled?.Invoke();
            }
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(_bar);
        panel.Children.Add(_status);
        panel.Children.Add(cancel);
        Content = panel;
        Loaded += (_, _) => cancel.Focus();
    }

    private bool _finished;

    public event Action? Cancelled;

    public void Report(double fraction)
    {
        var percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        _bar.Value = percent;
        _status.Text = $"{percent} percent";
        var step = percent / 10 * 10;
        if (step > _lastSpoken && step is > 0 and < 100)
        {
            _lastSpoken = step;
            _speak($"{step} percent");
        }
    }

    public void Finish()
    {
        _finished = true;
        Close();
    }
}
