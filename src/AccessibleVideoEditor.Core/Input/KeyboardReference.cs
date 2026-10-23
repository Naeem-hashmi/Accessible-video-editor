using System.Net;
using System.Text;

namespace AccessibleVideoEditor.Core.Input;

/// <summary>Writes the keyboard reference from the command table, so the documents always match the program.</summary>
public static class KeyboardReference
{
    public static string Markdown(Keymap keymap)
    {
        var text = new StringBuilder();
        text.AppendLine("# Keyboard reference");
        text.AppendLine();
        text.AppendLine("This file is generated from the program's command table (`tools/AccessibleVideoEditor.AssetGenerator --docs docs`). Do not edit it by hand.");
        text.AppendLine();
        text.AppendLine("Keys without Control or Alt (letters, arrows, Space, Enter, Delete, brackets) work while the timeline, the layer list or the preview has focus. Every key can be changed in the keyboard settings file (Tools menu).");
        text.AppendLine();
        text.AppendLine("**Laptops:** every command has at least one key that needs no number pad, no Fn key (Home, End, F1 to F12) and no AltGr (brackets on many non-US keyboards). Where the first key listed is one of those, the second one is the laptop-friendly alternative, for example `I` and `O` for range start and end, `Ctrl+Left` and `Ctrl+Right` for first and last item, `Ctrl+H` for help and `Alt+.` for the next area.");
        foreach (var category in CommandCatalog.Categories)
        {
            text.AppendLine();
            text.AppendLine($"## {category}");
            text.AppendLine();
            text.AppendLine("| Keys | Command | What it does |");
            text.AppendLine("|---|---|---|");
            foreach (var command in CommandCatalog.All.Where(c => c.Category == category))
            {
                var keys = string.Join(" or ", keymap.KeysFor(command.Id).Select(k => $"`{Keymap.Display(k)}`"));
                text.AppendLine($"| {keys} | {command.Title} | {command.Help} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Keys inside position mode");
        text.AppendLine();
        text.AppendLine("Press `P` on a picture, video or text to enter position mode. A short tone plays after each move: it is panned left or right by where the picture is across the frame, and higher the nearer it is to the top.");
        text.AppendLine();
        text.AppendLine("| Keys | What it does |");
        text.AppendLine("|---|---|");
        foreach (var (keys, what) in PositionKeys)
        {
            text.AppendLine($"| {keys} | {what} |");
        }

        return text.ToString();
    }

    public static string Html(Keymap keymap)
    {
        var text = new StringBuilder();
        text.AppendLine("<!DOCTYPE html>");
        text.AppendLine("<html lang=\"en\">");
        text.AppendLine("<head><meta charset=\"utf-8\"><title>Keyboard reference - Accessible Video Editor</title>");
        text.AppendLine("<style>body{font-family:Segoe UI,sans-serif;max-width:60em;margin:1em auto;padding:0 1em;line-height:1.5}table{border-collapse:collapse;width:100%}th,td{border:1px solid #888;padding:.3em .5em;text-align:left;vertical-align:top}kbd{font-family:inherit;font-weight:bold}</style></head>");
        text.AppendLine("<body>");
        text.AppendLine("<h1>Keyboard reference</h1>");
        text.AppendLine("<p>Keys without Control or Alt (letters, arrows, Space, Enter, Delete, brackets) work while the timeline, the layer list or the preview has focus. Each section is a heading and each table has column headers, so screen readers can move by heading (H) and by table cell.</p>");
        text.AppendLine("<p><strong>Laptops:</strong> every command has at least one key that needs no number pad, no Fn key (Home, End, F1 to F12) and no AltGr (brackets on many non-US keyboards). Where a command lists two keys, the second is the laptop-friendly one: for example I and O for range start and end, Control Left and Control Right for first and last item, Control H for help and Alt period for the next area.</p>");
        foreach (var category in CommandCatalog.Categories)
        {
            text.AppendLine($"<h2>{Encode(category)}</h2>");
            text.AppendLine($"<table><caption>{Encode(category)} keys</caption><thead><tr><th scope=\"col\">Keys</th><th scope=\"col\">Command</th><th scope=\"col\">What it does</th></tr></thead><tbody>");
            foreach (var command in CommandCatalog.All.Where(c => c.Category == category))
            {
                var keys = string.Join(" or ", keymap.KeysFor(command.Id).Select(k => $"<kbd>{Encode(k.Spoken())}</kbd>"));
                text.AppendLine($"<tr><td>{keys}</td><th scope=\"row\">{Encode(command.Title)}</th><td>{Encode(command.Help)}</td></tr>");
            }

            text.AppendLine("</tbody></table>");
        }

        text.AppendLine("<h2>Keys inside position mode</h2>");
        text.AppendLine("<p>Press P on a picture, video or text to enter position mode. A short tone plays after each move: it is panned left or right by where the picture is across the frame, and higher the nearer it is to the top.</p>");
        text.AppendLine("<table><caption>Position mode keys</caption><thead><tr><th scope=\"col\">Keys</th><th scope=\"col\">What it does</th></tr></thead><tbody>");
        foreach (var (keys, what) in PositionKeys)
        {
            text.AppendLine($"<tr><td><kbd>{Encode(keys)}</kbd></td><td>{Encode(what)}</td></tr>");
        }

        text.AppendLine("</tbody></table>");

        text.AppendLine("</body></html>");
        return text.ToString();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    /// <summary>The keys of position mode, shared by the documents and the in-program help.</summary>
    public static IReadOnlyList<(string Keys, string What)> PositionKeys { get; } =
    [
        ("Arrows", "Move 5 percent of the frame. With Shift, 1 percent."),
        ("Plus and minus", "Bigger and smaller. With Shift, in small steps."),
        ("1 to 9 (the number row above the letters, or a number pad)", "Snap to a place laid out like a phone keypad upside down, or a number pad: 7 top left, 8 top centre, 9 top right, 4 middle left, 5 centre, 6 middle right, 1 bottom left, 2 bottom centre, 3 bottom right. No number pad is needed."),
        ("0 (number row or number pad)", "Full frame (maximise)."),
        ("F", "Full frame only: switch between fit (whole picture, maybe with bars) and fill (covers the frame, edges cut off)."),
        ("Control and an arrow", "Crop 5 percent more from that edge."),
        ("Control Shift and an arrow", "Give back 5 percent of that edge."),
        ("R", "Remove all cropping."),
        ("O and Shift O", "More and less opacity, in steps of 10 percent."),
        ("D or W", "Describe where it is and everything else in the picture."),
        ("Enter", "Keep the changes and leave position mode."),
        ("Escape", "Undo every change made in position mode and leave."),
    ];
}
