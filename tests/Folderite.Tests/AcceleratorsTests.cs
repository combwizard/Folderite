using Folderite.Menu;

namespace Folderite.Tests;

public sealed class AcceleratorsTests
{
    [Theory]
    [InlineData("Reloa&d", 'D')]
    [InlineData("&Copy path", 'C')]
    [InlineData("&copy", 'C')]
    [InlineData("&&&x", 'X')]
    [InlineData("A && &B", 'B')]
    public void Of_returns_the_upper_cased_accelerator(string text, char expected) =>
        Assert.Equal(expected, Accelerators.Of(text));

    [Theory]
    [InlineData("Reload")]
    [InlineData("Fish && Chips")]
    [InlineData("&&")]
    [InlineData("Trailing &")]
    [InlineData("&")]
    [InlineData("")]
    [InlineData(null)]
    public void Of_returns_null_without_an_accelerator(string? text) =>
        Assert.Null(Accelerators.Of(text));

    [Fact]
    public void Find_returns_the_unique_explicit_match()
    {
        var texts = new string?[] { "&Alpha", "B&eta", "&Gamma" };

        Assert.Equal((1, true), Accelerators.Find(texts, 'E', -1));
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        var texts = new string?[] { "&Alpha", "B&eta" };

        Assert.Equal((1, true), Accelerators.Find(texts, 'e', -1));
        Assert.Equal((0, true), Accelerators.Find(texts, 'a', -1));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 0)]
    public void Find_cycles_through_several_matches(int current, int expected)
    {
        var texts = new string?[] { "&Pa", "Zed", "&Pb", "Zee", "&Pc" };

        Assert.Equal((expected, false), Accelerators.Find(texts, 'p', current));
    }

    [Fact]
    public void Find_falls_back_to_first_letter_when_there_are_no_explicit_matches()
    {
        var texts = new string?[] { "Apple", "  Amp", "Banana", "&&Fish" };

        Assert.Equal((0, false), Accelerators.Find(texts, 'a', -1));
        Assert.Equal((1, false), Accelerators.Find(texts, 'a', 0));
        Assert.Equal((0, false), Accelerators.Find(texts, 'a', 1));
        Assert.Equal((2, true), Accelerators.Find(texts, 'b', -1));
        Assert.Equal((3, true), Accelerators.Find(texts, '&', -1));
    }

    [Fact]
    public void Find_ignores_first_letters_when_an_explicit_match_exists()
    {
        var texts = new string?[] { "Apple", "B&anana" };

        Assert.Equal((1, true), Accelerators.Find(texts, 'a', -1));
    }

    [Fact]
    public void Find_skips_null_entries()
    {
        var texts = new string?[] { null, "&Alpha", null, "&Another" };

        Assert.Equal((1, false), Accelerators.Find(texts, 'a', -1));
        Assert.Equal((3, false), Accelerators.Find(texts, 'a', 1));
        Assert.Equal((-1, false), Accelerators.Find(new string?[] { null, null }, 'a', -1));
    }

    [Fact]
    public void Find_returns_no_match_for_unknown_keys_and_empty_lists()
    {
        Assert.Equal((-1, false), Accelerators.Find(new string?[] { "&Alpha", "Beta" }, 'z', -1));
        Assert.Equal((-1, false), Accelerators.Find([], 'a', -1));
    }
}
