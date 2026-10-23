using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using FreeTheLights.CarData;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;
using FreeTheLights.Updates;
using GameReaderCommon;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;

namespace FreeTheLights
{
    [PluginDescription("Drives the LEDs of wheels, dashboards, LED bars and LED flags: car accurate RPM lights, race control flags, car proximity, ABS and TC.")]
    [PluginAuthor("Alexander Groll")]
    [PluginName("FreeTheLights")]
    public class FreeTheLightsPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        private const string SettingsName = "Settings";

        private readonly FrameStore frames = new FrameStore();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private long lastAbsMs = long.MinValue / 2;
        private long lastTcMs = long.MinValue / 2;
        private long lastErrorLogMs = long.MinValue / 2;
        private readonly EngineStateTracker engine = new EngineStateTracker();

        /// <summary>How long the debris flag isn't read after reading it failed.</summary>
        private const int DebrisRetryMs = 5000;

        private long debrisRetryAtMs = long.MinValue;
        private bool debrisFailureLogged;
        private PluginManager inputSource;
        private bool gameWasRunning;

        /// <summary>The running plugin instance, used by the LED editor effects to fetch frames.</summary>
        public static FreeTheLightsPlugin Instance { get; private set; }

        public PluginManager PluginManager { get; set; }

        public PluginSettings Settings { get; private set; }

        /// <summary>Copies of the device settings that DataUpdate and the LED effects render from (see <see cref="DeviceSnapshot"/>).</summary>
        public DeviceSnapshot RenderDevices { get; } = new DeviceSnapshot();

        /// <summary>
        /// The device being set up in the wizard, rendered like the others but not saved (null = none). Set it on the UI
        /// thread, then call <see cref="PublishDevices"/>.
        /// </summary>
        public DeviceConfig WizardDevice { get; set; }

        public CarDataService CarData { get; private set; }

        /// <summary>Newer releases of the plugin and their install (null before Init).</summary>
        public UpdateService Updates { get; private set; }

        /// <summary>Where the plugin's settings and SimHub's device settings are backed up before they are changed.</summary>
        public string BackupDirectory { get; private set; }

        public TestMode TestMode { get; } = new TestMode();

        /// <summary>The driver's inputs as SimHub reports them, for elements that react to a press (and for assigning inputs).</summary>
        public InputState Inputs { get; private set; }

        /// <summary>Telemetry of the last game loop (including test inputs).</summary>
        public TelemetrySnapshot LastTelemetry { get; private set; } = new TelemetrySnapshot();

        public ImageSource PictureIcon => CreateIcon();

        public string LeftMenuTitle => "FreeTheLights";

