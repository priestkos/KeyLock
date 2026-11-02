using Dalamud.Configuration;
using Newtonsoft.Json;

namespace KeyLock;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>True while the movement keys are swapped, so the state survives a plugin reload.</summary>
    public bool Swapped { get; set; }

    /// <summary>
    /// The 11-byte Keybind struct of every input the swap touched, keyed by InputId. Saved before
    /// the write, so a restore still works after a client restart even if the game persisted the swap.
    /// </summary>
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public Dictionary<int, byte[]> OriginalBinds { get; set; } = [];
}
