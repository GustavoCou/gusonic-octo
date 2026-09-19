using Octo.Services.Common;

namespace Octo.Tests;

public class SmartSearchInterpreterTests
{
    private readonly SmartSearchInterpreter _sut = new();

    [Theory]
    [InlineData("Daft Punk")]
    [InlineData("música para estudiar")]
    [InlineData("musique calme pour travailler")]
    [InlineData("夜に運転する音楽")]
    [InlineData("موسيقى هادئة للدراسة")]
    public void Fallback_PreservesAnyLanguageLiterally(string query)
    {
        var result = _sut.Interpret(query);

        Assert.False(result.IsSemantic);
        Assert.Null(result.Genre);
        Assert.Empty(result.Tags);
        Assert.Single(result.ProviderQueries);
        Assert.Equal(query, result.ProviderQueries[0]);
    }

    [Fact]
    public void Fallback_TrimsOuterWhitespaceAndQuotesOnly()
    {
        var result = _sut.Interpret("  \"Daft Punk\"  ");

        Assert.Equal("Daft Punk", result.OriginalQuery);
        Assert.Equal("Daft Punk", result.ProviderQueries[0]);
    }

    [Fact]
    public void Fallback_EmptyInputReturnsEmptyIntent()
    {
        var result = _sut.Interpret("   ");

        Assert.False(result.IsSemantic);
        Assert.Empty(result.Tags);
        Assert.Empty(result.ProviderQueries);
    }
}
