using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FreeTheLights.Updates
{
    /// <summary>A failed update, with a message for the user.</summary>
    public sealed class UpdateException : Exception
    {
        public UpdateException(string message, Exception inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>Runs a PowerShell script with administrator rights (a UAC prompt).</summary>
    public interface IElevatedRunner
    {
        /// <summary>Runs <paramref name="script"/> elevated and waits for it; false if the user declined or it failed.</summary>
        bool Run(string script);
    }

    /// <summary>
    /// Verifies a downloaded plugin DLL and puts it in place of the running one. Windows lets a loaded DLL be renamed but
    /// not overwritten, so the running DLL is renamed to "FreeTheLights.dll.old" and the new one copied in its place; SimHub
    /// loads it at its next start, and <see cref="Cleanup"/> then deletes the old one. Where the plugin folder needs
    /// administrator rights (SimHub under Program Files), the same steps run through <see cref="IElevatedRunner"/>.
    /// </summary>
    public sealed class UpdateInstaller
    {
        private static readonly Regex Sha256Pattern = new Regex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

        private readonly IElevatedRunner elevated;

        /// <param name="pluginPath">The running plugin DLL (FreeTheLights.dll in SimHub's folder).</param>
        /// <param name="elevated">Runs the install with administrator rights when the folder needs them.</param>
        public UpdateInstaller(string pluginPath, IElevatedRunner elevated)
        {
            PluginPath = Path.GetFullPath(pluginPath);
            this.elevated = elevated;
        }

        public string PluginPath { get; }

        private string PluginFolder => Path.GetDirectoryName(PluginPath);

        private string PluginName => Path.GetFileName(PluginPath);

        /// <summary>The SHA-256 of a file, as lower-case hex.</summary>
        public static string Sha256OfFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
            }
        }

        /// <summary>The hash in a checksum file ("&lt;hash&gt;  FreeTheLights.dll", as sha256sum writes it), lower case; null if there is none.</summary>
        public static string ParseChecksum(string text)
        {
            var first = (text ?? string.Empty).Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return first != null && Sha256Pattern.IsMatch(first) ? first.ToLowerInvariant() : null;
        }

        /// <summary>
        /// Checks that <paramref name="file"/> has the SHA-256 <paramref name="expectedSha256"/> and is the FreeTheLights
        /// plugin of version <paramref name="expectedVersion"/>; throws <see cref="UpdateException"/> otherwise.
        /// </summary>
        public static void Verify(string file, string expectedSha256, Version expectedVersion)
        {
            if (expectedSha256 == null || !string.Equals(Sha256OfFile(file), expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("The download is damaged. Try again later.");
            }

            AssemblyName name;
            try
            {
                // Reads the metadata only; nothing is loaded.
                name = AssemblyName.GetAssemblyName(file);
            }
            catch (Exception ex)
            {
                throw new UpdateException("The download isn't a FreeTheLights plugin.", ex);
            }

            if (!string.Equals(name.Name, "FreeTheLights", StringComparison.Ordinal))
            {
                throw new UpdateException("The download isn't a FreeTheLights plugin.");
            }

            if (name.Version == null || ReleaseInfo.Normalize(name.Version) != ReleaseInfo.Normalize(expectedVersion))
            {
                throw new UpdateException($"The download is version {name.Version}, not {expectedVersion}.");
            }
        }

        /// <summary>
        /// Puts <paramref name="newDll"/> (verified) in place of the plugin. Without write access to the plugin folder, it
        /// asks for administrator rights. Throws <see cref="UpdateException"/> if the plugin couldn't be replaced; the old
        /// plugin then stays in place.
        /// </summary>
        public void Install(string newDll)
        {
            var expected = Sha256OfFile(newDll);
            try
            {
                Cleanup();
                var old = NextOldPath();
                File.Move(PluginPath, old);
                try
                {
                    File.Copy(newDll, PluginPath);
                }
                catch
                {
                    File.Move(old, PluginPath);
                    throw;
                }

                DeleteStaleSymbols();
            }
            catch (UnauthorizedAccessException)
            {
                if (elevated == null || !elevated.Run(ElevatedScript(newDll, NextOldPath())))
                {
                    throw new UpdateException("Updating needs administrator rights. Allow it, or download the update from GitHub.");
                }
            }
            catch (IOException ex)
            {
                throw new UpdateException("The plugin file couldn't be replaced: " + ex.Message, ex);
            }

            if (!File.Exists(PluginPath) || Sha256OfFile(PluginPath) != expected)
            {
                throw new UpdateException("The plugin file couldn't be replaced.");
            }
        }

        /// <summary>
        /// Deletes the plugin files an earlier update left behind ("FreeTheLights.dll.old"). Files that are still loaded or
        /// need administrator rights stay; returns how many are left.
        /// </summary>
        public int Cleanup()
        {
            var left = 0;
            foreach (var file in OldFiles())
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    left++;
                }
            }

            return left;
        }

        /// <summary>The PowerShell script that installs <paramref name="newDll"/> with administrator rights (same steps as <see cref="Install"/>).</summary>
        public string ElevatedScript(string newDll, string oldPath)
        {
            string Quote(string s) => "'" + s.Replace("'", "''") + "'";
            var pdb = Path.ChangeExtension(PluginPath, ".pdb");
            return string.Join("\n",
                "$ErrorActionPreference = 'Stop'",
                $"$dll = {Quote(PluginPath)}",
                $"$new = {Quote(Path.GetFullPath(newDll))}",
                $"$old = {Quote(oldPath)}",
                $"Get-ChildItem -LiteralPath {Quote(PluginFolder)} -Filter {Quote(PluginName + "*.old")} | Remove-Item -Force -ErrorAction SilentlyContinue",
                "Move-Item -LiteralPath $dll -Destination $old",
                "try { Copy-Item -LiteralPath $new -Destination $dll } catch { Move-Item -LiteralPath $old -Destination $dll; exit 1 }",
                $"Remove-Item -LiteralPath {Quote(pdb)} -Force -ErrorAction SilentlyContinue",
                "exit 0");
        }

        private string[] OldFiles()
        {
            try
            {
                return Directory.GetFiles(PluginFolder, PluginName + "*.old")
                    .Where(f => f.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new string[0];
            }
        }

        /// <summary>A free name for the renamed plugin: "FreeTheLights.dll.old", or "FreeTheLights.dll.1.old" ... while older ones are still loaded.</summary>
        private string NextOldPath()
        {
            var path = PluginPath + ".old";
            for (var i = 1; File.Exists(path); i++) path = $"{PluginPath}.{i}.old";
            return path;
        }

        /// <summary>A debug symbols file of the old plugin would not match the new one.</summary>
        private void DeleteStaleSymbols()
        {
            try
            {
                File.Delete(Path.ChangeExtension(PluginPath, ".pdb"));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Harmless: it only makes stack traces less precise.
            }
        }
    }

    /// <summary>Runs PowerShell elevated through UAC ("runas"), without a window.</summary>
    public sealed class PowerShellElevatedRunner : IElevatedRunner
    {
        private const int ErrorCancelled = 1223;

        public bool Run(string script)
        {
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            try
            {
                using (var process = Process.Start(info))
                {
                    if (process == null) return false;
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
        }
    }
}
