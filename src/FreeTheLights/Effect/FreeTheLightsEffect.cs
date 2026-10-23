using System;
using System.Collections.Generic;
using System.Windows.Controls;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;
using Newtonsoft.Json;
using SimHub.Plugins.DataPlugins.RGBDriver;
using SimHub.Plugins.DataPlugins.RGBDriver.LedsContainers.Attributes;
using SimHub.Plugins.DataPlugins.RGBDriver.LedsContainers.Base;
using SimHub.Plugins.DataPlugins.RGBDriver.Settings;
using SimHub.Plugins.DataPlugins.RGBDriverCommon;

namespace FreeTheLights.Effect
{
    /// <summary>
    /// LED editor effect for LED strip devices (wheels, dashes, LED bars): outputs the frame of one part of a
    /// FreeTheLights device. Add it to the LED profile of each SimHub channel (shift lights, buttons, ...) and select
    /// the FreeTheLights device and the part for that channel.
    /// LEDs that FreeTheLights does not drive stay transparent, so effects below it remain visible.
    /// </summary>
    [ContainerMetadata(5, "FreeTheLights", "Shows a FreeTheLights device (RPM, flags, proximity, ABS, TC)", "Effects")]
    public class FreeTheLightsEffect : LedsContainerBase, IFreeTheLightsEffect
    {
        public Guid DeviceId { get; set; }

        /// <summary>Part (LED channel) to show; empty or unknown = the device's first part.</summary>
        public Guid PartId { get; set; }

        [JsonIgnore]
        public Guid MatrixGroupId { get; set; }

        [JsonIgnore]
        public bool IsMatrixEffect => false;

        /// <summary>Id of the SimHub LED profile this effect is in; tells which SimHub device and channel it drives.</summary>
        [JsonIgnore]
        public Guid ParentProfileId { get; private set; }

        [JsonIgnore]
        public override bool IsAllowedOutOfGame => true;

        public override void LoadDefaultSettings()
        {
            // No default device: the right one depends on the SimHub device this effect is added to (see SetParentProfile).
            DeviceId = Guid.Empty;
            PartId = Guid.Empty;
        }

        public override void SetParentProfile(Profile profile)
        {
            base.SetParentProfile(profile);
            ParentProfileId = profile?.ProfileId ?? Guid.Empty;
            if (DeviceId == Guid.Empty) EffectHelper.AssignMatchingPart(this);
        }

        public override LedResult SetResultBase(LedsGameData data, LedResult result, List<Profile> includedFrom)
        {
            var plugin = FreeTheLightsPlugin.Instance;
            try
            {
                // FindPart returns the render copy of the settings, which the UI never modifies.
                var part = plugin?.FindPart(DeviceId, PartId);
                var frame = part == null ? null : plugin.GetFrame(part.Id);
                result.Clear(frame?.Length ?? part?.TotalLeds ?? 0);
                if (frame == null) return result;

                for (var i = 0; i < frame.Length; i++)
                {
                    var c = frame[i];
                    if (!c.IsTransparent) result[i] = EffectHelper.ToDrawing(c);
                }
            }
            catch (Exception ex)
            {
                // This runs inside SimHub's LED driver: never let an exception escape.
                plugin?.LogErrorThrottled("FreeTheLights: error in the LED strip effect", ex);
            }

            return result;
        }

        [JsonIgnore]
        public override Control MainLedsEditor => new EffectEditor { DataContext = new EffectEditorViewModel(this) };

        protected override bool IsActive(LedsGameData data) => true;

        protected override UserControl GetEditControl() => null;
    }

    /// <summary>Common surface of the strip and matrix effects for the shared editor.</summary>
    public interface IFreeTheLightsEffect
    {
        Guid DeviceId { get; set; }

        Guid PartId { get; set; }

        Guid MatrixGroupId { get; set; }

        bool IsMatrixEffect { get; }

        /// <summary>Id of the SimHub LED profile the effect is in, or empty when unknown.</summary>
        Guid ParentProfileId { get; }
    }

    internal static class EffectHelper
    {
        /// <summary>
        /// Points an effect at the FreeTheLights part made for the SimHub device and channel of its LED profile, if there
        /// is one. Never throws: this runs while SimHub loads or edits LED profiles, on SimHub's threads, so it reads the
        /// render copies of the devices, not the live settings the UI edits.
        /// </summary>
        public static void AssignMatchingPart(IFreeTheLightsEffect effect)
        {
            try
            {
                var plugin = FreeTheLightsPlugin.Instance;
                var owner = SimHubConnector.FindOwner(plugin?.PluginManager, effect.ParentProfileId);
                if (owner == null) return;
                var (device, part) = SimHubProfiles.FindPartFor(plugin.RenderDevices.Devices, owner.DeviceTypeId, owner.Channel);
                if (device == null || part == null) return;
                effect.DeviceId = device.Id;
                effect.PartId = part.Id;
            }
            catch (Exception ex)
            {
                FreeTheLightsPlugin.Instance?.LogErrorThrottled("FreeTheLights: could not find the device of a new effect", ex);
            }
        }

        public static System.Drawing.Color ToDrawing(LedColor c) => System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
    }
}
