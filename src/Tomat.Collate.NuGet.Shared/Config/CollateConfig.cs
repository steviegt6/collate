using System.Collections.Generic;
using Newtonsoft.Json;

namespace Tomat.Collate.NuGet.Shared.Config;

/// <summary>
///     A Collate configuration file.
/// </summary>
public sealed class CollateConfig {
    /// <summary>
    ///     Plugin settings. Keys are plugin assembly names (case insensitive),
    ///     values are a dictionary of plugin settings (key-value pairs).
    /// </summary>
    [JsonProperty("plugins")]
    public Dictionary<string, Dictionary<string, object?>>? Plugins { get; set; }
}
