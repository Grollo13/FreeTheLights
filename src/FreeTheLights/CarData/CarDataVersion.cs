using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FreeTheLights.CarData
{
    /// <summary>
    /// Which lovely-car-data is in use: the branch read from (format versions are branches, "main" holds the
    /// latest data), the newest file format listed in the changelog, and the last commit that changed the data.
    /// </summary>
    public class CarDataVersion
    {
        private static readonly Regex RawGitHubUrl = new Regex(@"^https?://raw\.githubusercontent\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/(?<branch>.+?)/data/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ChangelogVersion = new Regex(@"^\s*#{1,6}\s*(?<version>v?\d+(\.\d+)*)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public string Branch { get; set; }

        /// <summary>Newest file format version from changelog.md, e.g. "v2.0.0".</summary>
        public string FormatVersion { get; set; }

        public string CommitSha { get; set; }

        public DateTime? CommitDateUtc { get; set; }

        /// <summary>When this information was fetched from GitHub.</summary>
        public DateTime CheckedUtc { get; set; }

        /// <summary>True when the information comes from the disk cache because GitHub could not be reached.</summary>
        [JsonIgnore]
        public bool Offline { get; set; }

        [JsonIgnore]
        public string ShortSha => string.IsNullOrEmpty(CommitSha) ? null : CommitSha.Substring(0, Math.Min(7, CommitSha.Length));

        [JsonIgnore]
        public string Display
        {
            get
            {
                var text = "lovely-car-data " + (FormatVersion ?? "(format unknown)");
                if (!string.IsNullOrEmpty(Branch)) text += " · " + Branch;
                if (ShortSha != null) text += " @ " + ShortSha;
                if (CommitDateUtc.HasValue) text += " (" + CommitDateUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")";
                if (Offline) text += " - offline, checked " + CheckedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                return text;
            }
        }

        /// <summary>Splits a raw.githubusercontent.com ".../{owner}/{repo}/{branch}/data/" URL; false for other sources.</summary>
        public static bool TryParseBaseUrl(string baseUrl, out string owner, out string repo, out string branch)
        {
            var match = RawGitHubUrl.Match(baseUrl ?? string.Empty);
            owner = match.Success ? match.Groups["owner"].Value : null;
            repo = match.Success ? match.Groups["repo"].Value : null;
            branch = match.Success ? match.Groups["branch"].Value : null;
            return match.Success;
        }

        /// <summary>First version heading of the changelog ("#### v2.0.0" → "v2.0.0"), or null.</summary>
        public static string ParseFormatVersion(string changelog)
        {
            var match = ChangelogVersion.Match(changelog ?? string.Empty);
            if (!match.Success) return null;
            var version = match.Groups["version"].Value;
            return version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : "v" + version;
        }
    }
}
