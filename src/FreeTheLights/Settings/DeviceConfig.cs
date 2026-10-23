using System;
using System.Collections.ObjectModel;
using System.Linq;
using FreeTheLights.Core;
using Newtonsoft.Json;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// What kind of hardware a device is; it decides how the device is previewed. Values are explicit because they are
    /// stored in the settings; never renumber them.
    /// </summary>
    public enum DeviceType
    {
        Custom = 0,
        Wheel = 1,
        Dashboard = 2,
        LedBar = 3,
        LedFlag = 4,
    }

    /// <summary>
    /// The SimHub device a device was created from. The layout was copied from SimHub when the device was added, so
    /// a SimHub update never changes a device behind the user's back; "Refresh from SimHub" copies it again.
    /// </summary>
    public class SimHubPreset
    {
        /// <summary>SimHub's device type id (the name of its .shdevice file).</summary>
        public string DeviceTypeId { get; set; }

        /// <summary>SimHub's product name, e.g. "Ascher Racing Artura GT4".</summary>
        public string ProductName { get; set; }

        /// <summary>Product picture, relative to the SimHub folder (DevicesLogos\&lt;id&gt;.png), or null.</summary>
        public string ProductImageFile { get; set; }

        /// <summary>
        /// Picture the LED positions refer to, relative to the SimHub folder, or null: a wheel's photo from its SimHub
        /// button layout, or a dashboard's product picture.
        /// </summary>
        public string PhotoFile { get; set; }

        /// <summary>
        /// Size of the canvas the positions (<see cref="LedGroup.PhotoX"/>, <see cref="LedGroup.PhotoLeds"/>) are
        /// measured in: SimHub's layout canvas for wheels, the picture's pixels for dashboards.
        /// </summary>
        public double CanvasWidth { get; set; }

        public double CanvasHeight { get; set; }

        /// <summary>Dashboards: where the LED positions on <see cref="PhotoFile"/> come from, or that the user chose the generic drawing.</summary>
        public PhotoLedSource LedPositions { get; set; }

        /// <summary>A dashboard shown on its picture (positions known or placed, not declined).</summary>
        [JsonIgnore]
        public bool ShowsLedsOnPhoto => PhotoFile != null && (LedPositions == PhotoLedSource.BuiltIn || LedPositions == PhotoLedSource.Found || LedPositions == PhotoLedSource.Manual);
    }

    /// <summary>
    /// One physical device (wheel, dashboard, LED bar, LED flag) as seen by FreeTheLights: one card in the device list,
    /// with one <see cref="DevicePart"/> per SimHub LED channel it drives.
    /// </summary>
    public class DeviceConfig : ObservableObject
    {
        private Guid id = Guid.NewGuid();
        private string name = "New device";
        private bool enabled = true;
        private DeviceType type;
        private SimHubPreset preset;

        public Guid Id { get => id; set => Set(ref id, value); }

        public string Name { get => name; set => Set(ref name, value); }

        public bool Enabled { get => enabled; set => Set(ref enabled, value); }

        public DeviceType Type { get => type; set => Set(ref type, value); }

        /// <summary>The SimHub device this was created from, or null for a generic device.</summary>
        public SimHubPreset Preset { get => preset; set => Set(ref preset, value); }

        /// <summary>The LED outputs, one per SimHub channel; at least one.</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<DevicePart> Parts { get; set; } = new ObservableCollection<DevicePart>();

        /// <summary>Total LEDs over all parts.</summary>
        [JsonIgnore]
        public int TotalLeds => Parts.Sum(p => p.TotalLeds);

        /// <summary>
        /// The part with <paramref name="partId"/>, or the first part for an empty or unknown id (an effect whose part
        /// was not picked yet, or was removed). Null only for a device without parts.
        /// </summary>
        public DevicePart FindPart(Guid partId)
        {
            DevicePart first = null;
            foreach (var part in Parts)
            {
                if (part == null) continue;
                if (part.Id == partId) return part;
                if (first == null) first = part;
            }

            return first;
        }

        /// <summary>Repairs settings from a partial file: every device has at least one part.</summary>
        public void EnsureDefaults()
        {
            if (Parts == null) Parts = new ObservableCollection<DevicePart>();
            foreach (var broken in Parts.Where(p => p == null).ToList()) Parts.Remove(broken);
            if (Parts.Count == 0) Parts.Add(new DevicePart());
            foreach (var part in Parts) part.EnsureDefaults();
        }

        /// <summary>Deep copy with new ids for the device, its parts and their groups.</summary>
        public DeviceConfig Clone()
        {
            var copy = JsonConvert.DeserializeObject<DeviceConfig>(JsonConvert.SerializeObject(this));
            copy.Id = Guid.NewGuid();
            copy.Name = Name + " (copy)";
            copy.Parts = new ObservableCollection<DevicePart>(Parts.Select(p =>
            {
                var part = p.Clone();
                part.Name = p.Name;
                return part;
            }));
            copy.EnsureDefaults();
            return copy;
        }

        /// <summary>A device with a single part.</summary>
        public static DeviceConfig WithPart(string name, DeviceType type, DevicePart part)
        {
            var device = new DeviceConfig { Name = name, Type = type };
            device.Parts.Add(part);
            device.EnsureDefaults();
            return device;
        }

        /// <summary>A typical wheel: one part with a 15 LED shift light row and two 3 LED columns.</summary>
        public static DeviceConfig CreateDefault(string name) => WithPart(name, DeviceType.Wheel, DevicePart.CreateDefault("LEDs"));
    }

    public class PluginSettings
    {
        /// <summary>
        /// Version of the settings format written by this build. Bump it whenever saved settings would no longer load
        /// as intended (a property renamed, moved or reinterpreted, an enum value changed), and add the migration from
        /// the previous version to <see cref="SettingsMigrator.Migrations"/>.
        /// </summary>
        public const int CurrentVersion = 5;

        /// <summary>Format version of the settings. <see cref="SettingsMigrator"/> reads it from the saved JSON.</summary>
        public int SettingsVersion { get; set; } = CurrentVersion;

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<DeviceConfig> Devices { get; set; } = new ObservableCollection<DeviceConfig>();

        /// <summary>Base URL of the lovely-car-data "data" folder.</summary>
        public string CarDataBaseUrl { get; set; } = CarData.CarDataService.DefaultBaseUrl;

        /// <summary>Hours before cached car data is refreshed from the internet.</summary>
        public int CarDataRefreshHours { get; set; } = 24;

        /// <summary>ABS/TC are kept lit at least this long to avoid flicker from single-frame activations.</summary>
        public int IndicatorHoldMs { get; set; } = 100;

        /// <summary>Whether the plugin looks for a newer release on GitHub (once a day) and offers to install it.</summary>
        public bool CheckForUpdates { get; set; } = true;

        /// <summary>Repairs missing or invalid parts. Settings of other formats are handled by <see cref="SettingsMigrator"/> before this.</summary>
        public void EnsureDefaults()
        {
            SettingsVersion = CurrentVersion;
            if (Devices == null) Devices = new ObservableCollection<DeviceConfig>();
            foreach (var broken in Devices.Where(d => d == null).ToList()) Devices.Remove(broken);
            foreach (var device in Devices) device.EnsureDefaults();
            if (string.IsNullOrWhiteSpace(CarDataBaseUrl)) CarDataBaseUrl = CarData.CarDataService.DefaultBaseUrl;
            if (CarDataRefreshHours <= 0) CarDataRefreshHours = 24;
            if (IndicatorHoldMs < 0) IndicatorHoldMs = 0;
        }
    }
}