        public void Init(PluginManager pluginManager)
        {
            SimHub.Logging.Current.Info("FreeTheLights: starting");

            var dataDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FreeTheLights");
            BackupDirectory = Path.Combine(dataDirectory, "backups");
            Settings = LoadSettings(BackupDirectory);
            if (Settings.Devices.Count == 0) Settings.Devices.Add(DeviceConfig.CreateDefault("My wheel"));
            RenderDevices.Update(Settings.Devices);

            var cacheDirectory = Path.Combine(dataDirectory, "cardata");
            CarData = new CarDataService(
                cacheDirectory,
                () => Settings.CarDataBaseUrl,
                () => TimeSpan.FromHours(Settings.CarDataRefreshHours),
                message => SimHub.Logging.Current.Info(message));

            CarData.RefreshVersion();

            // Newer releases on GitHub; deletes what a previous update left behind. Updates can only be installed when the
            // plugin was loaded from its file in SimHub's folder.
            var assembly = typeof(FreeTheLightsPlugin).Assembly;
            var installer = string.Equals(Path.GetFileName(assembly.Location), ReleaseInfo.DllAsset, StringComparison.OrdinalIgnoreCase)
                ? new UpdateInstaller(assembly.Location, new PowerShellElevatedRunner())
                : null;
            Updates = new UpdateService(
                Path.Combine(dataDirectory, "update"),
                assembly.GetName().Version,
                installer,
                message => SimHub.Logging.Current.Info(message));
            Updates.CleanupAfterUpdate();
            if (Settings.CheckForUpdates) Updates.Check();

            // SimHub's device list for the "add device" wizard, read in the background.
            SimHubCatalog.Log = (message, ex) => SimHub.Logging.Current.Warn(message, ex);
            SimHubCatalog.LoadAsync();

            this.AttachDelegate("CarDataStatus", () => CarData.StatusText);
            this.AttachDelegate("CarDataVersion", () => CarData.VersionText);
            this.AttachDelegate("CarName", () => CarData.Current?.CarName);
            this.AttachDelegate("CarLedCount", () => CarData.Current?.LedCount ?? 0);
            this.AttachDelegate("FlagName", () => LastTelemetry.FlagName);
            this.AttachDelegate("PluginVersion", () => ReleaseInfo.Normalize(Updates.Installed).ToString());
            this.AttachDelegate("UpdateAvailable", () => Updates.Status == UpdateStatus.Available);
            this.AddAction("ReloadCarData", (a, b) => CarData.Reload());

            // Every input SimHub reads (wheel buttons, encoder steps, ...), whether or not it is mapped to an action.
            Inputs = new InputState(() => clock.ElapsedMilliseconds);
            inputSource = pluginManager;
            pluginManager.InputPressed += OnInputPressed;
            pluginManager.InputReleased += OnInputReleased;

            Instance = this;
        }

