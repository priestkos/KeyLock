using KeyLock;

var checks = 0;
var failures = 0;

void Check(string name, bool condition, string detail)
{
    checks++;
    if (condition) return;
    failures++;
    Console.WriteLine($"FAIL {name}: {detail}");
}

const int W = 0x57, A = 0x41, S = 0x53, D = 0x44, Space = 0x20;
const int Up = 0x26, Left = 0x25, Down = 0x28, Right = 0x27, NoKey = 0x00;
const int Q = 0x51, E = 0x45, Enter = 0x0D, Num0 = 0x60, F1 = 0x70;

// The five keys that move, and where each one goes.
var moves = new (int From, int To, string FromName)[]
{
    (W, Up, "W"), (A, Left, "A"), (S, Down, "S"), (D, Right, "D"), (Space, NoKey, "Space"),
};

foreach (var (from, to, fromName) in moves)
{
    var moved = KeyMap.TryRemap(from, out var replacement);
    Check($"remap-{fromName}", moved && replacement == to, $"expected {to}, got {replacement} (moved={moved})");

    // Remapping the target again must do nothing: the swap is idempotent, so a second run cannot
    // walk the binds further along.
    if (to != NoKey)
        Check($"remap-idempotent-{fromName}", !KeyMap.TryRemap(to, out _), $"{to} matched the map");
}

// Nothing else is touched.
foreach (var (key, name) in new[] { (Q, "Q"), (E, "E"), (Enter, "Enter"), (Num0, "NumPad0"), (F1, "F1") })
{
    Check($"untouched-{name}", !KeyMap.TryRemap(key, out var replacement) && replacement == key,
        $"key {key} matched the map");
}

// One input's two keyboard binds are decided together.
var rekeyed = KeyMap.TryRekey(W, 0, out var primary, out var alt);
Check("rekey-primary", rekeyed && primary == Up && alt == 0, $"{primary}/{alt}");

rekeyed = KeyMap.TryRekey(E, Space, out primary, out alt);
Check("rekey-alt", rekeyed && primary == E && alt == NoKey, $"{primary}/{alt}");

rekeyed = KeyMap.TryRekey(W, D, out primary, out alt);
Check("rekey-both", rekeyed && primary == Up && alt == Right, $"{primary}/{alt}");

rekeyed = KeyMap.TryRekey(Q, E, out primary, out alt);
Check("rekey-none", !rekeyed && primary == Q && alt == E, $"{primary}/{alt}");

rekeyed = KeyMap.TryRekey(0, 0, out primary, out alt);
Check("rekey-unbound", !rekeyed && primary == 0 && alt == 0, $"{primary}/{alt}");

// A default FFXIV keyboard loadout, as the plugin would see it: only the four movement binds and
// Jump change; the Q/E strafes and the unbound "move and steer" input are left alone.
var loadout = new (string Id, int Primary, int Alt, bool ShouldChange)[]
{
    ("MOVE_FORE", W, 0, true),
    ("MOVE_BACK", S, 0, true),
    ("MOVE_LEFT", A, 0, true),
    ("MOVE_RIGHT", D, 0, true),
    ("MOVE_STRIFE_L", Q, 0, false),
    ("MOVE_STRIFE_R", E, 0, false),
    ("MOVE_AND_STEER", 0, 0, false),
    ("JUMP", Space, 0, true),
};

var changed = 0;
foreach (var (id, boundPrimary, boundAlt, shouldChange) in loadout)
{
    var touched = KeyMap.TryRekey(boundPrimary, boundAlt, out _, out _);
    if (touched) changed++;
    Check($"loadout-{id}", touched == shouldChange, $"changed={touched}, expected {shouldChange}");
}

Check("loadout-count", changed == 5, $"changed={changed}");

// No two movement inputs may land on the same arrow key, or a direction would stop existing.
var targets = moves.Where(m => m.To != NoKey).Select(m => m.To).ToArray();
Check("targets-distinct", targets.Distinct().Count() == targets.Length, string.Join(",", targets));

