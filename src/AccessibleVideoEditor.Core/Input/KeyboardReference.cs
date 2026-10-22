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
        text.AppendLine("Keys without Control or Alt (letters, arrows, Space, Enter, Delete, brackets) work while the timeline or the layer list has focus. Every key can be changed in the keyboard settings file (Tools menu).");
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
        text.AppendLine("<p>Keys without Control or Alt (letters, arrows, Space, Enter, Delete, brackets) work while the timeline or the layer list has focus. Each section is a heading and each table has column headers, so screen readers can move by heading (H) and by table cell.</p>");
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

        text.AppendLine("</body></html>");
        return text.ToString();
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
