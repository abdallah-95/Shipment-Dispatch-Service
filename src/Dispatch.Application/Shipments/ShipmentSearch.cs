using Dispatch.Domain;

namespace Dispatch.Application.Shipments;

/// <summary>What a raw <c>search</c> string means for the list query.</summary>
public abstract record SearchCondition
{
    private SearchCondition()
    {
    }

    /// <summary>Null or whitespace: apply no search filter.</summary>
    public sealed record None : SearchCondition;

    /// <summary>Content, but no letters or digits: nothing can match, so the result is an empty page.</summary>
    public sealed record NoTerms : SearchCondition;

    /// <summary>Longer than <see cref="ShipmentSearch.MaxLength"/> characters: rejected with 400.</summary>
    public sealed record TooLong : SearchCondition;

    /// <summary>A full-text condition such as <c>"al*" AND "rashid*"</c>, sent to CONTAINS as a parameter.</summary>
    public sealed record FullText(string Condition) : SearchCondition;
}

/// <summary>
/// Turns the raw <c>search</c> string into a SQL Server <c>CONTAINS</c> condition. Pure: no EF, no
/// database, so the rules are unit-testable on their own.
/// </summary>
/// <remarks>
/// Terms are runs of letters and digits; every other character is a separator. Quotes, asterisks,
/// parentheses and the like therefore never reach the CONTAINS syntax, and because each term is
/// quoted, words such as AND, OR and NOT are searched for rather than parsed as operators. Each term
/// is a word prefix (<c>"term*"</c>) and all terms must match. Combining marks are not letters, so
/// scripts that depend on them are not supported.
/// </remarks>
public static class ShipmentSearch
{
    public const int MaxLength = 100;
    public const int MaxTerms = 8;

    public static SearchCondition Parse(string? search)
    {
        if (search is null)
        {
            return new SearchCondition.None();
        }

        // Raw length, before normalisation: the cap is on what the client sent.
        if (search.Length > MaxLength)
        {
            return new SearchCondition.TooLong();
        }

        if (string.IsNullOrWhiteSpace(search))
        {
            return new SearchCondition.None();
        }

        var terms = SplitTerms(SearchTextNormalizer.Normalize(search));
        if (terms.Count == 0)
        {
            return new SearchCondition.NoTerms();
        }

        return new SearchCondition.FullText(string.Join(" AND ", terms.Select(term => $"\"{term}*\"")));
    }

    /// <summary>Runs of letters and digits, at most <see cref="MaxTerms"/>; any further terms are ignored.</summary>
    private static List<string> SplitTerms(string normalized)
    {
        var terms = new List<string>(MaxTerms);
        var start = -1;

        for (var i = 0; i <= normalized.Length && terms.Count < MaxTerms; i++)
        {
            if (i < normalized.Length && char.IsLetterOrDigit(normalized[i]))
            {
                if (start < 0)
                {
                    start = i;
                }

                continue;
            }

            if (start >= 0)
            {
                terms.Add(normalized[start..i]);
                start = -1;
            }
        }

        return terms;
    }
}
