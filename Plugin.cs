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
    private const int KeybindSize = 11;
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

    private readonly Configuration configuration;

    public Plugin()
    {
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Move movement off WASD and jump off Space, and put them back. " +
                          "/keylock status prints the live binds; /keylock off restores them.",
        });

        if (this.configuration.Swapped)
            Log.Warning("[KeyLock] keybinds are still swapped from a previous session; /keylock off restores them.");
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
                if (this.configuration.Swapped) this.Restore();
                else this.Swap();
                break;
            case "on":
                this.Swap();
                break;
            case "off":
            case "restore":
                this.Restore();
                break;
            case "status":
                this.PrintStatus();
                break;
            default:
                ChatGui.Print("[KeyLock] /keylock [on|off|restore|status]");
                break;
        }
    }

    private void Swap()
    {
        var input = UIInputData.Instance();
        if (input == null)
        {
            ChatGui.Print("[KeyLock] not logged in.");
            return;
        }

        if (this.configuration.Swapped)
        {
            ChatGui.Print("[KeyLock] already swapped; /keylock off restores the original binds.");
            return;
        }

        // The byte offsets below are only right for this layout; refuse rather than corrupt a bind.
        if (sizeof(Keybind) != KeybindSize)
        {
            Log.Error($"[KeyLock] Keybind is {sizeof(Keybind)} bytes, expected {KeybindSize}; no binds written.");
            ChatGui.Print("[KeyLock] the game's keybind layout does not match this build; nothing was changed.");
            return;
        }

        var changed = 0;
        foreach (var id in TargetIds)
        {
            var bind = input->GetKeybind(id);
            if (bind == null) continue;

            var original = *bind;
            var patched = original;
            var bytes = (byte*)&patched;
            if (!KeyMap.TryRekey(bytes[PrimaryKeyOffset], bytes[AltKeyOffset], out var primary, out var alt)) continue;

            bytes[PrimaryKeyOffset] = (byte)primary;
            bytes[AltKeyOffset] = (byte)alt;

            this.configuration.OriginalBinds[(int)id] = new ReadOnlySpan<byte>(&original, KeybindSize).ToArray();
            input->SetKeybind(id, &patched);
            changed++;
        }

        this.configuration.Swapped = true;
        this.Save();

        ChatGui.Print(changed == 0
            ? "[KeyLock] nothing to swap: no movement or jump bind uses W, A, S, D or Space."
            : $"[KeyLock] swapped {changed} keybind(s): movement is on the arrow keys, Jump has no key. /keylock off restores.");
    }

    private void Restore()
    {
        var input = UIInputData.Instance();
        if (input == null)
        {
            ChatGui.Print("[KeyLock] not logged in.");
            return;
        }

        if (this.configuration.OriginalBinds.Count == 0)
        {
            ChatGui.Print("[KeyLock] nothing saved to restore; set the binds in Keybinds if they look wrong.");
            this.configuration.Swapped = false;
            this.Save();
            return;
        }

        var restored = 0;
        foreach (var (id, saved) in this.configuration.OriginalBinds)
        {
            if (saved.Length != KeybindSize) continue;
            var bind = input->GetKeybind((InputId)id);
            if (bind == null) continue;

            // Write through a copy so the game's setter, not us, is what updates the live table.
            var original = *bind;
            new ReadOnlySpan<byte>(saved).CopyTo(new Span<byte>(&original, KeybindSize));
            input->SetKeybind((InputId)id, &original);
            restored++;
        }

        this.configuration.OriginalBinds.Clear();
        this.configuration.Swapped = false;
        this.Save();

        ChatGui.Print($"[KeyLock] restored {restored} keybind(s).");
    }

    private void PrintStatus()
    {
        var input = UIInputData.Instance();
        if (input == null)
        {
            ChatGui.Print("[KeyLock] not logged in.");
            return;
        }

        ChatGui.Print($"[KeyLock] {(this.configuration.Swapped ? "swapped" : "normal")}, " +
                      $"{this.configuration.OriginalBinds.Count} saved bind(s):");

        foreach (var id in TargetIds)
        {
            var bind = input->GetKeybind(id);
            if (bind == null) continue;
            var bytes = (byte*)bind;
            ChatGui.Print($"  {id,-16} {KeyMap.Name(bytes[PrimaryKeyOffset])} / {KeyMap.Name(bytes[AltKeyOffset])}");
        }
    }

    private void Save() => PluginInterface.SavePluginConfig(this.configuration);
}
