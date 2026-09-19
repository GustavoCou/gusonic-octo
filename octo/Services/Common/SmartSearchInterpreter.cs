using System.Globalization;
using System.Text;

namespace Octo.Services.Common;

/// <summary>
/// Lightweight, provider-independent interpretation for natural-language music searches.
///
/// The goal is deliberately narrower than an LLM: turn common PT/ES/EN requests such as
/// "música tranquila para estudar" or "musica latina para fiesta" into canonical Last.fm
/// tags / Deezer-friendly queries without changing literal artist/title searches.
///
/// Unknown words are never invented into artists or tracks. The downstream providers still
/// decide which real music matches the interpreted tags.
/// </summary>
public sealed class SmartSearchInterpreter
{
    public sealed record Intent(
        string OriginalQuery,
        bool IsSemantic,
        string? Genre,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> ProviderQueries);

    private static readonly (string Canonical, string[] Aliases)[] Genres =
    [
        ("rock", ["rock"]),
        ("pop", ["pop"]),
        ("jazz", ["jazz"]),
        ("metal", ["metal", "heavy metal"]),
        ("house", ["house", "deep house"]),
        ("techno", ["techno"]),
        ("trance", ["trance"]),
        ("electronic", ["electronic", "electronica", "eletronica", "electrónica", "electronica"]),
        ("dance", ["dance"]),
        ("hip-hop", ["hip hop", "hip-hop", "rap"]),
        ("r&b", ["r&b", "rnb", "rhythm and blues"]),
        ("soul", ["soul"]),
        ("funk", ["funk"]),
        ("indie", ["indie"]),
        ("alternative", ["alternative", "alternativo", "alternativa"]),
        ("punk", ["punk"]),
        ("reggae", ["reggae"]),
        ("reggaeton", ["reggaeton", "reggaetón"]),
        ("latin", ["latin", "latino", "latina", "musica latina", "música latina"]),
        ("salsa", ["salsa"]),
        ("bachata", ["bachata"]),
        ("kizomba", ["kizomba"]),
        ("afrobeats", ["afrobeats", "afrobeat"]),
        ("amapiano", ["amapiano"]),
        ("blues", ["blues"]),
        ("country", ["country"]),
        ("classical", ["classical", "clasica", "clásica", "classica", "clássica"]),
        ("ambient", ["ambient"]),
        ("lo-fi", ["lofi", "lo-fi", "lo fi"]),
        ("folk", ["folk"]),
        ("gospel", ["gospel"]),
        ("disco", ["disco"]),
        ("drum and bass", ["drum and bass", "dnb", "drum & bass"]),
        ("dubstep", ["dubstep"]),
    ];

    private static readonly (string Tag, string[] Triggers)[] ContextTags =
    [
        ("party", ["party", "fiesta", "festa", "balada"]),
        ("dance", ["bailar", "baile", "dançar", "dancar", "dancing", "dance"]),
        ("workout", ["workout", "gym", "gimnasio", "ginásio", "ginasio", "treino", "treinar", "entrenar"]),
        ("running", ["running", "correr", "corrida", "run"]),
        ("study", ["study", "studying", "estudar", "estudo", "estudiar"]),
        ("focus", ["focus", "foco", "concentrar", "concentration", "concentração", "concentracao"]),
        ("sleep", ["sleep", "dormir", "sono", "sueño", "sueno"]),
        ("relax", ["relax", "relaxar", "relajarse", "relajante", "relaxing", "tranquilo", "tranquila", "calma", "calm"]),
        ("chill", ["chill", "chillout", "chill out"]),
        ("romantic", ["romantic", "romântico", "romantico", "romántico", "romantica", "romántica", "amor", "love"]),
        ("sad", ["sad", "triste", "melancholic", "melancólico", "melancolico", "melancólica", "melancolica"]),
        ("happy", ["happy", "feliz", "alegre", "uplifting"]),
        ("energetic", ["energetic", "energia", "energético", "energetico", "energética", "energetica", "intenso", "intensa"]),
        ("night", ["night", "noite", "noche", "madrugada"]),
        ("driving", ["driving", "drive", "conduzir", "conduccion", "conducción", "carro", "coche"]),
        ("summer", ["summer", "verão", "verao", "verano"]),
        ("beach", ["beach", "praia", "playa"]),
        ("coffee", ["coffee", "café", "cafe"]),
        ("dinner", ["dinner", "jantar", "cena"]),
    ];

