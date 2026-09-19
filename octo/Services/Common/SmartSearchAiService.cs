using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Octo.Models.Settings;

namespace Octo.Services.Common;

/// <summary>
/// Optional multilingual intent parser. The model never chooses tracks or artists; it only
/// translates free-form language into compact music concepts that real providers resolve.
///
/// Compatible with OpenAI-style /v1/chat/completions endpoints, including local Ollama,
/// LM Studio and vLLM gateways. Any failure is a soft failure and callers fall back to the
/// deterministic built-in interpreter.
/// </summary>
public sealed class SmartSearchAiService
{
    public const string ClientName = "smart-search-ai";

    private sealed record Cached(DateTime ExpiresUtc, SmartSearchInterpreter.Intent Intent);

    private readonly IHttpClientFactory _clients;
    private readonly IOptionsMonitor<SmartSearchSettings> _settings;
    private readonly ILogger<SmartSearchAiService> _logger;
    private readonly ConcurrentDictionary<string, Cached> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public SmartSearchAiService(
        IHttpClientFactory clients,
        IOptionsMonitor<SmartSearchSettings> settings,
        ILogger<SmartSearchAiService> logger)
    {
        _clients = clients;
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => _settings.CurrentValue.IsConfigured;

    public async Task<SmartSearchInterpreter.Intent?> InterpretAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var text = (query ?? string.Empty).Trim();
        var settings = _settings.CurrentValue;
        if (text.Length == 0 || !settings.IsConfigured) return null;

        if (_cache.TryGetValue(text, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            return cached.Intent;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.EffectiveTimeoutSeconds));

        try
        {
            var client = _clients.CreateClient(ClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint);
            if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

            var body = new
            {
                model = settings.Model,
                temperature = 0.1,
                response_format = new { type = "json_object" },
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content =
                            """
                            You are a multilingual music-search intent parser.
                            Understand the user's language automatically. Never invent song titles or artists.
                            Return ONLY JSON with this shape:
                            {
                              "semantic": true|false,
                              "genre": "canonical English music genre or null",
                              "tags": ["1 to 5 concise English music/mood/activity tags"],
                              "queries": ["1 to 4 short provider-friendly search phrases in English"]
                            }

                            semantic=false for a plain artist, album or song-title lookup such as
                            "Daft Punk", "Random Access Memories" or "Blinding Lights".
                            semantic=true for intent, mood, activity, era, genre or descriptive requests such as
                            "musique pour étudier", "música para una fiesta", "夜に運転する音楽",
                            "sad rainy day songs" or "afro music for the gym".

                            Preserve named genres. Translate moods, activities and descriptive concepts to concise
                            English tags suitable for Last.fm / Deezer search. Do not output explanations.
                            """
                    },
                    new { role = "user", content = text }
                }
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Smart search AI returned HTTP {Status}", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);

            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return null;

            var message = choices[0].TryGetProperty("message", out var msg) ? msg : default;
            var raw = message.ValueKind == JsonValueKind.Object
                && message.TryGetProperty("content", out var content)
                ? content.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Some local models wrap JSON in markdown fences despite the instruction.
            raw = raw.Trim();
            if (raw.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = raw.IndexOf('\n');
                var lastFence = raw.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                    raw = raw[(firstNewline + 1)..lastFence].Trim();
            }

            using var parsed = JsonDocument.Parse(raw);
            var root = parsed.RootElement;

            var semantic = root.TryGetProperty("semantic", out var sem)
                && sem.ValueKind is JsonValueKind.True or JsonValueKind.False
                && sem.GetBoolean();

            string? genre = null;
            if (root.TryGetProperty("genre", out var genreEl)
                && genreEl.ValueKind == JsonValueKind.String)
            {
                genre = Clean(genreEl.GetString(), 80);
                if (genre?.Equals("null", StringComparison.OrdinalIgnoreCase) == true) genre = null;
            }

            var tags = ReadArray(root, "tags", 5, 80);
            var queries = ReadArray(root, "queries", 4, 120);
            if (queries.Count == 0) queries.Add(text);

            if (semantic && genre is null && tags.Count == 0) semantic = false;

            var intent = new SmartSearchInterpreter.Intent(text, semantic, genre, tags, queries);
            _cache[text] = new Cached(
                DateTime.UtcNow.AddMinutes(settings.EffectiveCacheMinutes), intent);

            _logger.LogInformation(
                "Smart search AI interpreted '{Query}' semantic={Semantic} genre={Genre} tags=[{Tags}]",
                text, semantic, genre, string.Join(", ", tags));
            return intent;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Smart search AI timed out for '{Query}'", text);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Smart search AI failed for '{Query}'", text);
            return null;
        }
    }

    private static List<string> ReadArray(JsonElement root, string property, int maxItems, int maxLength)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(property, out var values) || values.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in values.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = Clean(item.GetString(), maxLength);
            if (value is null || result.Contains(value, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(value);
            if (result.Count >= maxItems) break;
        }
        return result;
    }

    private static string? Clean(string? value, int maxLength)
    {
        var cleaned = string.Join(' ', (value ?? string.Empty)
            .Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0) return null;
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].Trim();
    }
}
