using Octo.Services.Common;

namespace Octo.Tests;

public class SmartSearchInterpreterTests
{
    private readonly SmartSearchInterpreter _sut = new();

    [Theory]
    [InlineData("kizomba", "kizomba")]
    [InlineData("reggaetón", "reggaeton")]
    [InlineData("hip hop", "hip-hop")]
    [InlineData("música electrónica", "electronic")]
    public void Interpret_RecognisesGenresAcrossLanguages(string query, string expectedGenre)
    {
        var result = _sut.Interpret(query);

        Assert.True(result.IsSemantic);
        Assert.Equal(expectedGenre, result.Genre);
        Assert.Contains(expectedGenre, result.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("música para estudiar", "study")]
    [InlineData("musica para festa", "party")]
    [InlineData("algo tranquilo para conduzir à noite", "relax")]
    [InlineData("electrónica energética para correr", "running")]
    public void Interpret_MapsNaturalLanguageToDiscoveryTags(string query, string expectedTag)
    {
        var result = _sut.Interpret(query);

        Assert.True(result.IsSemantic);
        Assert.Contains(expectedTag, result.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.True(result.ProviderQueries.Count >= 2);
    }

    [Fact]
    public void Interpret_KeepsLiteralArtistSearchLiteral()
    {
        var result = _sut.Interpret("Daft Punk");

        Assert.False(result.IsSemantic);
        Assert.Null(result.Genre);
        Assert.Empty(result.Tags);
        Assert.Single(result.ProviderQueries);
        Assert.Equal("Daft Punk", result.ProviderQueries[0]);
    }

    [Fact]
    public void Interpret_CombinesGenreAndActivity()
    {
        var result = _sut.Interpret("música latina para fiesta");

        Assert.True(result.IsSemantic);
        Assert.Equal("latin", result.Genre);
        Assert.Contains("latin", result.Tags);
        Assert.Contains("party", result.Tags);
        Assert.Contains(result.ProviderQueries, q =>
            q.Contains("latin", StringComparison.OrdinalIgnoreCase)
            && q.Contains("party", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Reggaetón", "reggaeton")]
    [InlineData("RNB", "r&b")]
    [InlineData("dnb", "drum and bass")]
    public void CanonicalGenre_NormalisesAliases(string input, string expected)
    {
        Assert.Equal(expected, _sut.CanonicalGenre(input));
    }

    [Fact]
    public void CuratedGenres_ContainsUsefulArpeggiBrowseCategories()
    {
        Assert.Contains("Kizomba", SmartSearchInterpreter.CuratedGenres);
        Assert.Contains("Reggaeton", SmartSearchInterpreter.CuratedGenres);
        Assert.Contains("House", SmartSearchInterpreter.CuratedGenres);
        Assert.Contains("Afrobeats", SmartSearchInterpreter.CuratedGenres);
        Assert.Contains("Drum & Bass", SmartSearchInterpreter.CuratedGenres);
    }
}
