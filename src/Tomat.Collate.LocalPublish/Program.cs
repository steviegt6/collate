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
using System.Diagnostics;
using System.IO;
using System.Linq;
using NuGet.Configuration;

namespace Tomat.Collate.LocalPublish;

internal static class Program {
    // constants
    private const string root_dir = "src";
    private const string project_extension = ".csproj";

    // settings
    private const string configuration = "Release";
    private const string nuget_repo_name = "CollateLocalSources";

    // projects
    private const string tomat_collate = "Tomat.Collate";
    private const string tomat_collate_nuget = "Tomat.Collate.NuGet";
    private const string tomat_collate_localpublish = "Tomat.Collate.LocalPublish";

    // frameworks
    private const string netstandard20 = "netstandard2.0";
    private const string net60 = "net6.0";
    private const string net70 = "net7.0";

    public static void Main() {
        // Normalize the working directory to produce reliable results no matter
        // where this project is run from. Useful for both running from the
        // publish.sh script and running from the IDE.
        var cwd = NormalizeWorkingDirectory(Directory.GetCurrentDirectory());
        var srcDir = Path.Combine(cwd, root_dir);

        Console.WriteLine("cwd: " + cwd);
        Console.WriteLine("srcDir: " + srcDir);

        FindOrCreateLocalNuGetRepository(cwd, nuget_repo_name);

        var nugetCache = GetNuGetCache();
        DeleteNuGetCaches(
            nugetCache,
            tomat_collate_nuget
        );

        DeletePackages(
            srcDir,
            tomat_collate_nuget
        );

        BuildProjects(
            srcDir,
            tomat_collate,
            tomat_collate_nuget
        );

        PublishPackages(
            srcDir,
            nuget_repo_name,
            tomat_collate_nuget
        );
    }

    private static string NormalizeWorkingDirectory(string cwd) {
        if (!Directory.Exists(cwd))
            throw new DirectoryNotFoundException($"Directory '{cwd}' not found!");

        if (File.Exists(cwd))
            throw new DirectoryNotFoundException($"File '{cwd}' is not a directory!");

        var dirName = Path.GetFileName(cwd);

        string up(int levels) {
            var result = cwd;
            for (var i = 0; i < levels; i++)
                result = Path.Combine(result, ".."); // Path.GetDirectoryName?
            return result;
        }

        switch (dirName) {
            // We can assume this is the root directory since it matches the
            // repository name.
            // This doesn't include things like `collate-master` when a user
            // downloads and extracts an archive; unpacked archives will usually
            // have two directories with the same name anyway, so detecting that
            // is neither worth the effort nor an intended way of obtaining a
            // copy of this code.
            case "collate":
                return cwd;

            case root_dir:
                return up(1);

            case tomat_collate:
            case tomat_collate_nuget:
            case tomat_collate_localpublish:
                return up(2);

            case netstandard20:
            case net60:
            case net70:
                return up(5);

            default:
                var files = Directory.GetFiles(cwd);

                // Assume that this is the root[1] directory if publish.sh is
                // present. I would also check for `./git/`, but there's no
                // guarantee that the user would have cloned the repository[2]
                // instead of just downloading an archive, and there's no reason
                // to enforce[3] it either.
                // [1]: I use the word "root" sort of strangely in this project,
                //      but the `root_dir` const refers to the root of the
                //      solution and various projects. "root" here refers to the
                //      repository root.
                // [2]: I really do wish people would just clone the repository,
                //      but such things are out of my control.
                // [3]: I explicitly don't bother to check for a similar case
                //      earlier in this switch case, but this is less work.

                if (files.Contains("publish.sh"))
                    return cwd;

                throw new DirectoryNotFoundException($"Directory '{cwd}' was not detected as the root directory and was not able to be predictably traversed!");
        }
    }

