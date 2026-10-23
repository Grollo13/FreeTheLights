using System;
using System.IO;

namespace FreeTheLights.Core
{
    /// <summary>Names of backup files, which must never replace an earlier backup.</summary>
    public static class BackupFile
    {
        /// <summary>
        /// "&lt;name&gt;-&lt;yyyyMMdd-HHmmss&gt;.json" in <paramref name="folder"/>, with "-2", "-3", ... appended when
        /// that file exists: several backups can be taken within one second (one per channel connected).
        /// </summary>
        public static string NewPath(string folder, string name, DateTime time)
        {
            var stem = $"{name}-{time:yyyyMMdd-HHmmss}";
            var path = Path.Combine(folder, stem + ".json");
            for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem}-{n}.json");
            return path;
        }
    }
}
