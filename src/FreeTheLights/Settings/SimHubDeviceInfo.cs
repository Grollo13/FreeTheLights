using System;
using System.Collections.Generic;
using System.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// What SimHub knows about one of its devices, read from its .shdevice defaults (and, at runtime, from SimHub's
    /// device registry). Plain data without SimHub types, so it can be tested without SimHub.
    /// </summary>
    public class SimHubDeviceInfo
    {
        /// <summary>SimHub's device type id (the name of the .shdevice file).</summary>
        public string DeviceTypeId { get; set; }

        /// <summary>SimHub's product name, e.g. "Ascher Racing Artura GT4".</summary>
        public string Name { get; set; }

        /// <summary>Manufacturer from SimHub's device registry; the first word of the name when unknown.</summary>
        public string Brand { get; set; }

        /// <summary>The device has a screen (SimHub has display settings for it).</summary>
        public bool HasDisplay { get; set; }

        /// <summary>
        /// LED count per SimHub channel. A channel the device has but whose count SimHub doesn't state has count 0.
        /// </summary>
        public Dictionary<LedChannel, int> Channels { get; } = new Dictionary<LedChannel, int>();

        /// <summary>How the LEDs of the <see cref="LedChannel.Leds"/> channel likely split into side and top LEDs.</summary>
        public LedSplit SuggestedSplit { get; set; }

        /// <summary>Name of SimHub's button layout (a DefaultColorsEditor control) for this device, or null.</summary>
        public string ButtonLayoutName { get; set; }

        /// <summary>Product picture relative to the SimHub folder, or null.</summary>
        public string ProductImageFile { get; set; }

        public bool HasButtonLayout => !string.IsNullOrEmpty(ButtonLayoutName);

        public int TotalLeds => Channels.Values.Sum();

        /// <summary>The LED count of a channel, 0 when the device doesn't have it or the count is unknown.</summary>
        public int CountOf(LedChannel channel) => Channels.TryGetValue(channel, out var count) ? count : 0;

        public bool Has(LedChannel channel) => Channels.ContainsKey(channel);

        /// <summary>"21 shift lights · 14 button LEDs" style description of the channels.</summary>
        public string ChannelSummary
        {
            get
            {
                var parts = new List<string>();
                foreach (var channel in Channels.Keys.OrderBy(c => c))
                {
                    var count = Channels[channel];
                    var what = channel == LedChannel.Leds ? "LEDs" : channel == LedChannel.Buttons ? "button LEDs" : channel == LedChannel.Encoders ? "encoder LEDs" : "matrix";
                    parts.Add(count > 0 ? $"{count} {what}" : what);
                }

                return parts.Count == 0 ? "no LEDs" : string.Join(" · ", parts);
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>A split of a LED strip into left side LEDs, a top row and right side LEDs (in wiring order).</summary>
    public class LedSplit
    {
        public LedSplit(int left, int top, int right, bool confident)
        {
            Left = Math.Max(0, left);
            Top = Math.Max(0, top);
            Right = Math.Max(0, right);
            Confident = confident;
        }

        public int Left { get; }

        public int Top { get; }

        public int Right { get; }

        /// <summary>The default profile's RPM effects agreed with each other and the sides came out symmetric.</summary>
        public bool Confident { get; }

        public int Total => Left + Top + Right;

        public override string ToString() => $"{Left}/{Top}/{Right}";
    }
}