// Status line names.
Check("name-W", KeyMap.Name(W) == "W", KeyMap.Name(W));
Check("name-space", KeyMap.Name(Space) == "Space", KeyMap.Name(Space));
Check("name-up", KeyMap.Name(Up) == "Up", KeyMap.Name(Up));
Check("name-unbound", KeyMap.Name(NoKey) == "unbound", KeyMap.Name(NoKey));
Check("name-other", KeyMap.Name(F1) == "vk112", KeyMap.Name(F1));

// --- Snapshot/restore state machine over a fake bind table (11-byte Keybinds, keyed by InputId). ---
const int Shift = 0x01;
const int MoveFore = 321, Jump = 348;
byte[] Bind(int key, int mod = 0, int alt = 0, int altMod = 0) =>
    [(byte)key, (byte)mod, (byte)alt, (byte)altMod, 0xA1, 0xA2, 0xA3, 0xA4, 0, 0, 0];

Dictionary<int, byte[]> Table() => new() { [MoveFore] = Bind(W), [Jump] = Bind(Space, Shift) };
Func<int, byte[]?> Reader(Dictionary<int, byte[]> table) => id => table.TryGetValue(id, out var b) ? (byte[])b.Clone() : null;
Action<int, byte[]> Writer(Dictionary<int, byte[]> table) => (id, b) => table[id] = (byte[])b.Clone();
int[] ids = [MoveFore, Jump];
const ulong CharA = 1, CharB = 2;

// Swap writes arrows and clears Jump's Space together with its Shift modifier; gamepad bytes untouched.
var state = new Dictionary<ulong, Dictionary<int, byte[]>>();
var tableA = Table();
var swapped = KeyMap.Swap(state, CharA, ids, Reader(tableA), Writer(tableA), () => { });
Check("swap-count", swapped == 2, $"{swapped}");
Check("swap-fore", tableA[MoveFore][0] == Up, $"{tableA[MoveFore][0]}");
Check("swap-nokey-clears-modifier", tableA[Jump][0] == NoKey && tableA[Jump][1] == 0, $"{tableA[Jump][0]}/{tableA[Jump][1]}");
Check("swap-gamepad-untouched", tableA[Jump].Skip(4).SequenceEqual(Table()[Jump].Skip(4)), "gamepad bytes changed");
Check("swap-snapshot", state[CharA][Jump].SequenceEqual(Table()[Jump]), "snapshot is not the pre-swap bind");

// On while swapped: refused, nothing written, snapshot kept (a second swap would save the swapped layout).
var before = tableA[MoveFore].ToArray();
Check("on-while-swapped", KeyMap.Swap(state, CharA, ids, Reader(tableA), Writer(tableA), () => { }) == null
    && tableA[MoveFore].SequenceEqual(before) && state[CharA][MoveFore][0] == W, "second swap acted");

// Per-character isolation: B's state is its own, and B's restore never touches A's snapshot.
var tableB = new Dictionary<int, byte[]> { [MoveFore] = Bind(W), [Jump] = Bind(Q) };
Check("isolation-off-other-char", KeyMap.Restore(state, CharB, Reader(tableB), Writer(tableB)) == null
    && tableB[MoveFore][0] == W && state.ContainsKey(CharA), "B restored A's snapshot");
KeyMap.Swap(state, CharB, ids, Reader(tableB), Writer(tableB), () => { });
Check("isolation-swap-b", state[CharB].Count == 1 && tableB[Jump][0] == Q && state[CharA].Count == 2, "B swap leaked");

// Restore only writes back binds still holding what the swap wrote; gamepad bytes keep their live value.
tableA[Jump] = Bind(F1, 0, 0, 0);                    // user rebound Jump himself after the swap
tableA[MoveFore][5] = 0x55;                          // gamepad changed since the swap
var result = KeyMap.Restore(state, CharA, Reader(tableA), Writer(tableA));
Check("restore-count", result is { Restored: 1 } r1 && r1.Skipped.SequenceEqual([Jump]), $"{result}");
Check("restore-skip-foreign", tableA[Jump][0] == F1, $"{tableA[Jump][0]}");
Check("restore-keyboard", tableA[MoveFore][0] == W && tableA[MoveFore][1] == 0, $"{tableA[MoveFore][0]}");
Check("restore-keeps-gamepad", tableA[MoveFore][5] == 0x55, $"{tableA[MoveFore][5]}");
Check("restore-forgets", !state.ContainsKey(CharA) && state.ContainsKey(CharB), "state not cleared per character");

