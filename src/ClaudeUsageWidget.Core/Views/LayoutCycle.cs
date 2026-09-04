namespace ClaudeUsageWidget.Core;

/// One step through an enum's values, wrapping at the end.
///
/// Every toolbar button cycles its own setting — the spec's alternative was a
/// dropdown, which on a 170 pt panel is the tray menu again. All four cycled
/// settings are enums declared in the order the buttons walk them, so this is
/// the whole of "click cycles".
public static class LayoutCycle
{
    /// The next value after `value`, wrapping. A value outside the enum — a
    /// hand-edited settings file — starts the cycle at the first one rather
    /// than throwing: `Array.IndexOf` returns -1 and -1 + 1 is 0.
    public static T Next<T>(T value) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        return values[(Array.IndexOf(values, value) + 1) % values.Length];
    }
}
