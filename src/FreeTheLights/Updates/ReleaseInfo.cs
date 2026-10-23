using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.Updates
{
    /// <summary>
    /// A published release of the plugin on GitHub (see tools/release.sh): its version, release page and the URLs of the
    /// plugin DLL and its SHA-256. Only stable releases of <see cref="Repository"/> are accepted.
    /// </summary>
    public sealed class ReleaseInfo
    {
        /// <summary>The only repository whose releases are offered.</summary>
        public const string Repository = "Grollo13/FreeTheLights";

        public const string DllAsset = "FreeTheLights.dll";

        public const string ChecksumAsset = "FreeTheLights.dll.sha256";

        /// <summary>Where the release assets of <see cref="Repository"/> are downloaded from.</summary>
        public const string DownloadUrlPrefix = "https://github.com/" + Repository + "/releases/download/";

        /// <summary>The releases of <see cref="Repository"/> on GitHub.</summary>
        public const string ReleasesPageUrl = "https://github.com/" + Repository + "/releases";

        private static readonly Regex TagPattern = new Regex(@"^v?(\d{1,5})\.(\d{1,5})\.(\d{1,5})$", RegexOptions.CultureInvariant);

        /// <summary>The release's tag, e.g. "v0.2.0".</summary>
        public string Tag { get; set; }

        /// <summary>The release page on GitHub, with the release notes.</summary>
        public string PageUrl { get; set; }

        public DateTime? PublishedUtc { get; set; }

        public string DllUrl { get; set; }

        public string ChecksumUrl { get; set; }

        /// <summary>The version from <see cref="Tag"/>, or null if the tag isn't one.</summary>
        [JsonIgnore]
        public Version Version => TryParseTag(Tag, out var version) ? version : null;

        /// <summary>The page to open for the release notes: <see cref="PageUrl"/> if it is a release page of this repository, else the list of releases.</summary>
        [JsonIgnore]
        public string NotesUrl => PageUrl != null && PageUrl.StartsWith(ReleasesPageUrl + "/", StringComparison.Ordinal)
                                  && Uri.IsWellFormedUriString(PageUrl, UriKind.Absolute)
            ? PageUrl
            : ReleasesPageUrl;

        /// <summary>Whether the release has what the update needs: a version, the DLL and its SHA-256, from this repository.</summary>
        [JsonIgnore]
        public bool CanInstall => Version != null && IsTrustedAssetUrl(DllUrl) && IsTrustedAssetUrl(ChecksumUrl);

        /// <summary>Whether this release is newer than <paramref name="installed"/> (an assembly version, X.Y.Z.0).</summary>
        public bool IsNewerThan(Version installed)
        {
            var version = Version;
            return version != null && (installed == null || version > Normalize(installed));
        }

        /// <summary>X.Y.Z of a version, so that an assembly version X.Y.Z.0 equals the release X.Y.Z.</summary>
        public static Version Normalize(Version version) => new Version(version.Major, version.Minor, Math.Max(version.Build, 0));

        /// <summary>Parses a release tag "vX.Y.Z" or "X.Y.Z". Pre-release tags ("v1.0.0-beta.1") are rejected.</summary>
        public static bool TryParseTag(string tag, out Version version)
        {
            version = null;
            var match = TagPattern.Match(tag ?? string.Empty);
            if (!match.Success) return false;
            version = new Version(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>Whether <paramref name="url"/> is a release download of <see cref="Repository"/>, over https.</summary>
        public static bool IsTrustedAssetUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !url.StartsWith(DownloadUrlPrefix, StringComparison.Ordinal)) return false;
            // Uri resolves "../" segments, so check the prefix of the resolved URL too.
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com"
                   && uri.AbsoluteUri.StartsWith(DownloadUrlPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The release described by a release object of GitHub's REST API (GET /repos/{repo}/releases/latest), or null if
        /// it isn't a published, stable release with a version tag.
        /// </summary>
        public static ReleaseInfo FromGitHubJson(JObject json)
        {
            if (json == null) return null;
            if ((bool?)json["draft"] == true || (bool?)json["prerelease"] == true) return null;

            var tag = (string)json["tag_name"];
            if (!TryParseTag(tag, out _)) return null;

            var assets = json["assets"] as JArray ?? new JArray();
            string AssetUrl(string name) => assets.OfType<JObject>()
                .Where(a => string.Equals((string)a["name"], name, StringComparison.OrdinalIgnoreCase))
                .Select(a => (string)a["browser_download_url"])
                .FirstOrDefault();

            var published = json["published_at"];
            return new ReleaseInfo
            {
                Tag = tag,
                PageUrl = (string)json["html_url"],
                PublishedUtc = published != null && published.Type == JTokenType.Date ? ((DateTime)published).ToUniversalTime() : (DateTime?)null,
                DllUrl = AssetUrl(DllAsset),
                ChecksumUrl = AssetUrl(ChecksumAsset),
            };
        }
    }
}