        public void End(PluginManager pluginManager)
        {
            if (inputSource != null)
            {
                inputSource.InputPressed -= OnInputPressed;
                inputSource.InputReleased -= OnInputReleased;
                inputSource = null;
            }

            SaveSettings();
            CarData?.Dispose();
            Updates?.Dispose();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Reads the saved settings as JSON and migrates them to the current format (<see cref="SettingsMigrator"/>).
        /// Settings that were migrated or replaced by defaults are first copied to <paramref name="backupDirectory"/>.
        /// </summary>
        private PluginSettings LoadSettings(string backupDirectory)
        {
            JObject saved = null;
            try
            {
                saved = this.ReadCommonSettings<JObject>(SettingsName, () => null);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: could not read the settings, starting with defaults", ex);
            }

            var loaded = SettingsMigrator.Load(saved);
            if (loaded.NeedsBackup)
            {
                try
                {
                    Directory.CreateDirectory(backupDirectory);
                    var file = Path.Combine(backupDirectory, $"settings-v{loaded.SavedVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.WriteAllText(file, saved.ToString());
                    SimHub.Logging.Current.Info($"FreeTheLights: saved settings backed up to {file}");
                }
                catch (Exception ex)
                {
                    SimHub.Logging.Current.Error("FreeTheLights: could not back up the saved settings", ex);
                }
            }

            if (loaded.WasReset) SimHub.Logging.Current.Warn("FreeTheLights: " + loaded.Message, loaded.Error);
            else if (loaded.Message != null) SimHub.Logging.Current.Info("FreeTheLights: " + loaded.Message);
            return loaded.Settings;
        }

        public void SaveSettings()
        {
            if (Settings == null) return;
            try
            {
                this.SaveCommonSettings(SettingsName, Settings);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: could not save settings", ex);
            }
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            return new UI.SettingsControl(this);
        }

        /// <summary>Called once per game loop. Must be fast and never throw.</summary>
        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            try
            {
                var nowMs = clock.ElapsedMilliseconds;

                // A release SimHub never reported (a wheel unplugged or reset while a button was held) would keep its
                // press color on forever; starting or stopping a game starts afresh.
                if (data.GameRunning != gameWasRunning)
                {
                    gameWasRunning = data.GameRunning;
                    Inputs?.Clear();
                }

                var telemetry = BuildSnapshot(pluginManager, data, nowMs);

                if (telemetry.GameRunning) CarData.Request(telemetry.GameName, telemetry.CarId);
                var profile = CarData.Current;

                TestMode.Apply(telemetry, profile, nowMs);
                LastTelemetry = telemetry;

                var wiringPart = TestMode.WiringPartId;
                var confirmationDevice = TestMode.ConfirmationDeviceId;
                foreach (var device in RenderDevices.Devices)
                {
                    foreach (var part in device.Parts)
                    {
                        try
                        {
                            var frame = part.Id == wiringPart ? WiringPattern.Render(part, nowMs)
                                : device.Id == confirmationDevice ? ConfirmationPattern.Render(part, nowMs)
                                : device.Enabled ? FrameRenderer.Render(part, telemetry, profile, nowMs)
                                : new LedColor[part.TotalLeds];
                            frames.Put(part.Id, frame, nowMs);
                        }
                        catch (Exception ex)
                        {
                            // This part keeps its previous frame until it is too old (FrameStore); the others still update.
                            LogErrorThrottled($"FreeTheLights: error rendering '{device.Name}' / '{part.Name}'", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogErrorThrottled("FreeTheLights: error in DataUpdate", ex);
            }
        }

        /// <summary>
        /// Latest frame of a part (physical LED order of its channel), or null if the part is unknown or the game loop
        /// hasn't rendered it for a while (<see cref="FrameStore.MaxAgeMs"/>): the LEDs are then released.
        /// </summary>
        public LedColor[] GetFrame(Guid partId)
        {
            return frames.Get(partId, clock.ElapsedMilliseconds);
        }

        /// <summary>
        /// Hands changed settings (and the <see cref="WizardDevice"/>) to the render threads; true if something changed.
        /// Call it on the UI thread, which owns the live settings.
        /// </summary>
        public bool PublishDevices()
        {
            var wizardDevice = WizardDevice;
            return RenderDevices.Update(wizardDevice == null ? Settings.Devices : Settings.Devices.Concat(new[] { wizardDevice }));
        }

        /// <summary>The render copy of a device's settings (never modify it), or null if the device is unknown.</summary>
        public DeviceConfig FindDevice(Guid deviceId)
        {
            return RenderDevices.Find(deviceId);
        }

        /// <summary>The render copy of a device's part (never modify it); an empty or unknown part id means the first part.</summary>
        public DevicePart FindPart(Guid deviceId, Guid partId)
        {
            return RenderDevices.FindPart(deviceId, partId);
        }

        /// <summary>SimHub's input events (raised on background threads; the return value is ignored by SimHub). Must never throw.</summary>
        private bool OnInputPressed(string input)
        {
            try
            {
                Inputs?.Press(input);
            }
            catch (Exception ex)
            {
                LogErrorThrottled("FreeTheLights: error handling an input press", ex);
            }

            return false;
        }

        private bool OnInputReleased(string input)
        {
            try
            {
                Inputs?.Release(input);
            }
            catch (Exception ex)
            {
                LogErrorThrottled("FreeTheLights: error handling an input release", ex);
            }

            return false;
        }

        /// <summary>Logs an error at most every 10 s, so a fault that repeats every frame doesn't flood the log. Thread-safe.</summary>
        internal void LogErrorThrottled(string message, Exception ex)
        {
            var now = clock.ElapsedMilliseconds;
            var last = Interlocked.Read(ref lastErrorLogMs);
            if (now - last < 10000) return;
            if (Interlocked.CompareExchange(ref lastErrorLogMs, now, last) != last) return;
            SimHub.Logging.Current.Error(message, ex);
        }

        private TelemetrySnapshot BuildSnapshot(PluginManager pluginManager, GameData data, long nowMs)
        {
            // Inputs also outside a game, so pressing a button can be tried in the settings.
            var t = new TelemetrySnapshot { GameName = pluginManager.GameName, Inputs = Inputs };
            var d = data.NewData;
            if (!data.GameRunning || d == null)
            {
                engine.Reset();
                return t;
            }

            t.GameRunning = true;
            t.CarId = d.CarId;
            t.CarModel = d.CarModel;
            t.Rpm = d.Rpms;
            t.MaxRpm = d.MaxRpm;
            t.Redline = d.Redline;
            t.Gear = d.Gear;

            t.FlagYellow = d.Flag_Yellow != 0;
            t.FlagBlue = d.Flag_Blue != 0;
            t.FlagWhite = d.Flag_White != 0;
            t.FlagBlack = d.Flag_Black != 0;
            t.FlagCheckered = d.Flag_Checkered != 0;
            t.FlagGreen = d.Flag_Green != 0;
            t.FlagOrange = d.Flag_Orange != 0;
            t.FlagName = d.Flag_Name;
            t.FlagDebris = IsIRacingDebris(pluginManager, nowMs);

            t.SpotterCarLeft = d.SpotterCarLeft != 0;
            t.SpotterCarRight = d.SpotterCarRight != 0;
            t.SpotterCarLeftDistance = d.SpotterCarLeftDistance;
            t.SpotterCarRightDistance = d.SpotterCarRightDistance;

            // Hold ABS/TC for a short time: sims often report them for single frames only.
            if (d.ABSActive != 0) lastAbsMs = nowMs;
            if (d.TCActive != 0) lastTcMs = nowMs;
            t.AbsActive = nowMs - lastAbsMs <= Settings.IndicatorHoldMs;
            t.TcActive = nowMs - lastTcMs <= Settings.IndicatorHoldMs;

            t.PitLimiter = d.PitLimiterOn != 0;

            // Not every game reports EngineStarted; a turning engine runs, too.
            engine.Update(true, d.CarId, d.EngineStarted != 0 || d.Rpms > 0, nowMs);
            t.EngineRunning = engine.IsRunning;
            t.EngineStartedAtMs = engine.StartedAtMs;
            t.EngineStoppedAtMs = engine.StoppedAtMs;

            return t;
        }

        /// <summary>SimHub's common game data has no debris flag; iRacing reports it in its session flags.</summary>
        private bool IsIRacingDebris(PluginManager pluginManager, long nowMs)
        {
            if (!string.Equals(pluginManager.GameName, "IRacing", StringComparison.OrdinalIgnoreCase)) return false;
            if (nowMs < debrisRetryAtMs) return false;
            try
            {
                return IRacingSessionFlags.HasDebris(pluginManager.GetPropertyValue(IRacingSessionFlags.PropertyName));
            }
            catch (Exception ex)
            {
                // No raw data (yet), or not under this name: no debris flag. Trying again on every frame would throw an
                // exception 60 times a second, so wait a while.
                debrisRetryAtMs = nowMs + DebrisRetryMs;
                if (!debrisFailureLogged)
                {
                    debrisFailureLogged = true;
                    SimHub.Logging.Current.Warn($"FreeTheLights: could not read iRacing's {IRacingSessionFlags.PropertyName}, the debris flag stays off", ex);
                }

                return false;
            }
        }

        private static ImageSource CreateIcon()
        {
            // 24x24 white icon: a row of LEDs above two LED columns.
            var group = new DrawingGroup();
            var brush = Brushes.White;
            for (var i = 0; i < 5; i++) group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(4 + i * 4, 4), 1.6, 1.6)));
            for (var i = 0; i < 3; i++)
            {
                group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(3, 11 + i * 5), 1.6, 1.6)));
                group.Children.Add(new GeometryDrawing(brush, null, new EllipseGeometry(new Point(21, 11 + i * 5), 1.6, 1.6)));
            }

            group.Children.Add(new GeometryDrawing(null, new Pen(brush, 1.5), new RectangleGeometry(new Rect(7, 9, 10, 12), 2, 2)));
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }
    }
}
