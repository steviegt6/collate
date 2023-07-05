/* Copyright (C) 2023 Tomat & Contributors
 * 
 * Licensed under the GNU Lesser General Public License, version 2.1; you may
 * not use this file except in compliance with the License.
 * 
 * This program is distributed in the hope that it will be useful, but WITHOUT
 * ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 * FITNESS FOR A PARTICULAR PURPOSE.  See the GNU Lesser General Public License
 * for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Tomat.Collate.NuGet.Shared.Config;

namespace Tomat.Collate.NuGet.Shared;

/// <summary>
///     The content of a <see cref="CollateTask"/>.
/// </summary>
public sealed class CollateContext {
    /// <summary>
    ///     Whether the task should catch exceptions.
    /// </summary>
    public bool CatchOnException { get; set; } = true;

    public CollateConfig Config { get; set; } = new();
}

public abstract class CollateTask : Task {
    [Required]
    public string ConfigPath { get; set; }

    public override bool Execute() {
        var failed = false;
        var ctx = new CollateContext {
            Config = ResolveConfig(ref failed),
        };

        if (failed)
            return false;

        try {
            return Execute(ctx);
        }
        catch (Exception e) {
            if (!ctx.CatchOnException)
                throw;

            Log.LogErrorFromException(e);
            return false;
        }
    }

    /// <summary>
    ///     Executes the task.
    /// </summary>
    /// <param name="ctx">The context of the task.</param>
    /// <returns>
    ///     <see langword="true"/> if the task succeeded,
    ///     <see langword="false"/> otherwise.
    /// </returns>
    protected abstract bool Execute(CollateContext ctx);

    private CollateConfig ResolveConfig(ref bool failed) {
        if (string.IsNullOrEmpty(ConfigPath)) {
            Log.LogError("Collate config path not specified; specify it with the 'CollateConfigPath' property.");
            failed = true;
            return new CollateConfig();
        }

        if (!File.Exists(ConfigPath)) {
            Log.LogError($"Collate config path '{ConfigPath}' does not exist.");
            failed = true;
            return new CollateConfig();
        }

        try {
            var cfg = JsonConvert.DeserializeObject<CollateConfig>(File.ReadAllText(ConfigPath));
            if (cfg is not null)
                return cfg;

            Log.LogError($"Collate config path '{ConfigPath}' is invalid -- returned null.");
            failed = true;
            return new CollateConfig();
        }
        catch (Exception e) {
            Log.LogErrorFromException(e);
            failed = true;
            return new CollateConfig();
        }
    }
}
