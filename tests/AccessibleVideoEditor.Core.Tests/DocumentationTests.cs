using AccessibleVideoEditor.Core.Input;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class DocumentationTests
{
    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AccessibleVideoEditor.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine([dir!.FullName, .. parts]);
    }

    [Theory]
    [InlineData("KEYBOARD.md")]
    [InlineData("KEYBOARD.html")]
    public void Keyboard_reference_matches_the_command_table(string file)
    {
        var keymap = Keymap.Default();
        var expected = file.EndsWith(".md") ? KeyboardReference.Markdown(keymap) : KeyboardReference.Html(keymap);
        var actual = File.ReadAllText(RepoFile("docs", file));
        Assert.True(expected.ReplaceLineEndings() == actual.ReplaceLineEndings(),
            $"docs/{file} is out of date. Run: dotnet run --project tools/AccessibleVideoEditor.AssetGenerator -- --docs docs");
    }

    [Fact]
    public void Html_reference_is_well_formed_enough_for_screen_readers()
    {
        var html = KeyboardReference.Html(Keymap.Default());
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Equal(CommandCatalog.Categories.Count, html.Split("<h2>").Length - 1);
        Assert.Equal(html.Split("<table>").Length, html.Split("</table>").Length);
        Assert.Contains("<th scope=\"col\">Keys</th>", html);
        Assert.Contains("Alt 1", html);
    }
}
