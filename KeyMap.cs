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

    // Keybind layout: KeySettings[0].{Key, Modifier}, KeySettings[1].{Key, Modifier} in bytes 0x00-0x03,
    // then the two gamepad settings and 3 spare bytes, which the swap never touches.
    public const int KeybindSize = 11;
    private const int KeyboardBytes = 4;

    /// <summary>
    /// The bind the swap writes for <paramref name="original"/>, or null when it uses none of the moved
    /// keys. A key moved to "no key" also loses its modifier, so no Shift+NoKey is left behind.
    /// </summary>
    public static byte[]? Swapped(byte[] original)
    {
        if (original.Length != KeybindSize) return null;
        var patched = (byte[])original.Clone();
        var moved = false;
        for (var offset = 0; offset < KeyboardBytes; offset += 2)
        {
            if (!TryRemap(patched[offset], out var key)) continue;
            patched[offset] = (byte)key;
            if (key == NoKey) patched[offset + 1] = 0;
            moved = true;
        }

        return moved ? patched : null;
    }

    /// <summary>
    /// True when the live bind's keyboard slots still hold what the swap wrote for <paramref name="original"/>.
    /// The modifier of an empty slot is ignored: it means nothing, and older builds left it set.
    /// </summary>
    public static bool StillSwapped(byte[] live, byte[] original)
    {
        var written = Swapped(original);
        if (written == null || live.Length != KeybindSize) return false;
        for (var offset = 0; offset < KeyboardBytes; offset += 2)
        {
            if (live[offset] != written[offset]) return false;
            if (written[offset] != NoKey && live[offset + 1] != written[offset + 1]) return false;
        }

        return true;
    }

    /// <summary>
    /// The bind to write back: the live bind with only its keyboard bytes reset to <paramref name="original"/>,
    /// or null when someone changed the bind after the swap, so a foreign bind is never overwritten.
    /// </summary>
    public static byte[]? Restored(byte[] live, byte[] original)
    {
        if (!StillSwapped(live, original)) return null;
        var result = (byte[])live.Clone();
        Array.Copy(original, result, KeyboardBytes);
        return result;
    }

    /// <summary>
    /// Swaps <paramref name="ids"/> for one character and saves each pre-swap bind under it. Null when that
    /// character is already swapped; otherwise the number of binds written. A character counts as swapped
    /// while it has an entry in <paramref name="state"/>, even an empty one. Every original is recorded and
    /// <paramref name="persist"/> runs before the first write, so no write can outlive its saved original.
    /// </summary>
    public static int? Swap(Dictionary<ulong, Dictionary<int, byte[]>> state, ulong character, IEnumerable<int> ids,
        Func<int, byte[]?> read, Action<int, byte[]> write, Action persist)
    {
        if (state.ContainsKey(character)) return null;
        var saved = new Dictionary<int, byte[]>();
        var patches = new Dictionary<int, byte[]>();
        foreach (var id in ids)
        {
            var live = read(id);
            var patched = live == null ? null : Swapped(live);
            if (patched == null) continue;
            saved[id] = live!;
            patches[id] = patched;
        }

        state[character] = saved;
        persist();
        foreach (var (id, patched) in patches) write(id, patched);
        return saved.Count;
    }

    /// <summary>
    /// Puts one character's saved binds back and forgets them. Null when nothing is saved for that character;
    /// otherwise the restored count and the ids skipped because their live bind no longer matches the swap.
    /// </summary>
    public static (int Restored, List<int> Skipped)? Restore(Dictionary<ulong, Dictionary<int, byte[]>> state,
        ulong character, Func<int, byte[]?> read, Action<int, byte[]> write)
    {
        if (!state.TryGetValue(character, out var saved)) return null;
        var restored = 0;
        var skipped = new List<int>();
        foreach (var (id, original) in saved)
        {
            var live = read(id);
            var back = live == null ? null : Restored(live, original);
            if (back == null)
            {
                skipped.Add(id);
                continue;
            }

            write(id, back);
            restored++;
        }

        state.Remove(character); // only once every write went through: a throw keeps the originals
        return (restored, skipped);
    }

    /// <summary>
    /// True when a pre-per-character snapshot belongs to the character now logged in: every live bind still
    /// equals what the swap wrote. Anything else and the snapshot is left alone.
    /// </summary>
    public static bool LegacyMatches(Dictionary<int, byte[]> legacy, Func<int, byte[]?> read) =>
        legacy.Count > 0 && legacy.All(p => read(p.Key) is { } live && StillSwapped(live, p.Value));

    /// <summary>
    /// Binds may be read or written only for a loaded, logged-in character. The input table alone is not
    /// enough: it exists at the title screen and character select too, where there are no character binds.
    /// </summary>
    public static bool CanTouchBinds(bool loggedIn, bool playerLoaded, ulong contentId, bool hasInputTable) =>
        loggedIn && playerLoaded && contentId != 0 && hasInputTable;

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
