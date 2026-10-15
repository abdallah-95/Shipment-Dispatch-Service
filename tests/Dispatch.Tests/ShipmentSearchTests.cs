using Dispatch.Application.Shipments;

namespace Dispatch.Tests;

/// <summary>The pure search-condition helper. No database.</summary>
public sealed class ShipmentSearchTests
{
    [Theory]
    [InlineData("cedar", "\"cedar*\"")]
    [InlineData("  CeDaR   Port ", "\"cedar*\" AND \"port*\"")]
    [InlineData("al\trashid\n", "\"al*\" AND \"rashid*\"")]
    // Quotes are separators, so they can never open or close a CONTAINS phrase.
    [InlineData("\"al\" rashid\"", "\"al*\" AND \"rashid*\"")]
    [InlineData("o'brien", "\"o*\" AND \"brien*\"")]
    // Operators are quoted terms, not syntax.
    [InlineData("cedar OR port", "\"cedar*\" AND \"or*\" AND \"port*\"")]
    [InlineData("NOT cedar AND port", "\"not*\" AND \"cedar*\" AND \"and*\" AND \"port*\"")]
    [InlineData("NEAR(cedar, port)", "\"near*\" AND \"cedar*\" AND \"port*\"")]
    // Asterisks are separators: the helper adds the only prefix wildcard.
    [InlineData("ced*", "\"ced*\"")]
    [InlineData("*cedar**", "\"cedar*\"")]
    // Hyphenated tracking numbers split into their parts.
    [InlineData("DSP-1000-0001", "\"dsp*\" AND \"1000*\" AND \"0001*\"")]
    [InlineData("dsp10000001", "\"dsp10000001*\"")]
    public void Builds_quoted_prefix_terms_joined_with_AND(string search, string expected)
    {
        var result = ShipmentSearch.Parse(search);

        Assert.Equal(new SearchCondition.FullText(expected), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Null_empty_or_whitespace_means_no_filter(string? search)
    {
        Assert.IsType<SearchCondition.None>(ShipmentSearch.Parse(search));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("\"\"")]
    [InlineData("--**()!?")]
    [InlineData(" - ' . ")]
    public void Content_without_letters_or_digits_means_no_terms(string search)
    {
        Assert.IsType<SearchCondition.NoTerms>(ShipmentSearch.Parse(search));
    }

    [Fact]
    public void Over_100_characters_is_too_long_and_100_is_accepted()
    {
        Assert.IsType<SearchCondition.TooLong>(ShipmentSearch.Parse(new string('a', ShipmentSearch.MaxLength + 1)));
        Assert.IsType<SearchCondition.FullText>(ShipmentSearch.Parse(new string('a', ShipmentSearch.MaxLength)));

        // The cap is on the raw input, so whitespace padding counts.
        Assert.IsType<SearchCondition.TooLong>(ShipmentSearch.Parse("cedar" + new string(' ', ShipmentSearch.MaxLength)));
    }

    [Fact]
    public void Only_the_first_8_terms_are_used()
    {
        var result = ShipmentSearch.Parse("a b c d e f g h i j");

        Assert.Equal(
            new SearchCondition.FullText("\"a*\" AND \"b*\" AND \"c*\" AND \"d*\" AND \"e*\" AND \"f*\" AND \"g*\" AND \"h*\""),
            result);
    }
}
