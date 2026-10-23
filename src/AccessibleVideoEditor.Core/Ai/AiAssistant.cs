using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AccessibleVideoEditor.Core.Speech;

namespace AccessibleVideoEditor.Core.Ai;

/// <summary>A service that speaks the OpenAI chat API: OpenRouter, OpenAI, Ollama on this computer, or any other.</summary>
public sealed record AiProvider(string Id, string Name, string BaseUrl, string DefaultModel, bool NeedsKey, string Help)
{
    public static IReadOnlyList<AiProvider> All { get; } =
    [
        new("openrouter", "OpenRouter (many models, low cost)", "https://openrouter.ai/api/v1", "meta-llama/llama-3.3-70b-instruct", true,
            "One key for hundreds of models, many open source and cheap. Get a key at openrouter.ai, Keys. Model names are listed at openrouter.ai/models."),
        new("ollama", "Ollama (free, runs on this computer)", "http://localhost:11434/v1", "llama3.1", false,
            "Nothing leaves this computer and no key is needed. Install Ollama from ollama.com, then run: ollama pull llama3.1"),
        new("openai", "OpenAI", "https://api.openai.com/v1", "gpt-4o-mini", true,
            "Get a key at platform.openai.com, API keys."),
        new("custom", "Other OpenAI-compatible service", "", "", true,
            "For example Groq, Together, DeepSeek or LM Studio: enter its address (ending in /v1), model name and key."),
    ];

    public static AiProvider Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[0];
}

/// <summary>Where to send requests: the service address, model and key.</summary>
public sealed record AiConnection(string BaseUrl, string Model, string? ApiKey);

/// <summary>What the user wants removed, and what the video is about.</summary>
public sealed record AiCleanupRequest
{
    public string About { get; init; } = "";
    public bool Repeats { get; init; } = true;
    public bool Fillers { get; init; } = true;
    public bool Symbols { get; init; } = true;
    public bool Mistakes { get; init; } = true;
    public string Extra { get; init; } = "";
    public string Language { get; init; } = "";
}

public sealed class AiException(string message) : Exception(message);

/// <summary>
/// Asks a language model which words of a transcript to cut. The transcript goes out with every word
/// numbered; the answer must name word numbers and the words themselves, and anything that does not match
/// the transcript is thrown away, so a confused model can't cut the wrong part. Long transcripts go in parts.
/// Only the words are sent: never the video or sound.
/// </summary>
public static class AiAssistant
{
    public const int WordsPerRequest = 1200;

    /// <summary>The system message: what ClipScribe is, what a transcript cut does, and the answer format.</summary>
    internal static string Instructions(AiCleanupRequest request)
    {
        var remove = new List<string>();
        if (request.Repeats)
        {
            remove.Add("repeated words and phrases, and false starts where the speaker starts a sentence again (cut the earlier, abandoned try and keep the final one)");
        }

        if (request.Fillers)
        {
            remove.Add("filler sounds and filler words used as hesitation (um, uh, er, hmm, and 'like' or 'you know' only when they carry no meaning)");
        }

        if (request.Symbols)
        {
            remove.Add("stray symbols or noise markers that are not speech");
        }

        if (request.Mistakes)
        {
            remove.Add("obvious mistakes the speaker corrects straight away, keeping the corrected version");
        }

        var text = new StringBuilder();
        text.AppendLine("You are the editing assistant inside ClipScribe, a video editor used by blind and visually impaired people with screen readers.");
        text.AppendLine("The user gives you the transcript of their video. Every word is numbered like [12]word. Cutting words cuts that part of the video and sound, so be careful: only suggest cuts that make the video better and keep its meaning and flow intact. Never cut words just to shorten it.");
        if (request.About.Length > 0)
        {
            text.AppendLine($"The video is about: {request.About}");
        }

        if (request.Language.Length > 0)
        {
            text.AppendLine($"The speech is in language code '{request.Language}'.");
        }

        text.AppendLine("Suggest cutting: " + (remove.Count == 0 ? "only what the user asks below." : string.Join("; ", remove) + "."));
        if (request.Extra.Length > 0)
        {
            text.AppendLine($"The user also asks: {request.Extra}");
        }

        text.AppendLine("Answer with JSON only, no other text: {\"cuts\":[{\"from\":12,\"to\":14,\"words\":\"the exact words\",\"reason\":\"phrase said again\"}]}");
        text.AppendLine("from and to are the first and last word numbers to cut (inclusive), words are those words exactly as in the transcript, and reason is a few plain words. If nothing should be cut, answer {\"cuts\":[]}.");
        return text.ToString();
    }

