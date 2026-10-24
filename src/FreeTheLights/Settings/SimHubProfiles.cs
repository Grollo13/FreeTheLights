using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Reads and edits the LED profiles in a SimHub device's settings JSON (the format of its .shdevice files and of
    /// DeviceInstance.GetSettings). A FreeTheLights effect is stored there as
    /// { "ContainerType": "FreeTheLights.Effect.FreeTheLightsEffect", "DeviceId": ..., "PartId": ... }.
    /// </summary>
    /// <summary>What Connect does in a SimHub device's channel (see <see cref="SimHubProfiles.PlanConnect"/>).</summary>
    public enum ConnectAction
    {
        /// <summary>The channel has no LED profiles at all (the device doesn't have this channel).</summary>
        Impossible,

        /// <summary>The active profile is FreeTheLights' own: add or point the effect there.</summary>
        UseActive,

        /// <summary>FreeTheLights' own profile exists but isn't active: make it active (and make sure it shows the part).</summary>
        SwitchToOwn,

        /// <summary>No own profile yet: create one with the effect and make it active.</summary>
        CreateOwn,
    }

    public static class SimHubProfiles
    {
        /// <summary>Names of the profiles FreeTheLights creates start with this.</summary>
        public const string OwnProfilePrefix = "FreeTheLights";

        public const string StripEffectType = "FreeTheLights.Effect.FreeTheLightsEffect";
        public const string MatrixEffectType = "FreeTheLights.Effect.FreeTheLightsMatrixEffect";

        /// <summary>The active LED profile of a channel, or null when the device has no profile for it.</summary>
        public static JObject ActiveProfile(JObject deviceSettings, LedChannel channel)
        {
            var ledRoot = LedRoot(deviceSettings);
            if (!(ledRoot?[ChannelKey(channel)] is JObject channelSettings) || !(channelSettings["Profiles"] is JArray profiles)) return null;
            var activeId = (string)channelSettings["activeProfileId"];
            return profiles.OfType<JObject>().FirstOrDefault(p => (string)p["ProfileId"] == activeId) ?? profiles.OfType<JObject>().FirstOrDefault();
        }

        /// <summary>
        /// What <see cref="Connect"/> would do in the channel of <paramref name="part"/>, and the profile it would use
        /// (null when it creates one). Profiles FreeTheLights didn't create are never changed: when one of them is
        /// active (or none is), FreeTheLights switches to its own profile, creating it if needed.
        /// </summary>
        public static (ConnectAction action, JObject profile) PlanConnect(JObject deviceSettings, DeviceConfig device, DevicePart part)
        {
            var channel = ChannelSettings(deviceSettings, part.Channel);
            if (channel == null) return (ConnectAction.Impossible, null);
            var profiles = (channel["Profiles"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
            var activeId = (string)channel["activeProfileId"];
            var active = profiles.FirstOrDefault(p => activeId != null && (string)p["ProfileId"] == activeId);
            if (active != null && IsOwnProfile(active, device, part)) return (ConnectAction.UseActive, active);
            var own = profiles.FirstOrDefault(p => IsOwnProfile(p, device, part));
            return own != null ? (ConnectAction.SwitchToOwn, own) : (ConnectAction.CreateOwn, null);
        }

        /// <summary>
        /// Makes the channel of <paramref name="part"/> show the part, only through FreeTheLights' own profile (see
        /// <see cref="PlanConnect"/>): the effect is added to or pointed at the part in that profile, and it becomes the
        /// active profile. A created profile copies the neutral settings of the previously active one (brightness),
        /// without its effects, game or car binding and scripts. Its id is remembered in
        /// <see cref="DevicePart.SimHubProfileIds"/>. Returns the profile shown, or null when the device has no such channel.
        /// </summary>
        public static JObject Connect(JObject deviceSettings, DeviceConfig device, DevicePart part)
        {
            var (action, profile) = PlanConnect(deviceSettings, device, part);
            if (action == ConnectAction.Impossible) return null;
            var channel = ChannelSettings(deviceSettings, part.Channel);
            if (!(channel["Profiles"] is JArray profiles))
            {
                profiles = new JArray();
                channel["Profiles"] = profiles;
            }

            if (action == ConnectAction.CreateOwn)
            {
                var template = profiles.OfType<JObject>().FirstOrDefault(p => (string)p["ProfileId"] == (string)channel["activeProfileId"]) ?? profiles.OfType<JObject>().FirstOrDefault();
                profile = NewProfile(template, UniqueName(profiles, OwnProfileName(device, part)));
                profiles.Add(profile);
            }

            if (!Shows(profile, device, part) && !RetargetEffect(profile, device, part)) AddEffect(profile, device, part);
            var id = (string)profile["ProfileId"];
            channel["activeProfileId"] = id;
            if (Guid.TryParse(id, out var guid) && !part.SimHubProfileIds.Contains(guid)) part.SimHubProfileIds.Add(guid);
            return profile;
        }

        /// <summary>The name of the profile FreeTheLights creates for a part, e.g. "FreeTheLights: Dashboard" or "FreeTheLights: F-PRO / Buttons".</summary>
        public static string OwnProfileName(DeviceConfig device, DevicePart part) =>
            $"{OwnProfilePrefix}: {device.Name}" + (device.Parts.Count > 1 ? $" / {part.Name}" : string.Empty);

        /// <summary>A profile FreeTheLights created for this part: its id is remembered, or it has the part's own profile name.</summary>
        public static bool IsOwnProfile(JObject profile, DeviceConfig device, DevicePart part)
        {
            if (profile == null) return false;
            if (Guid.TryParse((string)profile["ProfileId"], out var id) && part.SimHubProfileIds.Contains(id)) return true;
            var name = (string)profile["Name"];
            var own = OwnProfileName(device, part);
            return name != null && (name == own || (name.StartsWith(own + " (", StringComparison.Ordinal) && name.EndsWith(")", StringComparison.Ordinal)));
        }

        private static JObject NewProfile(JObject template, string name)
        {
            var profile = new JObject();
            foreach (var key in new[] { "UseStrictJSIsolation", "GlobalBrightness", "GlobalBrightnessPreset", "UseProfileBrightness", "TestLedsGameData" })
            {
                if (template?[key] != null) profile[key] = template[key].DeepClone();
            }

            profile["CarChoices"] = new JArray();
            profile["EmbeddedJavascript"] = null;
            profile["LedContainers"] = new JArray();
            profile["Name"] = name;
            profile["ProfileId"] = Guid.NewGuid().ToString();
            profile["GameCode"] = null;
            profile["CarChoice"] = null;
            return profile;
        }

        private static string UniqueName(JArray profiles, string name)
        {
            var names = new HashSet<string>(profiles.OfType<JObject>().Select(p => (string)p["Name"]).Where(n => n != null));
            if (!names.Contains(name)) return name;
            for (var i = 2; ; i++)
            {
                var candidate = $"{name} ({i})";
                if (!names.Contains(candidate)) return candidate;
            }
        }

        /// <summary>The settings object of a channel (holding "Profiles" and "activeProfileId"), or null.</summary>
        private static JObject ChannelSettings(JObject deviceSettings, LedChannel channel) => LedRoot(deviceSettings)?[ChannelKey(channel)] as JObject;

        /// <summary>The profile shows <paramref name="part"/>: a FreeTheLights effect of the device whose part is this part.</summary>
        public static bool Shows(JObject profile, DeviceConfig device, DevicePart part) => Effects(profile).Any(e => IsFor(e, device, part));

        /// <summary>Adds the FreeTheLights effect for <paramref name="part"/> on top of the profile; false if it is already there.</summary>
        public static bool AddEffect(JObject profile, DeviceConfig device, DevicePart part)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (Shows(profile, device, part)) return false;
            if (!(profile["LedContainers"] is JArray containers))
            {
                containers = new JArray();
                profile["LedContainers"] = containers;
            }

            containers.Insert(0, new JObject
            {
                ["ContainerType"] = StripEffectType,
                ["DeviceId"] = device.Id.ToString(),
                ["PartId"] = part.Id.ToString(),
            });
            return true;
        }

        /// <summary>The profile has a FreeTheLights effect, but none for <paramref name="part"/> (another or a deleted device).</summary>
        public static bool ShowsOther(JObject profile, DeviceConfig device, DevicePart part) => Effects(profile).Any() && !Shows(profile, device, part);

        /// <summary>
        /// Points the first FreeTheLights effect of the profile at <paramref name="part"/> (keeping its place in the
        /// profile); false when the profile has no FreeTheLights effect.
        /// </summary>
        public static bool RetargetEffect(JObject profile, DeviceConfig device, DevicePart part)
        {
            var effect = Effects(profile).FirstOrDefault(e => (string)e["ContainerType"] == StripEffectType);
            if (effect == null) return false;
            effect["DeviceId"] = device.Id.ToString();
            effect["PartId"] = part.Id.ToString();
            return true;
        }

        /// <summary>Where a profile lives: the channel whose profile list has <paramref name="profileId"/>, or null.</summary>
        public static LedChannel? ChannelOfProfile(JObject deviceSettings, Guid profileId)
        {
            var ledRoot = LedRoot(deviceSettings);
            if (ledRoot == null || profileId == Guid.Empty) return null;
            foreach (LedChannel channel in Enum.GetValues(typeof(LedChannel)))
            {
                if (!(ledRoot[ChannelKey(channel)] is JObject channelSettings) || !(channelSettings["Profiles"] is JArray profiles)) continue;
                if (profiles.OfType<JObject>().Any(p => Guid.TryParse((string)p["ProfileId"], out var id) && id == profileId)) return channel;
            }

            return null;
        }

        /// <summary>
        /// The FreeTheLights device made from SimHub device type <paramref name="deviceTypeId"/>, and its part for
        /// <paramref name="channel"/> (null part when the device has no part for the channel).
        /// </summary>
        public static (DeviceConfig device, DevicePart part) FindPartFor(IEnumerable<DeviceConfig> devices, string deviceTypeId, LedChannel channel)
        {
            var device = devices?.FirstOrDefault(d => d?.Preset != null && string.Equals(d.Preset.DeviceTypeId, deviceTypeId?.Trim(), StringComparison.OrdinalIgnoreCase));
            return (device, device?.Parts.FirstOrDefault(p => p.Channel == channel));
        }

        /// <summary>
        /// The object holding the channels ("leds", "buttons", ...). It accepts both shapes of a device's settings:
        /// <list type="bullet">
        /// <item>a saved file or .shdevice: { "Settings": { "LEDS": { "leds": ... } } }, or "Settings" holding the channels directly;</item>
        /// <item>DeviceInstance.GetSettings at runtime: the content of "Settings" only, e.g. { "LEDS": { "leds": ... }, "LCD": ... }
        /// for a composite device (one key per component) or { "buttons": ... }.</item>
        /// </list>
        /// </summary>
        private static JObject LedRoot(JObject deviceSettings)
        {
            var settings = deviceSettings?["Settings"] as JObject ?? deviceSettings;
            return settings?[LedComponentKey] as JObject ?? settings;
        }

        private const string LedComponentKey = "LEDS";

        /// <summary>
        /// The component of a composite device that holds its LED channels, in the settings DeviceInstance.GetSettings
        /// returns ({ "LEDS": ..., "LCD": ... }: "LEDS"); null for a device that isn't composite ({ "buttons": ... }).
        /// Settings for SimHub go to this component only: SimHub merges a display's settings into the ones it has, which
        /// appends to every list there (the dash playlist's shortcuts doubled with each write).
        /// </summary>
        public static string LedComponent(JObject runtimeSettings) => runtimeSettings?[LedComponentKey] is JObject ? LedComponentKey : null;

        /// <summary>The settings key of a channel ("leds", "buttons", ...).</summary>
        public static string ChannelKey(LedChannel channel)
        {
            switch (channel)
            {
                case LedChannel.Buttons: return "buttons";
                case LedChannel.Encoders: return "encoders";
                case LedChannel.Matrix: return "matrix";
                default: return "leds";
            }
        }

        /// <summary>All FreeTheLights effects of a profile, including those nested in groups.</summary>
        private static IEnumerable<JObject> Effects(JObject profile)
        {
            var stack = new Stack<JArray>();
            if (profile?["LedContainers"] is JArray root) stack.Push(root);
            while (stack.Count > 0)
            {
                foreach (var container in stack.Pop().OfType<JObject>())
                {
                    var type = (string)container["ContainerType"];
                    if (type == StripEffectType || type == MatrixEffectType) yield return container;
                    if (container["LedContainers"] is JArray nested) stack.Push(nested);
                }
            }
        }

        /// <summary>Same device, and the effect's part is this part (an effect without a part shows the first part).</summary>
        private static bool IsFor(JObject effect, DeviceConfig device, DevicePart part)
        {
            if (!Guid.TryParse((string)effect["DeviceId"], out var deviceId) || deviceId != device.Id) return false;
            Guid.TryParse((string)effect["PartId"], out var partId);
            return device.FindPart(partId) == part;
        }
    }
}
