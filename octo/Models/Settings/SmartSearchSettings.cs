namespace Octo.Models.Settings;

/// <summary>
/// Optional AI intent parsing for natural-language music searches.
///
/// The endpoint follows the OpenAI-compatible chat-completions contract, so the same
/// integration works with local Ollama/LM Studio/vLLM gateways and compatible cloud APIs.
/// If Endpoint or Model is empty, Gusonic falls back to the built-in multilingual rules.
/// </summary>
public sealed class SmartSearchSettings
{
    public bool EnableAi { get; set; } = false;

    /// <summary>
    /// Full chat-completions endpoint, for example:
    /// http://ollama:11434/v1/chat/completions
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    /// <summary>Optional bearer token for compatible hosted providers.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Hard request ceiling. Smart search must never make normal search feel hung.</summary>
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>How long interpreted queries stay in memory.</summary>
    public int CacheMinutes { get; set; } = 120;

    public int EffectiveTimeoutSeconds => Math.Clamp(TimeoutSeconds, 2, 15);
    public int EffectiveCacheMinutes => Math.Clamp(CacheMinutes, 5, 1440);

    public bool IsConfigured => EnableAi
        && Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(Model);
}