    /// <summary>The transcript words from <paramref name="first"/> on, numbered, one sentence per line.</summary>
    internal static string Numbered(TranscriptDocument document, int first, int count)
    {
        var text = new StringBuilder();
        for (var i = first; i < Math.Min(document.Entries.Count, first + count); i++)
        {
            var word = document.Entries[i].Word.Text;
            text.Append('[').Append(i).Append(']').Append(word);
            text.Append(word.Length > 0 && ".?!।۔؟".Contains(word[^1]) ? '\n' : ' ');
        }

        return text.ToString().Trim();
    }

    /// <summary>Asks for suggestions for the whole document, part by part. Progress goes from 0 to 1.</summary>
    public static async Task<IReadOnlyList<CutSuggestion>> SuggestCutsAsync(HttpClient http, AiConnection connection, TranscriptDocument document,
        AiCleanupRequest request, IProgress<double>? progress = null, CancellationToken token = default)
    {
        var all = new List<CutSuggestion>();
        var system = Instructions(request);
        for (var first = 0; first < document.Entries.Count; first += WordsPerRequest)
        {
            token.ThrowIfCancellationRequested();
            var answer = await ChatAsync(http, connection, system, "Transcript:\n" + Numbered(document, first, WordsPerRequest), token).ConfigureAwait(false);
            all.AddRange(ParseCuts(answer, document, first, Math.Min(document.Entries.Count, first + WordsPerRequest) - 1));
            progress?.Report(Math.Min(1, (first + WordsPerRequest) / (double)Math.Max(1, document.Entries.Count)));
        }

        // In order and never overlapping.
        var ordered = new List<CutSuggestion>();
        foreach (var cut in all.OrderBy(c => c.First))
        {
            if (ordered.Count == 0 || cut.First > ordered[^1].Last)
            {
                ordered.Add(cut);
            }
        }

        return ordered;
    }

    /// <summary>A short request that proves the address, model and key work. Returns the model's reply.</summary>
    public static Task<string> TestAsync(HttpClient http, AiConnection connection, CancellationToken token = default) =>
        ChatAsync(http, connection, "Reply with the single word OK.", "Test", token);

    /// <summary>One chat completion. Errors become plain-language messages.</summary>
    public static async Task<string> ChatAsync(HttpClient http, AiConnection connection, string system, string user, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(connection.BaseUrl) || string.IsNullOrWhiteSpace(connection.Model))
        {
            throw new AiException("Set up the AI assistant first: choose a service and a model in Preferences, AI assistant.");
        }

        var body = new JsonObject
        {
            ["model"] = connection.Model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, connection.BaseUrl.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(connection.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey.Trim());
        }

