namespace Folderite.Menu;

/// <summary>Mnemonic lookup for owner-drawn menu items, which Windows does not handle itself (it sends WM_MENUCHAR).</summary>
internal static class Accelerators
{
    /// <summary>The accelerator character of a menu text ("Reloa&amp;d" gives 'D'), or null. "&amp;&amp;" is a literal ampersand.</summary>
    public static char? Of(string? text)
    {
        if (text is null)
            return null;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
                continue;
            if (i + 1 >= text.Length)
                return null;
            if (text[i + 1] == '&')
            {
                i++;
                continue;
            }
            return char.ToUpperInvariant(text[i + 1]);
        }
        return null;
    }

    /// <summary>
    /// Picks the item for a WM_MENUCHAR key press among one popup's items, in menu order. A null text means the item
    /// can't be chosen. <paramref name="current"/> is the highlighted index or -1.
    /// </summary>
    public static (int Index, bool Unique) Find(IReadOnlyList<string?> texts, char key, int current)
    {
        char upper = char.ToUpperInvariant(key);

        var matches = new List<int>();
        for (int i = 0; i < texts.Count; i++)
        {
            if (Of(texts[i]) == upper)
                matches.Add(i);
        }

        if (matches.Count == 0)
        {
            for (int i = 0; i < texts.Count; i++)
            {
                if (FirstVisible(texts[i]) == upper)
                    matches.Add(i);
            }
        }

        if (matches.Count == 0)
            return (-1, false);
        if (matches.Count == 1)
            return (matches[0], true);

        foreach (int index in matches)
        {
            if (index > current)
                return (index, false);
        }
        return (matches[0], false);
    }

    private static char? FirstVisible(string? text)
    {
        if (text is null)
            return null;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '&')
            {
                if (i + 1 < text.Length && text[i + 1] == '&')
                    i++;
                else
                    continue;
            }
            if (!char.IsWhiteSpace(c))
                return char.ToUpperInvariant(c);
        }
        return null;
    }
}
