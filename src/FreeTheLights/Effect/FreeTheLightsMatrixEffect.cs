using System;
using System.Collections.Generic;
using System.Windows.Controls;
using FreeTheLights.Settings;
using Newtonsoft.Json;
using SimHub.Plugins.DataPlugins.RGBDriverCommon;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver.MatrixContainers.Attributes;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver.MatrixContainers.Base;
using SimHub.Plugins.DataPlugins.RGBMatrixDriver.Settings;

namespace FreeTheLights.Effect
{
    /// <summary>
    /// Matrix editor effect for LED flag devices: shows one matrix group of a FreeTheLights device's part,
    /// scaled (nearest neighbour) to the physical matrix size.
    /// </summary>
    [ContainerMetadata(5, "FreeTheLights", "Shows a FreeTheLights matrix group (flags, proximity, ABS, TC)", "Effects", false, DeviceKind.Any)]
    public class FreeTheLightsMatrixEffect : MatrixContainerBase, IFreeTheLightsEffect
    {
        public Guid DeviceId { get; set; }

        /// <summary>Part (LED channel) whose matrix group is shown; empty or unknown = the device's first part.</summary>
        public Guid PartId { get; set; }

        /// <summary>Matrix group to show; empty or unknown = first matrix group of the device.</summary>
        public Guid MatrixGroupId { get; set; }

        [JsonIgnore]
        public bool IsMatrixEffect => true;

        [JsonIgnore]
        public override bool IsAllowedOutOfGame => true;

        /// <summary>Matrix profiles are not looked up; the device is picked in the editor.</summary>
        [JsonIgnore]
        public Guid ParentProfileId => Guid.Empty;

        public override void LoadDefaultSettings(DeviceKind deviceKind)
        {
            DeviceId = Guid.Empty;
            PartId = Guid.Empty;
        }

        protected override void SetDeviceKindInternal(DeviceKind deviceKind)
        {
        }

        public override MultiMatrixResult SetResultBase(LedsGameData data, MultiMatrixResult result, List<RGBMatrixProfile> includedFrom)
        {
            var plugin = FreeTheLightsPlugin.Instance;
            try
            {
                // FindPart returns the render copy of the settings, which the UI never modifies.
                var part = plugin?.FindPart(DeviceId, PartId);
                var group = part?.FindMatrixGroup(MatrixGroupId);
                var frame = part == null ? null : plugin.GetFrame(part.Id);

                var width = GetColumns(DeviceKind);
                var height = GetRows(DeviceKind);
                if (width <= 0 || height <= 0)
                {
                    width = group?.Columns ?? 8;
                    height = group?.Rows ?? 8;
                }

                var matrix = result.GetMatrixResult(0, true);
                matrix.Clear(width, height);
                if (group == null || frame == null) return result;

                for (var y = 0; y < height; y++)
                {
                    var sy = y * group.Rows / height;
                    for (var x = 0; x < width; x++)
                    {
                        var sx = x * group.Columns / width;
                        var index = group.PhysicalIndex(sx, sy);
                        if (index >= frame.Length || frame[index].IsTransparent) continue;
                        matrix.SetPixel(x, y, EffectHelper.ToDrawing(frame[index]));
                    }
                }
            }
            catch (Exception ex)
            {
                // This runs inside SimHub's LED driver: never let an exception escape.
                plugin?.LogErrorThrottled("FreeTheLights: error in the LED flag effect", ex);
            }

            return result;
        }

        [JsonIgnore]
        public override UserControl MainLedsEditor => new EffectEditor { DataContext = new EffectEditorViewModel(this) };

        protected override bool IsActive(LedsGameData data) => true;

        protected override UserControl GetEditControl() => null;
    }
}
