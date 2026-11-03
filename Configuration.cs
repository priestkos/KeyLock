using Dalamud.Configuration;
using Newtonsoft.Json;

namespace KeyLock;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>
    /// Pre-swap 11-byte Keybind of every input the swap touched, keyed by InputId, per character ContentId.
    /// Keybinds are per character; a character is swapped while it has an entry. Saved before the write, so
    /// a restore still works after a client restart even if the game persisted the swap.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<ulong, Dictionary<int, byte[]>> Characters { get; set; } = [];

    /// <summary>Legacy global swap flag from before per-character state; read only to migrate.</summary>
    public bool Swapped { get; set; }

    /// <summary>
    /// Legacy global snapshot from before per-character state. Adopted only by a character whose live binds
    /// still equal the swapped layout, never by whoever logs in first.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<int, byte[]> OriginalBinds { get; set; } = [];
}
