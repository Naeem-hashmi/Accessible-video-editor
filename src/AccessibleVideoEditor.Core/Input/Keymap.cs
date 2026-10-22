using System.Text.Json;

namespace AccessibleVideoEditor.Core.Input;

/// <summary>A key with modifiers. <see cref="Key"/> is a Windows key name such as "J", "Left", "D1" or "OemComma".</summary>
public readonly record struct KeyChord(string Key, bool Ctrl, bool Alt, bool Shift)
{
    public static bool TryParse(string text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        bool ctrl = false, alt = false, shift = false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(p => p.Length == 0))
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    ctrl = true;
                    break;
                case "alt":
                    alt = true;
                    break;
                case "shift":
                    shift = true;
                    break;
                default:
                    return false;
            }
        }

        chord = new KeyChord(parts[^1], ctrl, alt, shift);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Ctrl)
        {
            parts.Add("Ctrl");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>How the keys are read out, e.g. "Control Shift left bracket".</summary>
    public string Spoken()
    {
        var parts = new List<string>(4);
        if (Ctrl)
        {
            parts.Add("Control");
        }

        if (Alt)
        {
            parts.Add("Alt");
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(SpokenKey(Key));
        return string.Join(" ", parts);
    }

    public static string SpokenKey(string key) => key switch
    {
        "OemOpenBrackets" => "left bracket",
        "OemCloseBrackets" => "right bracket",
        "OemComma" => "comma",
        "OemPeriod" => "period",
        "Left" => "left arrow",
        "Right" => "right arrow",
        "Up" => "up arrow",
        "Down" => "down arrow",
        "Escape" => "Escape",
        "Delete" => "Delete",
        "Enter" or "Return" => "Enter",
        _ when key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]) => key[1..],
        _ => key,
    };
}

/// <summary>
/// The active key bindings: the defaults from <see cref="CommandCatalog"/>, optionally changed by a user
/// keymap file (JSON: command name to a list of keys).
/// </summary>
public sealed class Keymap
{
    private readonly Dictionary<KeyChord, CommandId> _byChord = new();
    private readonly Dictionary<CommandId, List<KeyChord>> _byCommand = new();

    private Keymap()
    {
    }

    public IReadOnlyList<string> Problems { get; private init; } = Array.Empty<string>();

    public static Keymap Default() => Create(null);

    /// <summary>Builds a keymap from defaults plus overrides. Bad entries are skipped and listed in <see cref="Problems"/>.</summary>
    public static Keymap Create(IReadOnlyDictionary<string, string[]>? overrides)
    {
        var problems = new List<string>();
        var keymap = new Keymap { Problems = problems };
        var bindings = CommandCatalog.All.ToDictionary(c => c.Id, c => c.DefaultKeys.ToList());

        if (overrides is not null)
        {
            foreach (var (name, keys) in overrides)
            {
                if (!Enum.TryParse<CommandId>(name, ignoreCase: true, out var id))
                {
                    problems.Add($"Unknown command '{name}'.");
                    continue;
                }

                bindings[id] = keys.ToList();
            }
        }

        foreach (var command in CommandCatalog.All)
        {
            keymap._byCommand[command.Id] = new List<KeyChord>();
            foreach (var text in bindings[command.Id])
            {
                if (!KeyChord.TryParse(text, out var chord))
                {
                    problems.Add($"'{text}' for {command.Id} is not a valid key.");
                    continue;
                }

                if (keymap._byChord.TryGetValue(chord, out var other))
                {
                    problems.Add($"{chord} is used by both {other} and {command.Id}; kept for {other}.");
                    continue;
                }

                keymap._byChord[chord] = command.Id;
                keymap._byCommand[command.Id].Add(chord);
            }
        }

        return keymap;
    }

    public static Keymap Load(string path)
    {
        if (!File.Exists(path))
        {
            return Default();
        }

        var overrides = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path));
        return Create(overrides);
    }

    /// <summary>Writes the current bindings, so users can edit them.</summary>
    public void Save(string path)
    {
        var data = _byCommand.ToDictionary(p => p.Key.ToString(), p => p.Value.Select(c => c.ToString()).ToArray());
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    public bool TryGetCommand(KeyChord chord, out CommandId command) => _byChord.TryGetValue(chord, out command);

    public IReadOnlyList<KeyChord> KeysFor(CommandId command) =>
        _byCommand.TryGetValue(command, out var list) ? list : Array.Empty<KeyChord>();

    /// <summary>The first key, written for a menu, e.g. "Ctrl+Shift+E".</summary>
    public string GestureText(CommandId command)
    {
        var keys = KeysFor(command);
        return keys.Count == 0 ? "" : Display(keys[0]);
    }

    public static string Display(KeyChord chord)
    {
        var text = chord.ToString();
        return text
            .Replace("OemOpenBrackets", "[")
            .Replace("OemCloseBrackets", "]")
            .Replace("OemComma", ",")
            .Replace("OemPeriod", ".")
            .Replace("+D0", "+0").Replace("+D1", "+1").Replace("+D2", "+2").Replace("+D3", "+3").Replace("+D4", "+4")
            .Replace("+D5", "+5").Replace("+D6", "+6").Replace("+D7", "+7").Replace("+D8", "+8").Replace("+D9", "+9");
    }
}
