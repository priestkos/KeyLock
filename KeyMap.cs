namespace KeyLock;

/// <summary>
/// The whole decision of what to rebind, with no Dalamud and no FFXIVClientStructs types, so the
/// offline harness compiles and checks this file as-is.
/// </summary>
/// <remarks>
/// Key values are virtual-key codes, which is what the game stores in a Keybind's KeySetting.
/// </remarks>
internal static class KeyMap
{
    // Keys that also type, plus the keys they move to. Arrow keys are unbound in FFXIV by default.
    private const int W = 0x57;
    private const int A = 0x41;
    private const int S = 0x53;
    private const int D = 0x44;
    private const int Space = 0x20;

    private const int Up = 0x26;
    private const int Left = 0x25;
    private const int Down = 0x28;
    private const int Right = 0x27;
    private const int NoKey = 0x00;

    /// <summary>What a single bound key becomes. False when the key is not one this plugin moves.</summary>
    public static bool TryRemap(int key, out int replacement)
    {
        switch (key)
        {
            case W: replacement = Up; return true;
            case A: replacement = Left; return true;
            case S: replacement = Down; return true;
            case D: replacement = Right; return true;
            case Space: replacement = NoKey; return true;
            default: replacement = key; return false;
        }
    }

    /// <summary>
    /// Rewrites one input's two keyboard binds. False when neither bind is a key this plugin moves,
    /// so the caller leaves that input alone and stores no snapshot for it.
    /// </summary>
    public static bool TryRekey(int primary, int alt, out int newPrimary, out int newAlt)
    {
        var primaryMoved = TryRemap(primary, out newPrimary);
        var altMoved = TryRemap(alt, out newAlt);
        return primaryMoved || altMoved;
    }

    /// <summary>Short readable name for a bound key, used by the status line.</summary>
    public static string Name(int key) => key switch
    {
        W => "W",
        A => "A",
        S => "S",
        D => "D",
        Space => "Space",
        Up => "Up",
        Left => "Left",
        Down => "Down",
        Right => "Right",
        NoKey => "unbound",
        _ => $"vk{key}",
    };
}
