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

Console.WriteLine($"{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;