    private static void FindOrCreateLocalNuGetRepository(string cwd, string repoName) {
        var settings = Settings.LoadDefaultSettings(null);
        var sourceProvider = new PackageSourceProvider(settings);
        var nugetRepo = sourceProvider.LoadPackageSources().FirstOrDefault(x => x.Name == repoName);

        if (nugetRepo is null) {
            Console.WriteLine($"NuGet repository '{repoName}' does not exist. Creating...");

            var repoDir = Path.Combine(cwd, "nuget");
            if (File.Exists(repoDir))
                throw new IOException($"File '{repoDir}' is not a directory!");

            if (!Directory.Exists(repoDir))
                Directory.CreateDirectory(repoDir);

            // If repoDir ends with an unescaped '\' then the command will fail.
            // If repoDir ends with an escaped '\' then we're good.
            // This only applies to Windows; I doubt we'll encounter this issue
            // on *nix.
            string getSanitizedRepoDir() {
                if (repoDir is null)
                    throw new InvalidOperationException();

                if (repoDir.EndsWith("\\\\"))
                    return repoDir;

                if (repoDir.EndsWith("\\"))
                    return repoDir + "\\";

                return repoDir;
            }

            // This is more reliable than just interfacing with the
            // NuGet.Protocol API directly. And by that, I mean that I broke
            // something and it caused a catastrophic issue with NuGet that I am
            // too scared to accidentally unleash on other people, as it
            // transcends past this project and into every other project...
            // Just brilliant.
            RunCommand("dotnet", $"nuget add source \"{getSanitizedRepoDir()}\" --name \"{repoName}\"");
        }
        else if (!nugetRepo.IsEnabled) {
            Console.WriteLine($"NuGet repository '{repoName}' exists but is not enabled. Enabling...");
            sourceProvider.EnablePackageSource(repoName);
        }
        else {
            Console.WriteLine($"NuGet repository '{repoName}' exists and is already enabled!");
        }
    }

    private static string GetNuGetCache() {
        // TODO: Can we auto-detect this with NuGet.Protocol or similar?

        string unix() {
            var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrEmpty(xdgCacheHome))
                return Path.Combine(xdgCacheHome, "NuGetPackages");

            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
                return Path.Combine(home, ".cache", "NuGetPackages");

            throw new PlatformNotSupportedException("Unsupported platform; contribute support for your OS' paths!");
        }

        return Environment.OSVersion.Platform switch {
            PlatformID.Win32NT => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages"),
            PlatformID.MacOSX => unix(),
            PlatformID.Unix => unix(),
            _ => throw new PlatformNotSupportedException("Unsupported platform; contribute support for your OS' paths!")
        };
    }

    private static void DeleteNuGetCaches(string cacheDir, params string[] packages) {
        packages = packages.Select(x => x.ToLowerInvariant()).ToArray();

        var cache = new DirectoryInfo(cacheDir);
        var pkgDirs = cache.GetDirectories().Where(x => packages.Contains(x.Name));

        foreach (var pkgDir in pkgDirs) {
            Console.WriteLine($"Deleting '{pkgDir.FullName}'...");
            pkgDir.Delete(true);
        }
    }

    private static void DeletePackages(string srcDir, params string[] projectNames) {
        foreach (var projectName in projectNames) {
            var dir = new DirectoryInfo(Path.Combine(srcDir, projectName));

            foreach (var nupkg in dir.GetFiles("*.nupkg", SearchOption.AllDirectories)) {
                Console.WriteLine($"Deleting '{nupkg.FullName}'...");
                nupkg.Delete();
            }
        }
    }

    private static void BuildProjects(string srcDir, params string[] projectFiles) {
        foreach (var project in projectFiles) {
            var projectFile = Path.Combine(srcDir, project, project + project_extension);
            Console.WriteLine($"Building '{projectFile}'...");
            RunCommand("dotnet", $"build {projectFile} -c {configuration}");
        }
    }

    private static void PublishPackages(string srcDir, string repoName, params string[] projectNames) {
        foreach (var projectName in projectNames) {
            var dir = new DirectoryInfo(Path.Combine(srcDir, projectName));

            foreach (var nupkg in dir.GetFiles("*.nupkg", SearchOption.AllDirectories)) {
                Console.WriteLine($"Publishing '{nupkg}'...");
                RunCommand("dotnet", $"nuget push {nupkg} -s {repoName}");
            }
        }
    }

    private static void RunCommand(string command, string args) {
        var startInfo = new ProcessStartInfo(command, args) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var process = Process.Start(startInfo);
        process?.WaitForExit();
    }
}