    private static readonly HashSet<string> SemanticLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "music", "musica", "música", "songs", "canciones", "cancoes", "canções",
        "para", "for", "algo", "something", "quiero", "quero", "pon", "poner",
        "ouvir", "escuchar", "listen", "playlist"
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "music", "musica", "songs", "song", "canciones", "cancion", "cancoes", "cancao",
        "para", "for", "de", "do", "da", "del", "la", "el", "los", "las", "um", "uma", "un", "una",
        "que", "quiero", "quero", "algo", "something", "poner", "pon", "ouvir", "escuchar", "listen",
        "me", "mi", "meu", "minha", "the", "a", "an", "y", "e", "and", "con", "com"
    };

    public static IReadOnlyList<string> CuratedGenres { get; } =
        Genres.Select(item => DisplayGenre(item.Canonical)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public Intent Interpret(string query)
    {
        var original = (query ?? string.Empty).Trim().Trim('"');
        if (original.Length == 0)
            return new Intent(original, false, null, [], []);

        var normalized = Normalize(original);
        var tags = new List<string>();

        string? genre = null;
        foreach (var item in Genres)
        {
            if (item.Aliases.Any(alias => ContainsPhrase(normalized, Normalize(alias))))
            {
                genre ??= item.Canonical;
                AddUnique(tags, item.Canonical);
            }
        }

        foreach (var item in ContextTags)
        {
            if (item.Triggers.Any(trigger => ContainsPhrase(normalized, Normalize(trigger))))
                AddUnique(tags, item.Tag);
        }

        var tokens = Tokenize(normalized);
        var looksNaturalLanguage = tokens.Count >= 3
            && tokens.Any(token => SemanticLeadWords.Contains(token));

        // A pure genre ("kizomba") is also semantic in the sense that the user means
        // "music tagged kizomba", not tracks whose title literally contains the word.
        var exactGenre = genre is not null && Genres
            .Where(item => item.Canonical == genre)
            .SelectMany(item => item.Aliases)
            .Any(alias => Normalize(alias) == normalized);

        var isSemantic = looksNaturalLanguage || exactGenre || tags.Count >= 2;

        // If the phrase clearly asks for a mood/activity but we do not recognise every
        // meaningful word, preserve up to two residual terms as candidate Last.fm tags.
        // This keeps slang and new genres useful without pretending we understand them.
        if (isSemantic)
        {
            foreach (var token in tokens)
            {
                if (token.Length < 3 || StopWords.Contains(token)) continue;
                if (tags.Any(tag => Normalize(tag) == token)) continue;
                if (Genres.Any(g => g.Aliases.Any(a => Normalize(a) == token))) continue;
                if (ContextTags.Any(c => c.Triggers.Any(t => Normalize(t) == token))) continue;
                AddUnique(tags, token);
                if (tags.Count >= 5) break;
            }
        }

        var providerQueries = new List<string> { original };

        if (isSemantic)
        {
            if (genre is not null)
            {
                var context = tags.FirstOrDefault(tag => !tag.Equals(genre, StringComparison.OrdinalIgnoreCase));
                AddUnique(providerQueries, context is null ? genre : $"{genre} {context}");
            }

            foreach (var tag in tags.Take(4))
                AddUnique(providerQueries, tag);
        }

        return new Intent(original, isSemantic, genre, tags, providerQueries);
    }

    public string CanonicalGenre(string genre)
    {
        var normalized = Normalize(genre);
        foreach (var item in Genres)
            if (item.Aliases.Any(alias => Normalize(alias) == normalized))
                return item.Canonical;
        return genre.Trim();
    }

    private static List<string> Tokenize(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool ContainsPhrase(string haystack, string needle)
    {
        if (needle.Length == 0) return false;
        return $" {haystack} ".Contains($" {needle} ", StringComparison.Ordinal);
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
            values.Add(value);
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(char.IsLetterOrDigit(ch) || ch is '&' or '-' ? ch : ' ');
        }
        return string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string DisplayGenre(string value) => value switch
    {
        "hip-hop" => "Hip-Hop",
        "r&b" => "R&B",
        "lo-fi" => "Lo-Fi",
        "afrobeats" => "Afrobeats",
        "amapiano" => "Amapiano",
        "drum and bass" => "Drum & Bass",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value)
    };
}
