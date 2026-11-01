using Folderite.Hooks;

namespace Folderite.Tests;

public sealed class HotkeyTests
{
    private const HotkeyModifiers Ctrl = HotkeyModifiers.Control;
    private const HotkeyModifiers Alt = HotkeyModifiers.Alt;
    private const HotkeyModifiers Shift = HotkeyModifiers.Shift;
    private const HotkeyModifiers Win = HotkeyModifiers.Win;

    [Theory]
    [InlineData("Win+Shift+F")]
    [InlineData("Ctrl+Alt+F5")]
    [InlineData("Win+Ctrl+Alt+Shift+Z")]
    [InlineData("Ctrl+0")]
    [InlineData("Alt+Space")]
    [InlineData("Ctrl+PageUp")]
    [InlineData("Ctrl+PageDown")]
    [InlineData("Win+Left")]
    [InlineData("Ctrl+Delete")]
    [InlineData("Shift+Insert")]
    [InlineData("F12")]
    [InlineData("F24")]
    public void ToString_round_trips_through_TryParse(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(text, hotkey.ToString());
    }

    [Fact]
    public void Parse_gives_the_win32_values()
    {
        Assert.True(Hotkey.TryParse("Ctrl+Shift+A", out var hotkey));

        Assert.Equal(Ctrl | Shift, hotkey.Modifiers);
        Assert.Equal((uint)'A', hotkey.Key);
        Assert.True(Hotkey.TryParse("Alt+F5", out hotkey));
        Assert.Equal(0x74u, hotkey.Key);
        Assert.Equal(1u, (uint)hotkey.Modifiers);
    }

    [Theory]
    [InlineData("ctrl+alt+f", Ctrl | Alt, 'F')]
    [InlineData("CONTROL+ALT+F", Ctrl | Alt, 'F')]
    [InlineData("Windows+Shift+x", Win | Shift, 'X')]
    [InlineData("  Ctrl  +  Shift + 7 ", Ctrl | Shift, '7')]
    [InlineData("Alt+SPACE", Alt, 0x20)]
    [InlineData("Ctrl+del", Ctrl, 0x2E)]
    [InlineData("ctrl+pageup", Ctrl, 0x21)]
    [InlineData("Ctrl+f1", Ctrl, 0x70)]
    [InlineData("Ctrl+Ctrl+A", Ctrl, 'A')]
    public void Parse_accepts_aliases_case_and_spacing(string text, HotkeyModifiers mods, int vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(mods, (uint)vk), hotkey);
    }

    [Theory]
    [InlineData("A+Ctrl+Shift+Alt+Win", "Win+Ctrl+Alt+Shift+A")]
    [InlineData("shift+alt+ctrl+F4", "Ctrl+Alt+Shift+F4")]
    [InlineData("Alt+Win+Q", "Win+Alt+Q")]
    [InlineData("Windows+Control+del", "Win+Ctrl+Delete")]
    public void ToString_uses_the_canonical_modifier_order(string text, string expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(expected, hotkey.ToString());
    }

    [Fact]
    public void Format_matches_ToString()
    {
        Assert.Equal("Win+Shift+F", Hotkey.Format(Shift | Win, 'F'));
        Assert.Equal("Ctrl+Alt+F5", Hotkey.Format(Alt | Ctrl, 0x74));
        Assert.Equal("F8", Hotkey.Format(HotkeyModifiers.None, 0x77));
        Assert.Equal(new Hotkey(Ctrl, 'K').ToString(), Hotkey.Format(Ctrl, 'K'));
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F12")]
    [InlineData("F24")]
    public void Function_key_without_modifier_is_valid(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.True(hotkey.IsValid);
        Assert.Equal(HotkeyModifiers.None, hotkey.Modifiers);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("5")]
    [InlineData("Space")]
    [InlineData("Delete")]
    public void Letter_or_other_key_without_modifier_is_invalid(string text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
        Assert.False(new Hotkey(HotkeyModifiers.None, 'A').IsValid);
        Assert.False(new Hotkey(HotkeyModifiers.None, 0x20).IsValid);
    }

    [Fact]
    public void IsValid_follows_the_rule()
    {
        Assert.True(new Hotkey(Ctrl, 'A').IsValid);
        Assert.True(new Hotkey(HotkeyModifiers.None, 0x7B).IsValid);
        Assert.False(new Hotkey(Ctrl, 0).IsValid);
        Assert.False(default(Hotkey).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F1+F2")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+Alt+Shift+Win")]
    [InlineData("Ctrl+")]
    [InlineData("+A")]
    [InlineData("Ctrl+++")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+AB")]
    [InlineData("Ctrl+0x0")]
    [InlineData("Ctrl+0x100")]
    public void Garbage_two_keys_or_modifiers_only_are_rejected(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(default, hotkey);
    }

    [Fact]
    public void Comma_and_period_parse_on_this_layout()
    {
        // ',' and '.' have keys of their own on practically every Latin layout ('/' doesn't: Swedish has it on Shift+7).
        foreach (var key in new[] { ",", "." })
        {
            Assert.True(Hotkey.TryParse("Ctrl+" + key, out var hotkey), key);
            Assert.Equal(Ctrl, hotkey.Modifiers);
            Assert.NotEqual(0u, hotkey.Key);
            Assert.True(hotkey.IsValid);
        }
    }

    [Fact]
    public void Punctuation_round_trips()
    {
        foreach (var key in new[] { "/", ",", ".", ";", "'", "[", "]", "\\", "`", "-", "=" })
        {
            // A key that is shifted on this layout is rejected on parse; only check the ones that parse.
            if (!Hotkey.TryParse("Ctrl+Alt+" + key, out var hotkey))
                continue;

            Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again), key);
            Assert.Equal(hotkey, again);
        }
    }

    [Fact]
    public void Plus_key_parses_as_a_key_not_a_separator()
    {
        Assert.True(Hotkey.TryParse("Ctrl++", out var hotkey));
        Assert.Equal(Ctrl, hotkey.Modifiers);
        Assert.NotEqual(0u, hotkey.Key);
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }

    [Fact]
    public void Unnamed_keys_use_the_hex_form_and_round_trip()
    {
        // VK_NUMPAD1: no printable name of its own, and MapVirtualKey would answer "1", which is a different key.
        var hotkey = new Hotkey(Ctrl, 0x61);

        Assert.Equal("Ctrl+0x61", hotkey.ToString());
        Assert.True(Hotkey.TryParse(hotkey.ToString(), out var again));
        Assert.Equal(hotkey, again);
    }
}
