using System.Globalization;
using System.Runtime.InteropServices;

namespace Folderite.Hooks;

/// <summary>Modifier keys of a <see cref="Hotkey"/>. The values are deliberately the RegisterHotKey MOD_* values.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global keyboard shortcut: modifiers plus one Win32 virtual-key code. Parses and prints the text kept in
/// config.json ("Ctrl+Alt+F5"). Everything is pure except single punctuation keys, which depend on the keyboard layout.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint Key)
{
    private const uint VkSpace = 0x20, VkPrior = 0x21, VkNext = 0x22, VkEnd = 0x23, VkHome = 0x24,
        VkLeft = 0x25, VkUp = 0x26, VkRight = 0x27, VkDown = 0x28, VkInsert = 0x2D, VkDelete = 0x2E,
        VkF1 = 0x70, VkF24 = 0x87, VkOemPlus = 0xBB;

    private const HotkeyModifiers AllModifiers =
        HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;

    private static readonly (string Name, uint Vk)[] NamedKeys =
    [
        ("Space", VkSpace), ("Insert", VkInsert), ("Delete", VkDelete), ("Del", VkDelete),
        ("Home", VkHome), ("End", VkEnd), ("PageUp", VkPrior), ("PageDown", VkNext),
        ("Up", VkUp), ("Down", VkDown), ("Left", VkLeft), ("Right", VkRight),
    ];

    /// <summary>
    /// A shortcut needs a modifier so it can't hijack plain typing; function keys are the one exception.
    /// </summary>
    public bool IsValid =>
        Key is >= 1 and <= 0xFF
        && (Modifiers & ~AllModifiers) == 0
        && (Modifiers != HotkeyModifiers.None || IsFunctionKey(Key));

    /// <summary>
    /// Parses text such as "Ctrl+Alt+F5" (case-insensitive, parts separated by '+', spaces allowed).
    /// Returns false for anything that isn't a single key plus modifiers, and for combinations that aren't
    /// <see cref="IsValid"/> (a letter with no modifier), so a true result is always safe to register.
    /// </summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        text = text.Trim();
        string[] parts;
        if (text.EndsWith('+'))
        {
            // The plus key itself ("Ctrl++"): the last '+' is the key, the one before it is the separator.
            var rest = text[..^1].TrimEnd();
            if (rest.Length > 0)
            {
                if (!rest.EndsWith('+'))
                    return false; // "Ctrl+" has nothing after the separator
                rest = rest[..^1];
            }
            string[] mods = rest.Length == 0 ? [] : rest.Split('+');
            parts = [.. mods, "+"];
        }
        else
        {
            parts = text.Split('+');
        }

        var modifiers = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            if (part.Length == 0)
                return false;

            if (ParseModifier(part) is { } mod)
            {
                modifiers |= mod;
                continue;
            }
            if (key is not null || ParseKey(part) is not { } vk)
                return false;
            key = vk;
        }

        if (key is null)
            return false;

        var result = new Hotkey(modifiers, key.Value);
        if (!result.IsValid)
            return false;
        hotkey = result;
        return true;
    }

    /// <summary>The canonical text, e.g. "Win+Shift+F" or "Ctrl+Alt+F5". <see cref="TryParse"/> reads it back.</summary>
    public override string ToString() => Format(Modifiers, Key);

    /// <summary>Canonical text for any modifiers + virtual key; modifiers come in the order Win, Ctrl, Alt, Shift.</summary>
    public static string Format(HotkeyModifiers mods, uint vk)
    {
        var parts = new List<string>(5);
        if (mods.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        if (mods.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(KeyName(vk));
        return string.Join('+', parts);
    }

    private static bool IsFunctionKey(uint vk) => vk is >= VkF1 and <= VkF24;

    private static HotkeyModifiers? ParseModifier(string part) => part.ToLowerInvariant() switch
    {
        "ctrl" or "control" => HotkeyModifiers.Control,
        "alt" => HotkeyModifiers.Alt,
        "shift" => HotkeyModifiers.Shift,
        "win" or "windows" => HotkeyModifiers.Win,
        _ => null,
    };

    private static uint? ParseKey(string part)
    {
        if (part.Length == 1)
        {
            var c = char.ToUpperInvariant(part[0]);
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
                return c;
            return PunctuationToVk(c);
        }

        foreach (var (name, vk) in NamedKeys)
            if (part.Equals(name, StringComparison.OrdinalIgnoreCase))
                return vk;

        if ((part[0] is 'F' or 'f') && int.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n is >= 1 and <= 24)
            return VkF1 + (uint)(n - 1);

        // The fallback form that KeyName writes for keys without a printable name.
        if (part.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(part.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex)
            && hex is >= 1 and <= 0xFF)
            return hex;

        return null;
    }

    private static string KeyName(uint vk)
    {
        if (vk is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            return ((char)vk).ToString();
        if (IsFunctionKey(vk))
            return "F" + (vk - VkF1 + 1).ToString(CultureInfo.InvariantCulture);
        foreach (var (name, v) in NamedKeys)
            if (v == vk && name != "Del")
                return name;

        // Only the OEM punctuation keys have a character worth showing; MapVirtualKey would also answer for the numpad.
        if (vk is (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2)
        {
            var c = MapVirtualKeyW(vk, 2) & 0x7FFFFFFF; // the top bit marks a dead key
            if (c is > 0x20 and < 0x7F && PunctuationToVk((char)c) == vk)
                return ((char)c).ToString();
        }
        return "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>Resolves a punctuation character on the current layout, only if it has a key of its own (no Shift/Ctrl/Alt needed).</summary>
    private static uint? PunctuationToVk(char c)
    {
        if (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || char.IsControl(c))
            return null;

        var r = VkKeyScanW(c);
        if (r != -1 && ((r >> 8) & 0xFF) == 0 && (r & 0xFF) != 0)
            return (uint)(r & 0xFF);

        // '+' is shifted on many layouts (US: Shift+=), but "Ctrl++" should still work there: use the key that carries it.
        return c == '+' ? VkOemPlus : null;
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);
}
