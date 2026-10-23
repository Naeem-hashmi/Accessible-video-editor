using System.ComponentModel;

namespace AccessibleVideoEditor.App.ViewModels;

/// <summary>
/// A list entry for a layer or an item. <see cref="Spoken"/> is what the screen reader reads (its UI
/// Automation name).
/// </summary>
/// <remarks>
/// While an entry has keyboard focus its spoken name is frozen: if it changed, screen readers would read
/// the whole entry again on top of the program's own short message. The new text is applied when focus
/// leaves. A one-time <see cref="SetPrefix">prefix</see> lets a message such as "Deleted clip 2." be read
/// together with the entry that receives focus, so the two never cut each other off.
/// </remarks>
public sealed class SpokenViewModel : INotifyPropertyChanged
{
    private string _description = "";
    private string _display = "";
    private string _prefix = "";
    private string _spoken = "";
    private bool _focused;

    public SpokenViewModel(Guid id)
    {
        Id = id;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    /// <summary>Visible text for sighted helpers.</summary>
    public string Display
    {
        get => _display;
        private set
        {
            if (_display != value)
            {
                _display = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }

    public string Spoken => _spoken;

    private string _icon = "";

    /// <summary>An icon-font glyph showing the kind of entry (video, picture, sound, text).</summary>
    public string Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }
    }

    /// <summary>The current full description, even while the spoken name is frozen.</summary>
    public string Description => _description;

    public bool IsFocused => _focused;

    public void Update(string description, string display)
    {
        _description = description;
        Display = display;
        if (!_focused)
        {
            Recompute();
        }
    }

    /// <summary>Text read once, before the description, the next time this entry receives focus.</summary>
    public void SetPrefix(string prefix)
    {
        _prefix = prefix.Trim();
        Recompute();
    }

    public void OnFocusChanged(bool focused)
    {
        _focused = focused;
        if (!focused)
        {
            _prefix = "";
            Recompute();
        }
    }

    private void Recompute()
    {
        var spoken = _prefix.Length == 0 ? _description : $"{_prefix} {_description}";
        if (spoken != _spoken)
        {
            _spoken = spoken;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Spoken)));
        }
    }
}
