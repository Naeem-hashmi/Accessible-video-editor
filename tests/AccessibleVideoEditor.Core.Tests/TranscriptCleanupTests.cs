using System.Net;
using System.Text;
using System.Text.Json;
using AccessibleVideoEditor.Core.Ai;
using AccessibleVideoEditor.Core.Editing;
using AccessibleVideoEditor.Core.Model;
using AccessibleVideoEditor.Core.Speech;
using AccessibleVideoEditor.Core.Tests.Support;

namespace AccessibleVideoEditor.Core.Tests;

public sealed class TranscriptCleanupTests
{
    /// <summary>A clip with one word every half second.</summary>
    private static (Project Project, Layer Layer, TranscriptDocument Document) Talk(string text)
    {
        var words = text.Split(' ');
        var project = new Project();
        var layer = ProjectEditor.AddLayer(project, LayerKind.Video);
        layer.Items.Add(Make.Clip("talk.mp4", words.Length * 0.5 + 1, sourceDuration: 100));
        project.Transcripts.Add(new Transcript
        {
            MediaPath = "talk.mp4",
            Words = words.Select((w, i) => new TranscriptWord { Text = w, Start = 0.5 + i * 0.5, End = 0.5 + i * 0.5 + 0.4 }).ToList(),
        });
        return (project, layer, TranscriptEditing.Build(project, layer));
    }

    [Fact]
    public void Fillers_doubled_words_restarted_phrases_and_symbols_are_found()
    {
        var (_, _, document) = Talk("So um I went to the I went to the shop and and bought ♪ very very good rice.");
        var found = TranscriptCleanup.Find(document);
        Assert.Equal(
            ["um: filler sound", "I went to the: phrase said again", "and: word said twice", "♪: symbol, not a word"],
            found.Select(f => $"{f.Words(document)}: {f.Reason}"));
        // "very very" is said on purpose.
        Assert.DoesNotContain(found, f => f.Words(document) == "very");
    }

    [Fact]
    public void Accepted_suggestions_cut_the_video_and_keep_the_last_try()
    {
        var (project, layer, document) = Talk("I went to the I went to the shop.");
        var found = TranscriptCleanup.Find(document);
        var edited = TranscriptEditing.Without(document, found.SelectMany(f => Enumerable.Range(f.First, f.Count)));
        Assert.Equal("I went to the shop.", edited);
        var before = layer.Items.Sum(i => i.Duration);
        var result = TranscriptEditing.Apply(project, layer, document, edited, TranscriptMode.Cut);
        Assert.True(result.Ok, result.Message);
        Assert.InRange(before - layer.Items.Sum(i => i.Duration), 1.7, 2.1);
        Assert.Equal("I went to the shop.", TranscriptEditing.Build(project, layer).Text);
    }

    [Fact]
    public void An_exported_transcript_edited_outside_comes_back_as_cuts_and_fixes()
    {
        var (project, layer, document) = Talk("Hello and um welcome to my chanel.");
        var exported = TranscriptCleanup.ExportText(document);
        Assert.Equal("Hello and um welcome to my chanel.\r\n", exported);
        var edited = TranscriptCleanup.ImportText("Hello and welcome to my channel.\r\n");
        var (deleted, changed) = TranscriptEditing.Compare(document, edited);
        Assert.Equal([2], deleted);
        Assert.Equal(1, changed);

        // Captions files work too: numbers, times and tags are left out.
        var srt = TranscriptCleanup.ExportCaptions(project, layer, webVtt: false);
        Assert.StartsWith("1\r\n00:00:00,500 --> ", srt);
        var vtt = TranscriptCleanup.ExportCaptions(project, layer, webVtt: true);
        Assert.StartsWith("WEBVTT\r\n\r\n00:00:00.500 --> ", vtt);
        Assert.Equal("Hello and welcome to my <i>chanel.</i>".Replace("<i>", "").Replace("</i>", ""),
            TranscriptCleanup.ImportText(srt.Replace("um ", "").Replace("chanel.", "<i>chanel.</i>")).ReplaceLineEndings(" ").Trim());
    }

    private sealed class FakeService(Func<HttpRequestMessage, string, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Seen.Add((request, body));
            return answer(request, body);
        }
    }

    private static HttpResponseMessage Reply(string content, HttpStatusCode code = HttpStatusCode.OK) => new(code)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } }), Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task The_ai_gets_numbered_words_and_only_cuts_that_match_the_transcript_are_kept()
    {
        var (_, _, document) = Talk("So um I went to the I went to the shop.");
        // Word 1 is right; the restart is given two numbers too late (found nearby); the last cut names words that are not there.
        var service = new FakeService((_, _) => Reply("""
            Here you go:
            ```json
            {"cuts":[{"from":1,"to":1,"words":"um","reason":"filler"},
                     {"from":4,"to":7,"words":"I went to the","reason":"restart"},
                     {"from":9,"to":9,"words":"banana","reason":"made up"}]}
            ```
            """));
        using var http = new HttpClient(service);
        var connection = new AiConnection("https://openrouter.ai/api/v1/", "meta-llama/llama-3.3-70b-instruct", "sk-test");
        var cuts = await AiAssistant.SuggestCutsAsync(http, connection, document, new AiCleanupRequest { About = "cooking rice" });

        Assert.Equal(["um", "I went to the"], cuts.Select(c => c.Words(document)));
        Assert.Equal((2, 5), (cuts[1].First, cuts[1].Last));
        var (request, body) = Assert.Single(service.Seen);
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", request.RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", request.Headers.Authorization!.ToString());
        using var json = JsonDocument.Parse(body);
        Assert.Equal("meta-llama/llama-3.3-70b-instruct", json.RootElement.GetProperty("model").GetString());
        var messages = json.RootElement.GetProperty("messages");
        Assert.Contains("ClipScribe", messages[0].GetProperty("content").GetString());
        Assert.Contains("cooking rice", messages[0].GetProperty("content").GetString());
        Assert.Contains("[0]So [1]um [2]I", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Long_transcripts_go_in_parts_and_ollama_needs_no_key()
    {
        var (_, _, document) = Talk(string.Join(' ', Enumerable.Range(0, 2500).Select(i => $"w{i}")) + ".");
        var service = new FakeService((_, _) => Reply("{\"cuts\":[]}"));
        using var http = new HttpClient(service);
        var cuts = await AiAssistant.SuggestCutsAsync(http, new AiConnection("http://localhost:11434/v1", "llama3.1", null), document, new AiCleanupRequest());
        Assert.Empty(cuts);
        Assert.Equal(3, service.Seen.Count);
        Assert.All(service.Seen, s => Assert.Null(s.Request.Headers.Authorization));
        Assert.Contains("[1200]w1200", service.Seen[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "refused the key")]
    [InlineData(HttpStatusCode.NotFound, "does not know the model")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limit")]
    public async Task Service_errors_are_explained(HttpStatusCode code, string expected)
    {
        var service = new FakeService((_, _) => new HttpResponseMessage(code) { Content = new StringContent("{\"error\":{\"message\":\"nope\"}}") });
        using var http = new HttpClient(service);
        var error = await Assert.ThrowsAsync<AiException>(() => AiAssistant.TestAsync(http, new AiConnection("https://x.test/v1", "m", "k")));
        Assert.Contains(expected, error.Message);
        Assert.Contains("It said: nope", error.Message);
    }
}
