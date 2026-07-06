using Jellyfin.Plugin.CsfdBadge.Models;
using Jellyfin.Plugin.CsfdBadge.Services;
using Xunit;

namespace Jellyfin.Plugin.CsfdBadge.Tests;

public sealed class CsfdMatcherTests
{
    [Fact]
    public void Normalize_RemovesAccentsAndPunctuation()
    {
        Assert.Equal("pelisky 1999", CsfdMatcher.Normalize("Pelíšky (1999)"));
    }

    [Fact]
    public void ScoreSearchResult_ExactTitleAndYear_IsStrongMatch()
    {
        var candidate = new CsfdSearchItem { Title = "Dune", Year = 2021 };

        var score = CsfdMatcher.ScoreSearchResult(candidate, "Dune", "Dune", 2021, "Dune", 0);

        Assert.True(score >= 100);
    }

    [Fact]
    public void ScoreSearchResult_RemakeWithWrongYear_IsRejected()
    {
        var candidate = new CsfdSearchItem { Title = "Dune", Year = 1984 };

        var score = CsfdMatcher.ScoreSearchResult(candidate, "Dune", "Dune", 2021, "Dune", 0);

        Assert.True(score < 70);
    }

    [Fact]
    public void IsSafeWinner_RejectsAmbiguousModerateScores()
    {
        Assert.False(CsfdMatcher.IsSafeWinner([85, 82], 70));
        Assert.True(CsfdMatcher.IsSafeWinner([101, 100], 70));
    }

    [Fact]
    public void ScoreDetail_UsesAlternativeTitles()
    {
        var detail = new CsfdMovieDetail
        {
            Title = "Cesta do fantazie",
            Year = 2001,
            TitlesOther = [new CsfdAlternativeTitle { Title = "Spirited Away" }]
        };

        var score = CsfdMatcher.ScoreDetail(detail, "Spirited Away", null, 2001);

        Assert.Equal(100, score);
    }
}