        // OpenRouter shows these in its dashboard; other services ignore them.
        message.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/Naeem-hashmi/Accessible-video-editor");
        message.Headers.TryAddWithoutValidation("X-Title", "ClipScribe");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException($"Could not reach {new Uri(message.RequestUri!.GetLeftPart(UriPartial.Authority)).Host}: {ex.Message} Check the address and the internet connection (or that Ollama is running).");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = ErrorDetail(text);
                throw new AiException((int)response.StatusCode switch
                {
                    401 or 403 => "The AI service refused the key. Check the API key in Preferences, AI assistant." + detail,
                    402 => "The AI service says the account has no credit left." + detail,
                    404 => $"The AI service does not know the model \"{connection.Model}\", or the address is wrong." + detail,
                    429 => "The AI service is busy or the rate limit was reached. Wait a minute and try again." + detail,
                    _ => $"The AI service answered with error {(int)response.StatusCode}." + detail,
                });
            }

            try
            {
                using var json = JsonDocument.Parse(text);
                return json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or InvalidOperationException)
            {
                throw new AiException("The AI service sent an answer ClipScribe could not read.");
            }
        }
    }

    private static string ErrorDetail(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.ValueKind == JsonValueKind.String ? error.GetString()
                    : error.TryGetProperty("message", out var m) ? m.GetString() : null;
                return string.IsNullOrWhiteSpace(message) ? "" : $" It said: {message}";
            }
        }
        catch (JsonException)
        {
        }

        return "";
    }

    /// <summary>
    /// Reads the model's answer. A cut is kept only when its numbers are inside this part and its words match
    /// the transcript there; when only the numbers are off, the words are looked for close by.
    /// </summary>
    public static IReadOnlyList<CutSuggestion> ParseCuts(string answer, TranscriptDocument document, int first = 0, int last = int.MaxValue)
    {
        last = Math.Min(last, document.Entries.Count - 1);
        var json = ExtractJson(answer);
        if (json is null)
        {
            return [];
        }

        var cuts = new List<CutSuggestion>();
        JsonDocument parsed;
        try
        {
            parsed = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (parsed)
        {
            var list = parsed.RootElement.ValueKind == JsonValueKind.Array ? parsed.RootElement
                : parsed.RootElement.TryGetProperty("cuts", out var c) ? c : default;
            if (list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (var cut in list.EnumerateArray())
            {
                if (cut.ValueKind != JsonValueKind.Object || !TryInt(cut, "from", out var from))
                {
                    continue;
                }

                var to = TryInt(cut, "to", out var t) ? t : from;
                var words = cut.TryGetProperty("words", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() ?? "" : "";
                var reason = cut.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                if (Locate(document, from, to, words, first, last) is { } found)
                {
                    cuts.Add(new CutSuggestion(found.First, found.Last, reason.Length > 0 ? reason : "suggested by the AI"));
                }
            }
        }

        return cuts;
    }

    private static (int First, int Last)? Locate(TranscriptDocument document, int from, int to, string words, int first, int last)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var wanted = TranscriptEditing.Tokenize(words).Select(TranscriptEditing.Normalize).Where(x => x.Length > 0).ToList();
        bool Matches(int start, int count) =>
            start >= first && start + count - 1 <= last &&
            Enumerable.Range(0, count).All(k => TranscriptEditing.Normalize(document.Entries[start + k].Word.Text) == wanted[k]);

        if (wanted.Count == 0)
        {
            // No words given: trust the numbers only for a short cut inside this part.
            return from >= first && to <= last && to - from < 4 ? (from, to) : null;
        }

        if (Matches(from, wanted.Count))
        {
            return (from, from + wanted.Count - 1);
        }

        // Models sometimes count a few words off: look nearby, nearest first.
        for (var distance = 1; distance <= 12; distance++)
        {
            foreach (var start in new[] { from - distance, from + distance })
            {
                if (start >= 0 && start + wanted.Count <= document.Entries.Count && Matches(start, wanted.Count))
                {
                    return (start, start + wanted.Count - 1);
                }
            }
        }

        return null;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var p))
        {
            return false;
        }

        return p.ValueKind == JsonValueKind.Number ? p.TryGetInt32(out value)
            : p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value);
    }

    /// <summary>The JSON in an answer, even when the model wrapped it in a code block or added words around it.</summary>
    internal static string? ExtractJson(string answer)
    {
        var start = answer.IndexOfAny(['{', '[']);
        var end = Math.Max(answer.LastIndexOf('}'), answer.LastIndexOf(']'));
        return start >= 0 && end > start ? answer[start..(end + 1)] : null;
    }
}
