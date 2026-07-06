using System.Globalization;
using System.Text;
using Jellyfin.Plugin.CsfdBadge.Models;

namespace Jellyfin.Plugin.CsfdBadge.Services;

/// <summary>
/// Scores ČSFD search results without performing network or storage operations.
/// </summary>
internal static class CsfdMatcher
{
    /// <summary>
    /// Scores one search result against Jellyfin metadata.
    /// </summary>
    public static int ScoreSearchResult(
        CsfdSearchItem candidate,
        string itemName,
        string? originalTitle,
        int? itemYear,
        string query,
        int resultIndex)
    {
        var score = 20;
        if (itemYear.HasValue && candidate.Year == itemYear.Value)
        {
            score += 30;
        }
        else if (itemYear.HasValue && Math.Abs(candidate.Year - itemYear.Value) == 1)
        {
            score += 12;
        }
        else if (itemYear.HasValue && candidate.Year > 0)
        {
            score -= 50;
        }

        score += TitleScore(candidate.Title, itemName, originalTitle);
        if (resultIndex == 0)
        {
            score += 20;
        }
        else if (resultIndex < 3)
        {
            score += 10;
        }

        if (Normalize(candidate.Title) == Normalize(query))
        {
            score += 10;
        }

        return score;
    }

    /// <summary>
    /// Verifies a detail response against Jellyfin metadata.
    /// </summary>
    public static int ScoreDetail(
        CsfdMovieDetail detail,
        string itemName,
        string? originalTitle,
        int? itemYear)
    {
        var score = 20;
        if (itemYear.HasValue && detail.Year == itemYear.Value)
        {
            score += 30;
        }
        else if (itemYear.HasValue && detail.Year > 0
                 && Math.Abs(detail.Year - itemYear.Value) > 1)
        {
            score -= 50;
        }

        var titles = detail.TitlesOther.Select(static title => title.Title).Append(detail.Title);
        var bestTitleScore = titles.Max(title => TitleScore(title, itemName, originalTitle));
        return score + bestTitleScore;
    }

    /// <summary>
    /// Rejects weak and ambiguous automatic matches.
    /// </summary>
    public static bool IsSafeWinner(IReadOnlyList<int> orderedScores, int minimumScore)
    {
        if (orderedScores.Count == 0 || orderedScores[0] < minimumScore)
        {
            return false;
        }

        return orderedScores.Count == 1
               || orderedScores[0] >= 100
               || orderedScores[0] - orderedScores[1] >= 5;
    }

    /// <summary>
    /// Normalizes titles for accent-insensitive matching.
    /// </summary>
    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int TitleScore(string candidate, string name, string? originalTitle)
    {
        var normalizedCandidate = Normalize(candidate);
        var itemTitles = new[] { name, originalTitle }
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Select(static title => Normalize(title!));

        var best = 0;
        foreach (var title in itemTitles)
        {
            if (normalizedCandidate == title)
            {
                best = Math.Max(best, 50);
            }
            else if (normalizedCandidate.Contains(title, StringComparison.Ordinal)
                     || title.Contains(normalizedCandidate, StringComparison.Ordinal))
            {
                best = Math.Max(best, 30);
            }
            else
            {
                best = Math.Max(best, (int)Math.Round(TokenSimilarity(normalizedCandidate, title) * 25));
            }
        }

        return best;
    }

    private static double TokenSimilarity(string left, string right)
    {
        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0;
        }

        var intersection = leftTokens.Intersect(rightTokens, StringComparer.Ordinal).Count();
        var union = leftTokens.Union(rightTokens, StringComparer.Ordinal).Count();
        return (double)intersection / union;
    }
}
