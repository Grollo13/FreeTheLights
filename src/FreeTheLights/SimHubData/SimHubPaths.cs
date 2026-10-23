using System;
using System.IO;

namespace FreeTheLights.SimHubData
{
    /// <summary>Locates SimHub's install folder and the files FreeTheLights reads from it.</summary>
    public static class SimHubPaths
    {
        private const string DefaultFolder = @"C:\Program Files (x86)\SimHub\";

        private static string folder;

        /// <summary>
        /// SimHub's install folder: the folder of the running SimHub, else SIMHUB_INSTALL_PATH, else the default
        /// install location. Ends with a path separator.
        /// </summary>
        public static string Folder
        {
            get
            {
                if (folder != null) return folder;
                var candidates = new[]
                {
                    AppDomain.CurrentDomain.BaseDirectory,
                    Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH"),
                    DefaultFolder,
                };
                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    var path = candidate.EndsWith("\\", StringComparison.Ordinal) ? candidate : candidate + "\\";
                    if (File.Exists(Path.Combine(path, "SimHubWPF.exe")))
                    {
                        folder = path;
                        return folder;
                    }
                }

                folder = DefaultFolder;
                return folder;
            }
        }

        /// <summary>Absolute path of a file given relative to the SimHub folder, or null for an empty name.</summary>
        public static string Resolve(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            return Path.IsPathRooted(relativePath) ? relativePath : Path.Combine(Folder, relativePath);
        }
    }
}
