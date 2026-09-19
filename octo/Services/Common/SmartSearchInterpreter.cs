namespace Octo.Services.Common;

/// <summary>
/// Deterministic fallback for search interpretation.
///
/// This class intentionally contains no language-, genre-, mood- or phrase-specific rules.
/// When AI interpretation is unavailable, Gusonic preserves the user's query literally
/// instead of guessing. That makes fallback behaviour predictable in every language.
/// </summary>
public sealed class SmartSearchInterpreter
{
    public sealed record Intent(
        string OriginalQuery,
        bool IsSemantic,
        string? Genre,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> ProviderQueries);

    public Intent Interpret(string query)
    {
        var original = (query ?? string.Empty).Trim().Trim('"');
        if (original.Length == 0)
            return new Intent(original, false, null, Array.Empty<string>(), Array.Empty<string>());

        return new Intent(
            original,
            IsSemantic: false,
            Genre: null,
            Tags: Array.Empty<string>(),
            ProviderQueries: new[] { original });
    }
}