// Off with no state: nothing written.
Check("off-no-state", KeyMap.Restore(state, CharA, Reader(tableA), Writer(tableA)) == null, "restored with no state");

// Ids the reader refuses (unknown to the plugin) are skipped and reported, never written.
var bogus = new Dictionary<ulong, Dictionary<int, byte[]>> { [CharA] = new() { [99999] = Bind(W) } };
var writes = 0;
var bogusResult = KeyMap.Restore(bogus, CharA, _ => null, (_, _) => writes++);
Check("restore-unknown-id", writes == 0 && bogusResult is { Restored: 0 } r2 && r2.Skipped.SequenceEqual([99999]), $"{bogusResult}");

// Legacy global snapshot: adopted only when the live binds equal the swapped layout, including the
// older build's Shift+NoKey leftover on Jump.
var legacy = new Dictionary<int, byte[]> { [MoveFore] = Bind(W), [Jump] = Bind(Space, Shift) };
var legacyLive = new Dictionary<int, byte[]> { [MoveFore] = Bind(Up), [Jump] = Bind(NoKey, Shift) };
Check("legacy-matches", KeyMap.LegacyMatches(legacy, Reader(legacyLive)), "swapped layout not recognised");
Check("legacy-other-char", !KeyMap.LegacyMatches(legacy, Reader(Table())), "unswapped character adopted it");
Check("legacy-empty", !KeyMap.LegacyMatches([], Reader(legacyLive)), "empty legacy adopted");

// Snapshot of the wrong size is never swapped or restored.
Check("bad-size", KeyMap.Swapped(new byte[4]) == null && KeyMap.Restored(Bind(Up), new byte[4]) == null, "short bind used");

// Logged-in gate: title screen / character select have the input table but no character.
Check("gate-title-screen", !KeyMap.CanTouchBinds(false, false, 0, true), "title screen allowed");
Check("gate-char-select", !KeyMap.CanTouchBinds(false, true, CharA, true), "character select allowed");
Check("gate-no-content-id", !KeyMap.CanTouchBinds(true, true, 0, true), "content id 0 allowed");
Check("gate-no-input", !KeyMap.CanTouchBinds(true, true, CharA, false), "missing input table allowed");
Check("gate-logged-in", KeyMap.CanTouchBinds(true, true, CharA, true), "logged-in character refused");

// A write that throws part-way must never lose a saved original: the swap records every original before
// the first write, and a restore forgets the character only after all its writes went through.
Action<int, byte[]> ThrowingWriter(Dictionary<int, byte[]> table, int failOn) => (id, b) =>
{
    if (id == failOn) throw new InvalidOperationException("write failed");
    table[id] = (byte[])b.Clone();
};
var crashState = new Dictionary<ulong, Dictionary<int, byte[]>>();
var crashTable = Table();
try { KeyMap.Swap(crashState, CharA, ids, Reader(crashTable), ThrowingWriter(crashTable, Jump), () => { }); } catch (InvalidOperationException) { }
Check("swap-throw-keeps-originals", crashState.TryGetValue(CharA, out var kept) && kept.Count == 2
    && kept[MoveFore][0] == W && crashTable[MoveFore][0] == Up, "an original written to the game was not saved");
try { KeyMap.Restore(crashState, CharA, Reader(crashTable), ThrowingWriter(crashTable, MoveFore)); } catch (InvalidOperationException) { }
Check("restore-throw-keeps-state", crashState.ContainsKey(CharA) && crashState[CharA][MoveFore][0] == W,
    "a failed restore forgot the originals");

// The originals reach the config file before the game sees any write.
var persistState = new Dictionary<ulong, Dictionary<int, byte[]>>();
var persistTable = Table();
var persistedBeforeWrite = false;
KeyMap.Swap(persistState, CharA, ids, Reader(persistTable), Writer(persistTable), () =>
    persistedBeforeWrite = persistState.TryGetValue(CharA, out var p) && p.Count == 2 && persistTable[MoveFore][0] == W);
Check("swap-persists-before-write", persistedBeforeWrite && persistTable[MoveFore][0] == Up, "persist ran after a write");

Console.WriteLine($"{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;
