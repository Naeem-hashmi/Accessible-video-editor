using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AccessibleVideoEditor.Core.Online;

public enum OnlineAudioCategory
{
    Music,
    SoundEffect,
}

/// <summary>A piece of openly licensed audio found online.</summary>
public sealed record OnlineAudio(
    string Id,
    string Title,
    string Creator,
    string License,
    string LicenseVersion,
    string LicenseUrl,
    string Attribution,
    string Url,
    string LandingUrl,
    string FileType,
    double DurationSeconds,
    string Source)
{
    /// <summary>Public domain (CC0 or Public Domain Mark): no credit needed.</summary>
    public bool IsPublicDomain => License is "cc0" or "pdm";

    /// <summary>What the licence allows, in plain words.</summary>
    public string LicenseText => License switch
    {
        "cc0" => "CC0, public domain: free for any use, no credit needed",
        "pdm" => "Public domain: free for any use, no credit needed",
        "by" => $"CC BY {LicenseVersion}: free for any use, including commercial, but you must credit {Creator}",
        _ => $"{License.ToUpperInvariant()} {LicenseVersion}",
    };

    public string Spoken => $"{Title} by {Creator}, {Text.TimeFormat.Words(DurationSeconds)}, {LicenseText}, from {Source}";

    /// <summary>The credit line to put in a video description.</summary>
    public string Credit => string.IsNullOrWhiteSpace(Attribution)
        ? $"\"{Title}\" by {Creator}, {License.ToUpperInvariant()} {LicenseVersion}, {LandingUrl}"
        : $"{Attribution} Source: {LandingUrl}";
}

public sealed record OnlineSearchResult(IReadOnlyList<OnlineAudio> Items, int TotalCount);

public sealed class OnlineSearchException(string message) : Exception(message);

/// <summary>
/// Searches Openverse (openverse.org, run by WordPress) for audio under licences that allow any use,
/// including commercial videos and editing: CC0, the Public Domain Mark and CC BY (credit required).
/// No account is needed; anonymous use allows about 20 searches a minute.
/// </summary>
public sealed class OpenverseClient
{
    public const string Endpoint = "https://api.openverse.org/v1/audio/";

    /// <summary>Licences that allow commercial use and changes such as trimming.</summary>
    public const string AllowedLicenses = "cc0,pdm,by";

    private readonly HttpClient _http;

    public OpenverseClient(HttpClient http)
    {
        _http = http;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AccessibleVideoEditor", "0.2"));
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(+https://github.com/Naeem-hashmi/Accessible-video-editor)"));
        }
    }

    public static Uri SearchUri(string query, OnlineAudioCategory category, int page, int pageSize = 20)
    {
        var parts = new List<string>
        {
            "format=json",
            "q=" + Uri.EscapeDataString(query.Trim()),
            "license=" + AllowedLicenses,
            // Freesound, the main source of sound effects, leaves the category empty, so search it directly.
            category == OnlineAudioCategory.Music ? "category=music" : "source=freesound",
            "mature=false",
            "page_size=" + Math.Clamp(pageSize, 1, 20).ToString(CultureInfo.InvariantCulture),
            "page=" + Math.Max(1, page).ToString(CultureInfo.InvariantCulture),
        };
        return new Uri(Endpoint + "?" + string.Join("&", parts));
    }

    public async Task<OnlineSearchResult> SearchAsync(string query, OnlineAudioCategory category, int page = 1, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new OnlineSearchException("Type what you are looking for, for example calm piano.");
        }

        using var response = await _http.GetAsync(SearchUri(query, category, page), token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new OnlineSearchException("Openverse asks to slow down: too many searches in a minute. Wait a minute and try again.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new OnlineSearchException($"Openverse could not search right now ({(int)response.StatusCode}). Try again later.");
        }

        var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        return Parse(json);
    }

    /// <summary>Reads an Openverse search response, keeping only usable, allowed results.</summary>
    public static OnlineSearchResult Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var total = root.TryGetProperty("result_count", out var count) && count.ValueKind == JsonValueKind.Number ? count.GetInt32() : 0;
        var items = new List<OnlineAudio>();
        if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in results.EnumerateArray())
            {
                var license = Str(r, "license").ToLowerInvariant();
                var url = Str(r, "url");
                if (license is not ("cc0" or "pdm" or "by") || !Uri.TryCreate(url, UriKind.Absolute, out _))
                {
                    continue;
                }

                var duration = r.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() / 1000 : 0;
                items.Add(new OnlineAudio(
                    Str(r, "id"),
                    Fallback(Str(r, "title"), "Untitled"),
                    Fallback(Str(r, "creator"), "unknown artist"),
                    license,
                    Str(r, "license_version"),
                    Str(r, "license_url"),
                    Str(r, "attribution"),
                    url,
                    Fallback(Str(r, "foreign_landing_url"), url),
                    NormaliseType(Str(r, "filetype")),
                    duration,
                    Fallback(Str(r, "source"), Str(r, "provider"))));
            }
        }

        return new OnlineSearchResult(items, total);
    }

    /// <summary>Downloads the audio into <paramref name="folder"/> and returns the file path.</summary>
    public async Task<string> DownloadAsync(OnlineAudio audio, string folder, CancellationToken token = default)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{SafeFileName(audio.Title)} - {SafeFileName(audio.Creator)}.{audio.FileType}");
        if (File.Exists(path))
        {
            return path;
        }

        using var response = await _http.GetAsync(audio.Url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new OnlineSearchException($"The file could not be downloaded ({(int)response.StatusCode}). Try another result.");
        }

        var temp = path + ".part";
        await using (var file = File.Create(temp))
        {
            await response.Content.CopyToAsync(file, token).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
        if (!audio.IsPublicDomain)
        {
            // The first line is the credit; the editor adds it to the project when the file is used.
            await File.WriteAllTextAsync(path + ".credit.txt", audio.Credit + Environment.NewLine + audio.LicenseText + Environment.NewLine, token).ConfigureAwait(false);
        }
        return path;
    }

    private static string NormaliseType(string type) => type.ToLowerInvariant() switch
    {
        "" or "mp32" or "mpeg" => "mp3",
        "oga" => "ogg",
        var t => t.Length <= 5 && t.All(char.IsLetterOrDigit) ? t : "mp3",
    };

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Fallback(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string SafeFileName(string text)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string(text.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        if (cleaned.Length > 60)
        {
            cleaned = cleaned[..60].Trim();
        }

        return cleaned.Length == 0 ? "audio" : cleaned;
    }
}
