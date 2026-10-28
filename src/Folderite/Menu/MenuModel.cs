namespace Folderite.Menu;

/// <summary>UI-agnostic menu tree. MenuBuilder renders it as a native popup; a custom UI could replace that.</summary>
public abstract record MenuNode;

/// <param name="Text">Display text; '&amp;' marks the accelerator key.</param>
/// <param name="OnInvoke">Run after the menu closes, on the UI thread.</param>
/// <param name="IconPath">File/folder whose shell icon is shown, or null for none.</param>
/// <param name="Detail">Secondary text shown dimmed and right-aligned (e.g. the parent folder). Raw text: '&amp;' is not an accelerator here.</param>
public sealed record MenuItem(
    string Text,
    Action OnInvoke,
    string? IconPath = null,
    bool Enabled = true,
    bool IsDefault = false,
    string? Detail = null) : MenuNode;

/// <param name="Children">Static children.</param>
/// <param name="LazyChildren">If set, called when the submenu is first opened (WM_INITMENUPOPUP) instead of using <paramref name="Children"/>.</param>
/// <param name="Detail">Secondary text shown dimmed and right-aligned (e.g. the parent folder). Raw text: '&amp;' is not an accelerator here.</param>
public sealed record SubMenu(
    string Text,
    IReadOnlyList<MenuNode> Children,
    string? IconPath = null,
    Func<IReadOnlyList<MenuNode>>? LazyChildren = null,
    string? Detail = null) : MenuNode;

/// <summary>Non-clickable label, rendered disabled.</summary>
public sealed record MenuHeader(string Text, string? IconPath = null) : MenuNode;

public sealed record MenuSeparator : MenuNode
{
    public static readonly MenuSeparator Instance = new();
}
