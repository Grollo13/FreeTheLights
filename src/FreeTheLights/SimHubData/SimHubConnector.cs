using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using Newtonsoft.Json.Linq;
using SimHub.Plugins;
using SimHub.Plugins.Devices;

namespace FreeTheLights.SimHubData
{
    /// <summary>
    /// Finds a preset device's SimHub device (same device type id) and checks or adds the FreeTheLights effect in the
    /// LED profile of each part's channel, through SimHub's device settings JSON (DeviceInstance.GetSettings/SetSettings).
    /// </summary>
    public static class SimHubConnector
    {
        /// <summary>The connection state of every part of a device made from a SimHub preset.</summary>
        public static IReadOnlyList<PartConnection> Check(PluginManager pluginManager, DeviceConfig device)
        {
            var instances = Instances(pluginManager, device);
            if (instances == null) return device.Parts.Select(p => new PartConnection(p, ConnectionState.Unknown, null)).ToList();
            if (instances.Count == 0) return device.Parts.Select(p => new PartConnection(p, ConnectionState.DeviceMissing, null)).ToList();

            var settings = instances.Select(i => i.GetSettings(false, false) as JObject).ToList();
            return device.Parts.Select(part =>
            {
                var profiles = settings.Select(s => SimHubProfiles.ActiveProfile(s, part.Channel)).Where(p => p != null).ToList();
                var shown = profiles.FirstOrDefault(p => SimHubProfiles.Shows(p, device, part));
                if (shown != null) return new PartConnection(part, ConnectionState.Connected, (string)shown["Name"]);
                var other = profiles.FirstOrDefault(p => SimHubProfiles.ShowsOther(p, device, part));
                if (other != null) return new PartConnection(part, ConnectionState.WrongDevice, (string)other["Name"]);
                return new PartConnection(part, ConnectionState.NotConnected, (string)profiles.FirstOrDefault()?["Name"]);
            }).ToList();
        }

        /// <summary>
        /// What Connect will do for <paramref name="part"/>, for the confirmation: e.g. create the profile
        /// "FreeTheLights: Dashboard" and make it active. Null when SimHub's devices can't be read.
        /// </summary>
        public static string Describe(PluginManager pluginManager, DeviceConfig device, DevicePart part)
        {
            var instance = Instances(pluginManager, device)?.FirstOrDefault();
            if (instance == null) return null;
            var (action, profile) = SimHubProfiles.PlanConnect(instance.GetSettings(false, false) as JObject, device, part);
            switch (action)
            {
                case ConnectAction.UseActive: return $"FreeTheLights will control \"{part.Name}\" of the {device.Preset?.ProductName}.";
                case ConnectAction.SwitchToOwn: return $"The {device.Preset?.ProductName} will switch to the FreeTheLights LED profile for \"{part.Name}\".";
                case ConnectAction.CreateOwn: return $"The {device.Preset?.ProductName} gets a FreeTheLights LED profile for \"{part.Name}\".";
                default: return null;
            }
        }

        /// <summary>
        /// Shows <paramref name="part"/> on every SimHub device of the preset's type, only through FreeTheLights' own LED
        /// profile of the part's channel, which becomes the active profile (created when needed; see
        /// <see cref="SimHubProfiles.Connect"/>). Profiles of the user or other plugins are never changed. SimHub's
        /// device settings are saved to <paramref name="backupFolder"/> first. Returns an error message, or null on
        /// success (checked by reading the settings back). Throws when a backup can't be written (nothing is changed
        /// then) or SimHub refuses the settings (the devices changed before are put back).
        /// </summary>
        public static string Connect(PluginManager pluginManager, DeviceConfig device, DevicePart part, string backupFolder)
        {
            if (part.Channel == LedChannel.Matrix) return "LED matrices are set up in SimHub's matrix editor.";
            var instances = Instances(pluginManager, device);
            if (instances == null) return "SimHub's devices can't be read right now. Please try again.";
            if (instances.Count == 0) return $"Add the {device.Preset?.ProductName} in SimHub's Devices first.";

            // Every backup is written before anything changes, so a failed backup leaves all devices as they were.
            var planned = new List<(DeviceInstance instance, JObject settings)>();
            foreach (var instance in instances)
            {
                var settings = instance.GetSettings(false, false) as JObject;
                if (SimHubProfiles.PlanConnect(settings, device, part).action == ConnectAction.Impossible) continue;

                Directory.CreateDirectory(backupFolder);
                File.WriteAllText(BackupFile.NewPath(backupFolder, instance.InstanceId.ToString(), DateTime.Now), settings.ToString());
                planned.Add((instance, settings));
            }

            var applied = new List<(DeviceInstance instance, JObject original)>();
            try
            {
                foreach (var (instance, settings) in planned)
                {
                    var original = (JObject)settings.DeepClone();
                    if (SimHubProfiles.Connect(settings, device, part) == null || JToken.DeepEquals(settings, original)) continue;
                    Apply(instance, settings);
                    applied.Add((instance, original));
                }
            }
            catch
            {
                // Not half connected: the devices changed so far get their settings back (the backups have them too).
                foreach (var (instance, original) in applied)
                {
                    try
                    {
                        Apply(instance, original);
                    }
                    catch (Exception)
                    {
                        // The error that stopped Connect is the one to report.
                    }
                }

                throw;
            }

            return Check(pluginManager, device).Any(c => c.Part == part && c.State == ConnectionState.Connected)
                ? null
                : "SimHub didn't apply the change. Select the FreeTheLights profile in SimHub's LED editor.";
        }

