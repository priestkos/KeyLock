using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace KeyLock;

public sealed unsafe class Plugin : IDalamudPlugin
{
    private const string CommandName = "/keylock";

    // Layout of FFXIVClientStructs' Keybind: KeySettings[0].Key, KeySettings[0].Modifier,
    // KeySettings[1].Key, KeySettings[1].Modifier, then the two gamepad settings and 3 spare bytes.
    private const int KeybindSize = KeyMap.KeybindSize;
    private const int PrimaryKeyOffset = 0;
    private const int AltKeyOffset = 2;

    private static readonly InputId[] TargetIds =
    [
        InputId.MOVE_FORE, InputId.MOVE_BACK, InputId.MOVE_LEFT, InputId.MOVE_RIGHT,
        InputId.MOVE_STRIFE_L, InputId.MOVE_STRIFE_R, InputId.MOVE_AND_STEER, InputId.JUMP,
    ];

    public string Name => "KeyLock";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;

    private readonly Configuration configuration;

    public Plugin()
    {
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Move movement off WASD and jump off Space, and put them back. " +
                          "/keylock status prints the live binds; /keylock off restores them.",
        });

        if (this.configuration.Characters.Count > 0 || this.configuration.OriginalBinds.Count > 0)
            Log.Warning("[KeyLock] keybinds are still swapped from a previous session; /keylock off on that character restores them.");
    }

    public void Dispose()
    {
        PluginInterface.SavePluginConfig(this.configuration);
        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args) => this.Handle(args.Trim().ToLowerInvariant());

    private void Handle(string args)
    {
        switch (args)
        {
            case "":
                if (!this.TryBegin(out var character)) return;
                if (this.configuration.Characters.ContainsKey(character)) this.Restore(character);
                else this.Swap(character);
                break;
            case "on":
                if (this.TryBegin(out character)) this.Swap(character);
                break;
            case "off":
            case "restore":
                if (this.TryBegin(out character)) this.Restore(character);
                break;
            case "status":
                if (this.TryBegin(out character)) this.PrintStatus(character);
                break;
            default:
                ChatGui.Print("[KeyLock] /keylock [on|off|restore|status]");
                break;
        }
    }

    /// <summary>Checks a character is logged in and the layout matches, then migrates any legacy global swap.</summary>
    private bool TryBegin(out ulong character)
    {
        character = PlayerState.ContentId;
        if (!KeyMap.CanTouchBinds(ClientState.IsLoggedIn, PlayerState.IsLoaded, character, UIInputData.Instance() != null))
        {
            ChatGui.Print("[KeyLock] not logged in.");
            return false;
        }

        // The byte offsets are only right for this layout; refuse rather than corrupt a bind.
        if (sizeof(Keybind) != KeybindSize)
        {
            Log.Error($"[KeyLock] Keybind is {sizeof(Keybind)} bytes, expected {KeybindSize}; no binds touched.");
            ChatGui.Print("[KeyLock] the game's keybind layout does not match this build; nothing was changed.");
            return false;
        }

        this.AdoptLegacy(character);
        return true;
    }

    private void AdoptLegacy(ulong character)
    {
        var legacy = this.configuration.OriginalBinds;
        if (legacy.Count == 0)
        {
            if (!this.configuration.Swapped) return;
            this.configuration.Swapped = false; // a legacy swap that changed nothing has nothing to restore
            this.Save();
            return;
        }

        if (this.configuration.Characters.ContainsKey(character) || !KeyMap.LegacyMatches(legacy, ReadBind)) return;

        this.configuration.Characters[character] = legacy;
        this.configuration.OriginalBinds = [];
        this.configuration.Swapped = false;
        this.Save();
        ChatGui.Print("[KeyLock] this character's binds match a swap saved by an older version; it is now tracked here.");
    }

    private void Swap(ulong character)
    {
        var changed = KeyMap.Swap(this.configuration.Characters, character, TargetIds.Select(id => (int)id), ReadBind, WriteBind);
        if (changed == null)
        {
            ChatGui.Print("[KeyLock] already swapped; /keylock off restores the original binds.");
            return;
        }

        this.Save();

        ChatGui.Print(changed == 0
            ? "[KeyLock] nothing to swap: no movement or jump bind uses W, A, S, D or Space."
            : $"[KeyLock] swapped {changed} keybind(s): movement is on the arrow keys, Jump has no key. /keylock off restores.");
    }

    private void Restore(ulong character)
    {
        var result = KeyMap.Restore(this.configuration.Characters, character, ReadBind, WriteBind);
        if (result is not var (restored, skipped))
        {
            ChatGui.Print("[KeyLock] nothing saved to restore for this character; set the binds in Keybinds if they look wrong.");
            return;
        }

        this.Save();

        ChatGui.Print($"[KeyLock] restored {restored} keybind(s).");
        if (skipped.Count > 0)
            ChatGui.Print($"[KeyLock] left alone because they changed after the swap: {string.Join(", ", skipped.Select(id => (InputId)id))}.");
    }

    private void PrintStatus(ulong character)
    {
        var input = UIInputData.Instance();
        var saved = this.configuration.Characters.GetValueOrDefault(character);
        ChatGui.Print($"[KeyLock] {(saved != null ? "swapped" : "normal")}, {saved?.Count ?? 0} saved bind(s):");

        foreach (var id in TargetIds)
        {
            var bind = input->GetKeybind(id);
            if (bind == null) continue;
            var bytes = (byte*)bind;
            ChatGui.Print($"  {id,-16} {KeyMap.Name(bytes[PrimaryKeyOffset])} / {KeyMap.Name(bytes[AltKeyOffset])}");
        }

        if (this.configuration.OriginalBinds.Count > 0)
            ChatGui.Print("[KeyLock] a swap saved by an older version does not match this character's binds and was kept " +
                          "for the character it came from: run /keylock off there.");
    }

    /// <summary>The live 11-byte bind for one of this plugin's inputs, or null for any other id.</summary>
    private static byte[]? ReadBind(int id)
    {
        var input = UIInputData.Instance();
        // Ids come from the saved config: never hand the game an id it does not own.
        if (input == null || !TargetIds.Contains((InputId)id) || id < 0 || id >= input->NumKeybinds) return null;
        var bind = input->GetKeybind((InputId)id);
        return bind == null ? null : new ReadOnlySpan<byte>(bind, KeybindSize).ToArray();
    }

    private static void WriteBind(int id, byte[] bytes)
    {
        var input = UIInputData.Instance();
        if (input == null || bytes.Length != KeybindSize) return;
        // The game's setter, not a direct write, is what updates the live table.
        fixed (byte* p = bytes) input->SetKeybind((InputId)id, (Keybind*)p);
    }

    private void Save() => PluginInterface.SavePluginConfig(this.configuration);
}
