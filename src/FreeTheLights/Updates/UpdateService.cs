using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.Updates
{
    public enum UpdateStatus
    {
        /// <summary>Not checked yet (or checking is turned off).</summary>
        NotChecked,
        Checking,
        UpToDate,
        Available,
        CheckFailed,

        /// <summary>Downloading and installing <see cref="UpdateService.Latest"/>.</summary>
        Installing,

        /// <summary>Installed; SimHub loads it at its next start.</summary>
        Installed,

        /// <summary>Installing failed (<see cref="UpdateService.Error"/>); the old plugin is still in place and it can be tried again.</summary>
        InstallFailed,
    }

    /// <summary>
    /// Checks GitHub for a newer release of the plugin (<see cref="ReleaseInfo"/>) and installs it. The check runs on a
    /// background task and is cached on disk, so GitHub is asked at most once per <see cref="DefaultMaxAge"/> unless forced;
    /// offline, the cached answer is used. <see cref="Install"/> downloads the release, verifies it and hands it to the
    /// <see cref="UpdateInstaller"/>. The state is read by the UI thread; all members are thread-safe.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        public const string LatestReleaseUrl = "https://api.github.com/repos/" + ReleaseInfo.Repository + "/releases/latest";

        public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(24);

        /// <summary>Larger downloads are refused (the plugin DLL is about 1 MB).</summary>
        public const long MaxDownloadBytes = 50L * 1024 * 1024;

        private const string CacheFile = "latest.json";

        private readonly string directory;
        private readonly UpdateInstaller installer;
        private readonly Action<string> log;
        private readonly TimeSpan maxAge;
        private readonly HttpClient http;
        private readonly object stateLock = new object();
        private int checkRunning;

        private UpdateStatus status = UpdateStatus.NotChecked;
        private ReleaseInfo latest;
        private string error;
        private DateTime? checkedUtc;
        private bool offline;

        /// <param name="directory">Where the check is cached and the update is downloaded to.</param>
        /// <param name="installed">The running plugin's version.</param>
        /// <param name="installer">Puts a downloaded release in place; null = updates can't be installed.</param>
        /// <param name="log">Log callback.</param>
        /// <param name="handler">HTTP handler (tests); null = the default.</param>
        /// <param name="maxAge">How long a check is reused; null = <see cref="DefaultMaxAge"/>.</param>
        public UpdateService(string directory, Version installed, UpdateInstaller installer, Action<string> log, HttpMessageHandler handler = null, TimeSpan? maxAge = null)
        {
            this.directory = directory;
            Installed = installed;
            this.installer = installer;
            this.log = log ?? (_ => { });
            this.maxAge = maxAge ?? DefaultMaxAge;

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = handler == null ? new HttpClient() : new HttpClient(handler);
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FreeTheLights-SimHub-Plugin");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>The running plugin's version.</summary>
        public Version Installed { get; }

        public UpdateStatus Status
        {
            get { lock (stateLock) return status; }
        }

        /// <summary>The latest release, or null if there is none or it hasn't been checked yet.</summary>
        public ReleaseInfo Latest
        {
            get { lock (stateLock) return latest; }
        }

        /// <summary>What went wrong in the last check or install, for a tooltip; null if nothing did.</summary>
        public string Error
        {
            get { lock (stateLock) return error; }
        }

        /// <summary>When GitHub was last asked successfully.</summary>
        public DateTime? CheckedUtc
        {
            get { lock (stateLock) return checkedUtc; }
        }

        /// <summary>Whether the last check failed and the answer is from the cache.</summary>
        public bool Offline
        {
            get { lock (stateLock) return offline; }
        }

        /// <summary>Asks GitHub for the latest release in the background (at most once per max age unless forced).</summary>
        public void Check(bool force = false)
        {
            lock (stateLock)
            {
                // Once an update is being installed or was installed, the answer can't change before SimHub restarts.
                if (status == UpdateStatus.Installing || status == UpdateStatus.Installed) return;
                if (Interlocked.Exchange(ref checkRunning, 1) == 1) return;
                status = UpdateStatus.Checking;
            }

            Task.Run(async () =>
            {
                try
                {
                    await CheckAsync(force).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log($"FreeTheLights: update check failed: {ex.Message}");
                    SetChecked(null, null, false, ex.Message, UpdateStatus.CheckFailed);
                }
                finally
                {
                    Interlocked.Exchange(ref checkRunning, 0);
                }
            });
        }

        private async Task CheckAsync(bool force)
        {
            var cachePath = Path.Combine(directory, CacheFile);
            var cached = ReadCache(cachePath);
            if (!force && cached != null && DateTime.UtcNow - cached.CheckedUtc < maxAge && DateTime.UtcNow >= cached.CheckedUtc)
            {
                SetChecked(cached.Release, cached.CheckedUtc, false, null, StatusOf(cached.Release));
                return;
            }

            try
            {
                ReleaseInfo release;
                using (var response = await http.GetAsync(LatestReleaseUrl).ConfigureAwait(false))
                {
                    // 404: no release published yet.
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        release = null;
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        release = ReleaseInfo.FromGitHubJson(JObject.Parse(json));
                    }
                }

                var now = DateTime.UtcNow;
                WriteCache(cachePath, new CachedCheck { CheckedUtc = now, Release = release });
                SetChecked(release, now, false, null, StatusOf(release));
            }
            catch (Exception ex) when (cached != null)
            {
                log($"FreeTheLights: update check failed, using the last answer: {ex.Message}");
                SetChecked(cached.Release, cached.CheckedUtc, true, ex.Message, StatusOf(cached.Release));
            }
        }

        private UpdateStatus StatusOf(ReleaseInfo release) => release != null && release.IsNewerThan(Installed) ? UpdateStatus.Available : UpdateStatus.UpToDate;

        private void SetChecked(ReleaseInfo release, DateTime? time, bool isOffline, string message, UpdateStatus newStatus)
        {
            lock (stateLock)
            {
                if (newStatus != UpdateStatus.CheckFailed)
                {
                    latest = release;
                    checkedUtc = time;
                }

                offline = isOffline;
                error = message;
                status = newStatus;
            }
        }

        private CachedCheck ReadCache(string path)
        {
            try
            {
                return File.Exists(path) ? JsonConvert.DeserializeObject<CachedCheck>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                log($"FreeTheLights: ignoring the cached update check: {ex.Message}");
                return null;
            }
        }

        private void WriteCache(string path, CachedCheck check)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(check, Formatting.Indented));
                if (File.Exists(path)) File.Replace(tempPath, path, null);
                else File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                log($"FreeTheLights: could not cache the update check: {ex.Message}");
            }
        }

        /// <summary>Whether <see cref="Install"/> would start: a newer release that can be installed is known, and no install is running.</summary>
        public bool CanInstall
        {
            get
            {
                lock (stateLock) return CanInstallLocked();
            }
        }

        private bool CanInstallLocked() =>
            installer != null && latest != null && latest.CanInstall && latest.IsNewerThan(Installed)
            && (status == UpdateStatus.Available || status == UpdateStatus.InstallFailed);

        /// <summary>
        /// Downloads the latest release, verifies it and installs it, in the background. The status goes to
        /// <see cref="UpdateStatus.Installed"/> (restart SimHub to load it) or <see cref="UpdateStatus.InstallFailed"/>.
        /// Does nothing unless <see cref="CanInstall"/>.
        /// </summary>
        public void Install()
        {
            ReleaseInfo release;
            lock (stateLock)
            {
                if (!CanInstallLocked()) return;
                release = latest;
                status = UpdateStatus.Installing;
                error = null;
            }

            Task.Run(async () =>
            {
                string failure;
                try
                {
                    await InstallAsync(release).ConfigureAwait(false);
                    log($"FreeTheLights: installed version {release.Version}, active after SimHub restarts");
                    SetInstallResult(UpdateStatus.Installed, null);
                    return;
                }
                catch (UpdateException ex)
                {
                    failure = ex.Message;
                }
                catch (HttpRequestException ex)
                {
                    failure = "The update couldn't be downloaded. Check your internet connection and try again.";
                    log($"FreeTheLights: downloading the update failed: {ex.Message}");
                }
                catch (TaskCanceledException)
                {
                    failure = "The download took too long. Try again.";
                }
                catch (Exception ex)
                {
                    failure = "The update failed: " + ex.Message;
                }

                log($"FreeTheLights: update to {release.Version} failed: {failure}");
                SetInstallResult(UpdateStatus.InstallFailed, failure);
            });
        }

        private async Task InstallAsync(ReleaseInfo release)
        {
            Directory.CreateDirectory(directory);

            var checksumFile = Path.Combine(directory, ReleaseInfo.ChecksumAsset);
            await DownloadAsync(release.ChecksumUrl, checksumFile, 4096).ConfigureAwait(false);
            var checksum = UpdateInstaller.ParseChecksum(File.ReadAllText(checksumFile));
            if (checksum == null) throw new UpdateException("The download is damaged. Try again later.");

            var dll = Path.Combine(directory, $"FreeTheLights-{release.Version}.dll");
            await DownloadAsync(release.DllUrl, dll, MaxDownloadBytes).ConfigureAwait(false);
            UpdateInstaller.Verify(dll, checksum, release.Version);
            installer.Install(dll);
            DeleteDownloads();
        }

        /// <summary>Downloads <paramref name="url"/> (a trusted release asset) to <paramref name="path"/>, refusing more than <paramref name="maxBytes"/>.</summary>
        private async Task DownloadAsync(string url, string path, long maxBytes)
        {
            if (!ReleaseInfo.IsTrustedAssetUrl(url)) throw new UpdateException("The update isn't from FreeTheLights' releases.");

            using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes) throw new UpdateException("The download is too large.");

                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = File.Create(path))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > maxBytes) throw new UpdateException("The download is too large.");
                        await target.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                    }
                }
            }
        }

        private void SetInstallResult(UpdateStatus newStatus, string message)
        {
            lock (stateLock)
            {
                status = newStatus;
                error = message;
            }
        }

        /// <summary>
        /// Deletes what an earlier update left behind: the old plugin DLL (once SimHub no longer has it loaded) and the
        /// downloads. Call it at start-up. Never throws.
        /// </summary>
        public void CleanupAfterUpdate()
        {
            try
            {
                var left = installer?.Cleanup() ?? 0;
                if (left > 0) log($"FreeTheLights: {left} old plugin file(s) from an update could not be deleted yet");
                DeleteDownloads();
            }
            catch (Exception ex)
            {
                log($"FreeTheLights: cleaning up after an update failed: {ex.Message}");
            }
        }

        private void DeleteDownloads()
        {
            if (!Directory.Exists(directory)) return;
            foreach (var pattern in new[] { "*.dll", "*.sha256" })
            {
                foreach (var file in Directory.GetFiles(directory, pattern))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // Deleted at the next start.
                    }
                }
            }
        }

        public void Dispose()
        {
            http.Dispose();
        }

        private sealed class CachedCheck
        {
            public DateTime CheckedUtc { get; set; }

            /// <summary>The latest release, or null if there was none.</summary>
            public ReleaseInfo Release { get; set; }
        }
    }
}
