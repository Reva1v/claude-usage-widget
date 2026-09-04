namespace ClaudeUsageWidget.Core;

/// Whether the per-model dial gets a cell in every block. Only the CELL is at
/// stake: the tray tooltip, the taskbar band and <see cref="DialModel.All"/>
/// keep reporting the model percentage either way, because none of them reads
/// the layout. The user asked for the option, not the removal — an account
/// can have no per-model bucket worth a square (2026-09-04).
public enum ModelDial
{
    Shown,
    Hidden,
}

public static class ModelDials
{
    /// The value a settings file means. Null is a file written before the
    /// setting existed, and it reads as Shown: defaulting to Hidden would take
    /// the dial off every panel that upgrades. Unlike
    /// <see cref="StatusModes.Resolve"/> there is nothing to read off the
    /// layout — the model cell was mandatory until now, so its presence says
    /// nothing about what anyone chose.
    public static ModelDial Resolve(ModelDial? saved) => saved ?? ModelDial.Shown;
}