        /// <summary>
        /// The SimHub device and channel an LED profile belongs to (by its id), e.g. where a FreeTheLights effect was added;
        /// null when it can't be found.
        /// </summary>
        public static ProfileOwner FindOwner(PluginManager pluginManager, Guid profileId)
        {
            if (pluginManager == null || profileId == Guid.Empty) return null;
            foreach (var instance in Devices(pluginManager) ?? Enumerable.Empty<DeviceInstance>())
            {
                var channel = SimHubProfiles.ChannelOfProfile(instance?.GetSettings(false, false) as JObject, profileId);
                if (channel.HasValue) return new ProfileOwner(instance.DeviceDescriptor?.DeviceTypeID?.Trim(), instance.MainDisplayName ?? instance.DeviceDescriptor?.Name, channel.Value);
            }

            return null;
        }

        /// <summary>
        /// Copies of the settings of every SimHub device of the preset's type, as they are now: <see cref="Restore"/> puts
        /// them back, e.g. when the device wizard that connected the device is cancelled. Empty when SimHub's devices
        /// can't be read.
        /// </summary>
        public static IReadOnlyList<DeviceSettingsCopy> Copy(PluginManager pluginManager, DeviceConfig device)
        {
            return (Instances(pluginManager, device) ?? new List<DeviceInstance>())
                .Select(i => new DeviceSettingsCopy(i, (i.GetSettings(false, false) as JObject)?.DeepClone() as JObject))
                .Where(c => c.Settings != null)
                .ToList();
        }

        /// <summary>
        /// Gives SimHub's devices back the LED settings of <paramref name="copies"/> (see <see cref="Copy"/>). A
        /// composite device's other components (its display) keep their current settings.
        /// </summary>
        public static void Restore(IEnumerable<DeviceSettingsCopy> copies)
        {
            foreach (var copy in copies) Apply(copy.Instance, copy.Settings);
        }

        /// <summary>
        /// Hands <paramref name="settings"/> (in the shape GetSettings returns) to SimHub: a composite device only to its
        /// LED component (<see cref="SimHubProfiles.LedComponent"/>), since setting the whole device would append its
        /// display's lists to the ones it has.
        /// </summary>
        private static void Apply(DeviceInstance instance, JObject settings)
        {
            if (!(instance is CompositeDeviceInstance composite))
            {
                instance.SetSettings(settings, false);
                return;
            }

            var key = SimHubProfiles.LedComponent(settings);
            var component = key == null ? null : composite.Devices?.FirstOrDefault(d => d.CompositeCode == key);
            if (component == null) throw new InvalidOperationException($"The {instance.MainDisplayName} has no LED component to set up.");
            component.SetSettings(settings[key], false);
        }

        /// <summary>Whether a device of this SimHub type is added in SimHub's Devices (false when the list isn't available).</summary>
        public static bool HasDevice(PluginManager pluginManager, string deviceTypeId) => Instances(pluginManager, deviceTypeId)?.Count > 0;

        /// <summary>The SimHub devices of the preset's type; null when SimHub's device list isn't available.</summary>
        private static List<DeviceInstance> Instances(PluginManager pluginManager, DeviceConfig device) => Instances(pluginManager, device?.Preset?.DeviceTypeId);

        private static List<DeviceInstance> Instances(PluginManager pluginManager, string deviceTypeId)
        {
            if (pluginManager == null || deviceTypeId == null) return null;
            var devices = Devices(pluginManager);
            if (devices == null) return null;
            return devices.Where(d => string.Equals(d.DeviceDescriptor?.DeviceTypeID?.Trim(), deviceTypeId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        /// <summary>
        /// The devices added in SimHub's Devices; null when the list isn't available. DevicesPlugin.GetDevices also lists
        /// the components of a composite device (a dashboard's LEDs and display), with the dashboard's device type: they
        /// are left out, since setting a display's settings appends to its lists (see <see cref="Apply"/>).
        /// </summary>
        private static IEnumerable<DeviceInstance> Devices(PluginManager pluginManager) =>
            pluginManager?.GetPlugin<DevicesPlugin>()?.GetDevices()?.Where(d => d != null && d.RootInstance == d);
    }
}

namespace FreeTheLights.SimHubData
{
    /// <summary>A copy of a SimHub device's settings (see <see cref="SimHubConnector.Copy"/>).</summary>
    public sealed class DeviceSettingsCopy
    {
        public DeviceSettingsCopy(DeviceInstance instance, JObject settings)
        {
            Instance = instance;
            Settings = settings;
        }

        public DeviceInstance Instance { get; }

        /// <summary>The device's settings JSON (the inner object of DeviceInstance.GetSettings).</summary>
        public JObject Settings { get; }

        /// <summary>SimHub's id of the device instance.</summary>
        public string InstanceId => Instance.InstanceId.ToString();
    }
}

namespace FreeTheLights.SimHubData
{
    /// <summary>The SimHub device and channel an LED profile belongs to.</summary>
    public sealed class ProfileOwner
    {
        public ProfileOwner(string deviceTypeId, string name, Settings.LedChannel channel)
        {
            DeviceTypeId = deviceTypeId;
            Name = name;
            Channel = channel;
        }

        public string DeviceTypeId { get; }

        public string Name { get; }

        public Settings.LedChannel Channel { get; }
    }
}
