using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.CarData
{
    public enum CarDataStatus
    {
        NoCar,
        Loading,
        Loaded,
        LoadedFromCache,
        NotFound,
        Error,
    }

    /// <summary>
    /// Fetches car LED definitions from lovely-car-data and caches them on disk.
    /// All network and disk work happens on background tasks: <see cref="Request"/> returns immediately
    /// and <see cref="Current"/> is swapped once the profile is available.
    /// </summary>
    public sealed class CarDataService : IDisposable
    {
        public const string DefaultBaseUrl = "https://raw.githubusercontent.com/Lovely-Sim-Racing/lovely-car-data/main/data/";

        private const string ManifestFile = "manifest.json";
        private const string VersionFile = "version.json";

        private readonly string cacheDirectory;
        private readonly Func<string> baseUrlProvider;
        private readonly Func<TimeSpan> maxAgeProvider;
        private readonly Action<string> log;
        private readonly HttpClient http;
        private readonly SemaphoreSlim manifestLock = new SemaphoreSlim(1, 1);

        /// <summary>Wait before the first retry of a failed load; it doubles with every further failure, up to <see cref="MaxRetryDelay"/>.</summary>
        public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(30);

        public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);

        private readonly TimeSpan retryDelay;
        private readonly Stopwatch clock = Stopwatch.StartNew();

        /// <summary>
        /// Guards the request state below, <see cref="Current"/> and <see cref="Status"/>: the game loop requests cars,
        /// <see cref="Reload"/> comes from the UI or a SimHub action, and loads finish on background tasks.
        /// </summary>
        private readonly object stateLock = new object();

        private int generation;
        private string lastGameName;
        private string lastCarId;

        /// <summary>The car being shown or loaded ("simId|carId"; null = none requested yet), and its two parts.</summary>
        private string requestedKey;
        private string requestedSimId;
        private string requestedCarId;

        /// <summary>Failed loads in a row for the current car.</summary>
        private int failures;

        /// <summary><see cref="clock"/> time at which a failed load is retried; long.MaxValue = no retry pending.</summary>
        private long retryAtMs = long.MaxValue;

        private JObject manifest;
        private DateTime manifestLoadedUtc;
        private int versionRefreshRunning;

        public CarDataService(string cacheDirectory, Func<string> baseUrlProvider, Func<TimeSpan> maxAgeProvider, Action<string> log, TimeSpan? retryDelay = null)
        {
            this.cacheDirectory = cacheDirectory;
            this.baseUrlProvider = baseUrlProvider;
            this.maxAgeProvider = maxAgeProvider;
            this.log = log ?? (_ => { });
            this.retryDelay = retryDelay ?? DefaultRetryDelay;

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FreeTheLights-SimHub-Plugin");
        }

        /// <summary>Profile of the current car, or null if none is available (yet).</summary>
        public CarLedProfile Current { get; private set; }

        public CarDataStatus Status { get; private set; } = CarDataStatus.NoCar;

        public string SimId { get; private set; }

        public string CarId { get; private set; }

        /// <summary>When the current car's file was downloaded (the cache file time).</summary>
        public DateTime? CurrentFileTimeUtc { get; private set; }

        /// <summary>The lovely-car-data version in use, or null until it has been checked.</summary>
        public CarDataVersion Version { get; private set; }

        public string VersionText => Version?.Display ?? "lovely-car-data version: checking...";

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case CarDataStatus.Loading: return $"Loading RPM lights for {CarId}...";
                    case CarDataStatus.Loaded: return $"{Current?.CarName}: car-specific RPM lights";
                    case CarDataStatus.LoadedFromCache: return $"{Current?.CarName}: car-specific RPM lights (offline copy)";
                    case CarDataStatus.NotFound: return $"{CarId}: no car-specific RPM lights, using generic ones";
                    case CarDataStatus.Error: return $"{CarId}: couldn't load the RPM lights, using generic ones";
                    default: return "No car";
                }
            }
        }

        private string FileDate => CurrentFileTimeUtc?.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "?";

        /// <summary>
        /// Requests the profile for the given game/car. Cheap to call every frame: it only starts work when the car changes,
        /// or to retry a load that failed (for example while offline), with a growing delay. Thread-safe.
        /// </summary>
        public void Request(string gameName, string carId)
        {
            // Called every frame, so the lock is uncontended nearly always. Compare the raw values first, because
            // normalizing allocates.
            lock (stateLock)
            {
                var retry = retryAtMs <= clock.ElapsedMilliseconds;
                if (!retry && requestedKey != null && string.Equals(gameName, lastGameName, StringComparison.Ordinal) && string.Equals(carId, lastCarId, StringComparison.Ordinal)) return;
                lastGameName = gameName;
                lastCarId = carId;

                var simId = NameNormalizer.Normalize(gameName);
                var key = simId + "|" + carId;
                if (!retry && key == requestedKey) return;
                StartLoad(simId, carId, key, force: false);
            }
        }

        /// <summary>Downloads the current car's file and the manifest again, ignoring the cache. Thread-safe.</summary>
        public void Reload()
        {
            RefreshVersion(force: true);
            lock (stateLock)
            {
                if (requestedKey != null) StartLoad(requestedSimId, requestedCarId, requestedKey, force: true);
            }
        }

        /// <summary>Makes <paramref name="key"/> the requested car and starts loading it. Call it holding <see cref="stateLock"/>.</summary>
        private void StartLoad(string simId, string carId, string key, bool force)
        {
            // A retry counts on with the failures of the same car; another car or a reload starts over.
            if (force || key != requestedKey) failures = 0;
            retryAtMs = long.MaxValue;
            requestedKey = key;
            requestedSimId = simId;
            requestedCarId = carId;
            var myGeneration = ++generation;
            SimId = simId;
            CarId = carId;
            Current = null;
            CurrentFileTimeUtc = null;

            var noCar = string.IsNullOrEmpty(simId) || string.IsNullOrWhiteSpace(carId);
            Status = noCar ? CarDataStatus.NoCar : CarDataStatus.Loading;
            if (noCar) return;

            // A forced load also fetches the manifest again (GetManifestAsync ignores its cached copy).
            Task.Run(() => LoadAsync(simId, carId, myGeneration, force));
        }

        private async Task LoadAsync(string simId, string carId, int myGeneration, bool force)
        {
            try
            {
                var relativePath = await ResolvePathAsync(simId, carId, force).ConfigureAwait(false);
                var (json, fromCache, fileTimeUtc) = await GetFileAsync(relativePath, force).ConfigureAwait(false);

                if (json == null)
                {
                    lock (stateLock)
                    {
                        if (myGeneration != generation) return;
                        Status = CarDataStatus.NotFound;
                    }

                    log($"FreeTheLights: no car data for '{carId}' ({simId}), tried {relativePath}");
                    return;
                }

                CarLedProfile profile;
                try
                {
                    profile = CarLedProfile.Parse(json);
                }
                catch (Exception ex)
                {
                    // The file itself can't be used (no RPM data, wrong types): loading it again gives the same result,
                    // so this is reported like a missing file and not retried. Reload downloads it again.
                    lock (stateLock)
                    {
                        if (myGeneration != generation) return;
                        Status = CarDataStatus.NotFound;
                    }

                    log($"FreeTheLights: car data {relativePath} for '{carId}' ({simId}) can't be used: {ex.Message}");
                    return;
                }

                // Checked and published under the lock, so a newer Request can't be overwritten by this older load.
                lock (stateLock)
                {
                    if (myGeneration != generation) return;
                    CurrentFileTimeUtc = fileTimeUtc;
                    Current = profile;
                    Status = fromCache ? CarDataStatus.LoadedFromCache : CarDataStatus.Loaded;
                }

                log($"FreeTheLights: loaded car data {relativePath} ({profile.LedCount} LEDs)");
            }
            catch (Exception ex)
            {
                TimeSpan delay;
                lock (stateLock)
                {
                    if (myGeneration != generation) return;
                    Status = CarDataStatus.Error;
                    failures++;
                    delay = RetryDelay(retryDelay, failures);
                    retryAtMs = clock.ElapsedMilliseconds + (long)delay.TotalMilliseconds;
                }

                log($"FreeTheLights: failed to load car data for '{carId}' ({simId}), retrying in {delay.TotalSeconds:0} s: {ex.Message}");
            }
        }

        /// <summary>Wait before retrying after <paramref name="failures"/> failed loads in a row: doubling from <paramref name="first"/>, at most <see cref="MaxRetryDelay"/>.</summary>
        public static TimeSpan RetryDelay(TimeSpan first, int failures)
        {
            var delay = first;
            for (var i = 1; i < failures && delay < MaxRetryDelay; i++) delay = TimeSpan.FromTicks(delay.Ticks * 2);
            return delay < MaxRetryDelay ? delay : MaxRetryDelay;
        }

        /// <summary>
        /// The file name does not always match the normalized car id (it is often derived from the car name),
        /// so the manifest is searched by car id first.
        /// </summary>
        private async Task<string> ResolvePathAsync(string simId, string carId, bool force)
        {
            var fallback = $"{simId}/{NameNormalizer.Normalize(carId)}.json";
            var index = await GetManifestAsync(force).ConfigureAwait(false);
            return FindInManifest(index, simId, carId) ?? fallback;
        }

        public static string FindInManifest(JObject index, string simId, string carId)
        {
            if (!(index?["cars"]?[simId] is JArray cars)) return null;

            var normalizedId = NameNormalizer.Normalize(carId);
            var match = cars.OfType<JObject>().FirstOrDefault(c => string.Equals((string)c["carId"], carId, StringComparison.OrdinalIgnoreCase))
                        ?? cars.OfType<JObject>().FirstOrDefault(c => NameNormalizer.Normalize((string)c["carId"]) == normalizedId)
                        ?? cars.OfType<JObject>().FirstOrDefault(c => NameNormalizer.Normalize((string)c["carName"]) == normalizedId);
            return (string)match?["path"];
        }

        private async Task<JObject> GetManifestAsync(bool force)
        {
            await manifestLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!force && manifest != null && DateTime.UtcNow - manifestLoadedUtc < maxAgeProvider()) return manifest;

                var (json, _, _) = await GetFileAsync(ManifestFile, force).ConfigureAwait(false);
                if (json == null) return manifest;
                manifest = JObject.Parse(json);
                manifestLoadedUtc = DateTime.UtcNow;
                return manifest;
            }
            catch (Exception ex)
            {
                log($"FreeTheLights: could not load car data manifest: {ex.Message}");
                return manifest;
            }
            finally
            {
                manifestLock.Release();
            }
        }

        /// <summary>
        /// Returns a fresh cached copy, otherwise downloads it (and updates the cache), otherwise falls back
        /// to a stale cached copy. Returns null json when the file does not exist anywhere.
        /// The time is when the returned content was downloaded. Only JSON objects are returned or cached.
        /// </summary>
        private async Task<(string json, bool fromCache, DateTime timeUtc)> GetFileAsync(string relativePath, bool force)
        {
            var cachePath = ResolveCachePath(cacheDirectory, relativePath);
            if (cachePath == null) throw new InvalidDataException($"Rejected car data path '{relativePath}'");

            var cached = TryReadCache(cachePath);
            if (!force && cached.HasValue && DateTime.UtcNow - cached.Value.timeUtc < maxAgeProvider())
            {
                return cached.Value;
            }

            try
            {
                var url = baseUrlProvider().TrimEnd('/') + "/" + string.Join("/", relativePath.Split('/').Select(Uri.EscapeDataString));
                using (var response = await http.GetAsync(url).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        if (cached.HasValue) return cached.Value;
                        return (null, false, DateTime.UtcNow);
                    }

                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!IsJsonObject(json)) throw new InvalidDataException($"{url} did not return a JSON object");
                    TryWriteCache(cachePath, json);
                    return (json, false, DateTime.UtcNow);
                }
            }
            catch (Exception) when (cached.HasValue)
            {
                return cached.Value;
            }
        }

        /// <summary>Cache file for a manifest path, or null if the path would leave the cache folder or is invalid.</summary>
        public static string ResolveCachePath(string cacheDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            try
            {
                var root = Path.GetFullPath(cacheDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        /// <summary>True when <paramref name="content"/> is a JSON object, as car files and the manifest are.</summary>
        public static bool IsJsonObject(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return false;
            try
            {
                return JToken.Parse(content) is JObject;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>The cached copy, or null when there is none. A cached file that isn't a JSON object is deleted.</summary>
        private (string json, bool fromCache, DateTime timeUtc)? TryReadCache(string cachePath)
        {
            try
            {
                if (!File.Exists(cachePath)) return null;
                var json = File.ReadAllText(cachePath);
                if (IsJsonObject(json)) return (json, true, File.GetLastWriteTimeUtc(cachePath));

                log($"FreeTheLights: deleting corrupt cache file {cachePath}");
                File.Delete(cachePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                log($"FreeTheLights: could not read cache file {cachePath}: {ex.Message}");
            }

            return null;
        }

        /// <summary>Writes a cache file through a temp file, so it is never half-written. Failures are only logged.</summary>
        private void TryWriteCache(string cachePath, string content)
        {
            var tempPath = cachePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                File.WriteAllText(tempPath, content);
                if (File.Exists(cachePath)) File.Replace(tempPath, cachePath, null);
                else File.Move(tempPath, cachePath);
            }
            catch (Exception ex)
            {
                log($"FreeTheLights: could not write cache file {cachePath}: {ex.Message}");
            }
        }

        /// <summary>Checks the lovely-car-data version in the background (at most once per refresh period unless forced).</summary>
        public void RefreshVersion(bool force = false)
        {
            if (Interlocked.Exchange(ref versionRefreshRunning, 1) == 1) return;
            Task.Run(async () =>
            {
                try
                {
                    Version = await LoadVersionAsync(force).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log($"FreeTheLights: could not determine the lovely-car-data version: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref versionRefreshRunning, 0);
                }
            });
        }

        private async Task<CarDataVersion> LoadVersionAsync(bool force)
        {
            var cachePath = Path.Combine(cacheDirectory, VersionFile);
            CarDataVersion cached = null;
            if (File.Exists(cachePath))
            {
                try
                {
                    cached = JsonConvert.DeserializeObject<CarDataVersion>(File.ReadAllText(cachePath));
                }
                catch (JsonException)
                {
                    cached = null;
                }
            }

            if (!CarDataVersion.TryParseBaseUrl(baseUrlProvider(), out var owner, out var repo, out var branch))
            {
                return new CarDataVersion { FormatVersion = "(custom source)", CheckedUtc = DateTime.UtcNow };
            }

            if (!force && cached != null && cached.Branch == branch && DateTime.UtcNow - cached.CheckedUtc < maxAgeProvider())
            {
                return cached;
            }

            try
            {
                var version = new CarDataVersion { Branch = branch, CheckedUtc = DateTime.UtcNow };

                var changelogUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/changelog.md";
                version.FormatVersion = CarDataVersion.ParseFormatVersion(await http.GetStringAsync(changelogUrl).ConfigureAwait(false));

                // Last commit that touched the data folder (GitHub API, unauthenticated: 60 requests/hour, we use one per day).
                var commitsUrl = $"https://api.github.com/repos/{owner}/{repo}/commits?sha={Uri.EscapeDataString(branch)}&path=data&per_page=1";
                if (JArray.Parse(await http.GetStringAsync(commitsUrl).ConfigureAwait(false)).FirstOrDefault() is JObject commit)
                {
                    version.CommitSha = (string)commit["sha"];
                    version.CommitDateUtc = (DateTime?)commit["commit"]?["committer"]?["date"];
                    if (version.CommitDateUtc.HasValue) version.CommitDateUtc = version.CommitDateUtc.Value.ToUniversalTime();
                }

                TryWriteCache(cachePath, JsonConvert.SerializeObject(version, Formatting.Indented));
                return version;
            }
            catch (Exception ex) when (cached != null)
            {
                log($"FreeTheLights: lovely-car-data version check failed, using cached information: {ex.Message}");
                cached.Offline = true;
                return cached;
            }
        }

        public void Dispose()
        {
            lock (stateLock) generation++;
            http.Dispose();
            manifestLock.Dispose();
        }
    }
}
