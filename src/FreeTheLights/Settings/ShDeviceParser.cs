using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Reads SimHub's .shdevice defaults (plain JSON): the device's name and id, its LED channels and a suggested
    /// split of the RPM/side LEDs. The JSON is SimHub's internal format, so everything is read defensively.
    /// </summary>
    public static class ShDeviceParser
    {
        /// <summary>Name prefix of SimHub's button layout controls.</summary>
        public const string LayoutPrefix = "DefaultColorsEditor";

        private static readonly (string key, LedChannel channel)[] ChannelKeys =
        {
            ("leds", LedChannel.Leds),
            ("buttons", LedChannel.Buttons),
            ("encoders", LedChannel.Encoders),
            ("matrix", LedChannel.Matrix),
        };

        /// <summary>Parses a .shdevice file's content; null when it isn't a device with an id and a name.</summary>
        public static SimHubDeviceInfo Parse(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception)
            {
                return null;
            }

            return Parse(root);
        }

        public static SimHubDeviceInfo Parse(JObject root)
        {
            var id = (string)root?["DeviceTypeID"];
            var name = (string)root?["DeviceTypeName"];
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) return null;

            var info = new SimHubDeviceInfo { DeviceTypeId = id.Trim().ToUpperInvariant(), Name = name.Trim(), Brand = FirstWord(name) };
            var settings = root["Settings"] as JObject;
            if (settings == null) return info;

            info.HasDisplay = settings["LCD"] is JObject || settings["display"] is JObject;

            // Most devices keep their LED channels under "LEDS"; some keep them directly in the settings.
            var ledRoot = settings["LEDS"] as JObject ?? settings;
            foreach (var (key, channel) in ChannelKeys)
            {
                if (!(ledRoot[key] is JObject channelSettings)) continue;
                var count = (int?)channelSettings["ForcedLedCount"] ?? 0;
                if (count <= 0) count = HighestUsedLed(ActiveProfile(channelSettings));
                info.Channels[channel] = Math.Max(0, count);
            }

            if (info.CountOf(LedChannel.Leds) > 0 && ledRoot["leds"] is JObject leds)
            {
                info.SuggestedSplit = SuggestSplit(info.CountOf(LedChannel.Leds), ActiveProfile(leds));
            }

            return info;
        }

        /// <summary>
        /// Splits <paramref name="total"/> LEDs into left side / top row / right side from the RPM effects of SimHub's
        /// default profile: the RPM LEDs are the top row, LEDs before them the left side, LEDs after them the right side.
        /// Without RPM effects all LEDs form the top row.
        /// </summary>
        public static LedSplit SuggestSplit(int total, JObject profile)
        {
            var rpm = Containers(profile?["LedContainers"] as JArray)
                .Where(c => ((string)c["ContainerType"] ?? string.Empty).IndexOf("RPM", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(c => (start: (int?)c["StartPosition"] ?? 0, count: (int?)c["LedCount"] ?? 0))
                .Where(r => r.start >= 1 && r.start <= total)
                .ToList();
            if (rpm.Count == 0) return new LedSplit(0, total, 0, false);

            var start = rpm.Min(r => r.start);
            var counted = rpm.Where(r => r.count > 0).ToList();
            int end;
            bool consistent;
            if (counted.Count > 0)
            {
                end = counted.Max(r => r.start + r.count - 1);
                consistent = end <= total;
            }
            else
            {
                // No counts: assume the side LEDs are symmetric.
                end = total - (start - 1);
                consistent = false;
            }

            if (end < start || end > total) return new LedSplit(0, total, 0, false);
            var left = start - 1;
            var right = total - end;
            return new LedSplit(left, end - start + 1, right, consistent && Math.Abs(left - right) <= 1);
        }

        /// <summary>
        /// The layout whose name contains all of the device name's words in order (shortest match), e.g.
        /// "Ascher Racing Artura GT4" → DefaultColorsEditorAscherRacingMcLarenArturaGT4. Fallback when SimHub's
        /// registry doesn't link the device to a layout.
        /// </summary>
        public static string MatchLayoutName(string deviceName, IEnumerable<string> layoutNames)
        {
            var tokens = Regex.Matches((deviceName ?? string.Empty).ToLowerInvariant(), "[a-z0-9]+").Cast<Match>().Select(m => m.Value).ToList();
            if (tokens.Count == 0) return null;
            string best = null;
            foreach (var layout in layoutNames)
            {
                var target = Regex.Replace(layout.Substring(Math.Min(LayoutPrefix.Length, layout.Length)).ToLowerInvariant(), "[^a-z0-9]", string.Empty);
                var position = 0;
                var matches = true;
                foreach (var token in tokens)
                {
                    var at = target.IndexOf(token, position, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        matches = false;
                        break;
                    }

                    position = at + token.Length;
                }

                if (matches && (best == null || layout.Length < best.Length)) best = layout;
            }

            return best;
        }

        /// <summary>The channel's active LED profile, or its first profile.</summary>
        private static JObject ActiveProfile(JObject channel)
        {
            var profiles = channel?["Profiles"] as JArray;
            if (profiles == null) return null;
            var activeId = (string)channel["activeProfileId"];
            return profiles.OfType<JObject>().FirstOrDefault(p => (string)p["ProfileId"] == activeId) ?? profiles.OfType<JObject>().FirstOrDefault();
        }

        /// <summary>The highest LED any effect of the profile drives (an estimate when SimHub doesn't state the count).</summary>
        private static int HighestUsedLed(JObject profile)
        {
            return Containers(profile?["LedContainers"] as JArray)
                .Select(c => ((int?)c["StartPosition"] ?? 0) + Math.Max(1, (int?)c["LedCount"] ?? 1) - 1)
                .DefaultIfEmpty(0)
                .Max();
        }

        /// <summary>All effects of a profile, including those nested in groups.</summary>
        private static IEnumerable<JObject> Containers(JArray containers)
        {
            if (containers == null) yield break;
            foreach (var container in containers.OfType<JObject>())
            {
                yield return container;
                foreach (var nested in Containers(container["LedContainers"] as JArray)) yield return nested;
            }
        }

        private static string FirstWord(string name)
        {
            var trimmed = name.Trim();
            var space = trimmed.IndexOf(' ');
            return space > 0 ? trimmed.Substring(0, space) : trimmed;
        }
    }
}
