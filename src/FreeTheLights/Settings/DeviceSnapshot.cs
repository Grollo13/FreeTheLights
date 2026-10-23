using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Copies of the device settings for the render threads (DataUpdate and the LED effects).
    /// The UI binds to and edits the live settings, so reading those from another thread races with the edits.
    /// <see cref="Update"/> publishes fresh copies whenever the settings changed; nobody modifies a published copy.
    /// </summary>
    public sealed class DeviceSnapshot
    {
        private DeviceConfig[] devices = new DeviceConfig[0];
        private string publishedJson;

        /// <summary>The latest copies. Safe to read from any thread; never modify them.</summary>
        public DeviceConfig[] Devices => Volatile.Read(ref devices);

        /// <summary>
        /// Publishes copies of <paramref name="liveDevices"/> if they changed since the last call; returns true if it did.
        /// Call it only from the thread that owns the live settings (Init, then the UI thread).
        /// </summary>
        public bool Update(IEnumerable<DeviceConfig> liveDevices)
        {
            var json = JsonConvert.SerializeObject((liveDevices ?? Enumerable.Empty<DeviceConfig>()).Where(d => d != null).ToArray());
            if (json == publishedJson) return false;

            var copies = JsonConvert.DeserializeObject<DeviceConfig[]>(json) ?? new DeviceConfig[0];
            foreach (var copy in copies) copy.EnsureDefaults();
            publishedJson = json;
            Volatile.Write(ref devices, copies);
            return true;
        }

        /// <summary>The copy of a device's part (see <see cref="DeviceConfig.FindPart"/>), or null for an unknown device.</summary>
        public DevicePart FindPart(Guid deviceId, Guid partId) => Find(deviceId)?.FindPart(partId);

        /// <summary>The copy of the device with <paramref name="id"/>, or null.</summary>
        public DeviceConfig Find(Guid id)
        {
            foreach (var device in Devices)
            {
                if (device.Id == id) return device;
            }

            return null;
        }
    }
}
