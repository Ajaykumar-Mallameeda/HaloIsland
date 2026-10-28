namespace Halo.App.Island;

/// <summary>One popup-menu item: a command (<see cref="Id"/>), a separator (no text) or a submenu (<see cref="Children"/>).</summary>
internal readonly record struct MenuEntry(int Id, string? Text, bool Checked = false, bool Disabled = false, MenuEntry[]? Children = null);
