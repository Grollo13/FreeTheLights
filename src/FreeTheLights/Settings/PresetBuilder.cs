using System;
using System.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Creates a device from a SimHub device and the kind the user chose: one part per SimHub LED channel. The layout
    /// is copied, so later SimHub updates don't change the device (see <see cref="SimHubPreset"/>).
    /// </summary>
    public static class PresetBuilder
    {
        /// <summary>Markers at least this large are dials (rings around rotaries), at most this small are lights.</summary>
        public const double DialSize = 70;

        public const double LightSize = 42;

        /// <summary>
        /// The device for <paramref name="info"/> as <paramref name="kind"/>. <paramref name="layout"/> is SimHub's
        /// button layout (null when there is none); <paramref name="split"/> overrides the suggested split of the
        /// LEDs channel (dashboards: left side / top row / right side). <paramref name="photo"/> are a dashboard's LED
        /// positions on its product picture (see <see cref="DashPhotoLayouts"/>); when found, their split wins.
        /// </summary>
        public static DeviceConfig Build(SimHubDeviceInfo info, DeviceType kind, SimHubButtonLayout layout = null, LedSplit split = null, DashPhotoResult photo = null)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));
            var dashPhoto = kind == DeviceType.Dashboard && layout == null && photo?.PictureFile != null ? photo : null;
            if (dashPhoto?.Layout != null && split == null) split = dashPhoto.Layout.Split;
            var device = new DeviceConfig
            {
                Name = info.Name,
                Type = kind,
                Preset = new SimHubPreset
                {
                    DeviceTypeId = info.DeviceTypeId,
                    ProductName = info.Name,
                    ProductImageFile = info.ProductImageFile,
                    PhotoFile = layout?.PhotoFile ?? dashPhoto?.PictureFile,
                    CanvasWidth = layout?.CanvasWidth ?? dashPhoto?.PictureWidth ?? 0,
                    CanvasHeight = layout?.CanvasHeight ?? dashPhoto?.PictureHeight ?? 0,
                    LedPositions = dashPhoto?.Source ?? PhotoLedSource.None,
                },
            };

            foreach (var channel in info.Channels.Keys.OrderBy(c => c))
            {
                var part = BuildPart(info, channel, kind, layout, split);
                if (part == null) continue;
                if (channel == LedChannel.Leds && dashPhoto?.Layout != null) DashPhotoLayouts.Apply(part.Groups, dashPhoto.Layout);
                device.Parts.Add(part);
            }

            device.EnsureDefaults();
            return device;
        }

        /// <summary>
        /// What <see cref="Refresh"/> would remove: button elements whose LED number SimHub doesn't list any more,
        /// parts of channels SimHub doesn't list any more, and parts whose LED count changed (their groups are replaced).
        /// </summary>
        public static int CountRemovedByRefresh(DeviceConfig device, DeviceConfig fresh)
        {
            var removed = 0;
            foreach (var part in device.Parts)
            {
                var update = fresh.Parts.FirstOrDefault(p => p.Channel == part.Channel);
                if (update == null) removed++;
                else if (IsSimHubLayout(update)) removed += part.Elements.Count(e => e.HasPhotoPosition && update.Elements.All(u => u.ElementSlot != e.ElementSlot));
                else if (update.TotalLeds != part.TotalLeds) removed++;
            }

            return removed;
        }

        /// <summary>
        /// Updates a device made from a SimHub preset to SimHub's current data (<paramref name="fresh"/>, built with
        /// <see cref="Build"/>): button elements keep their name, type, static color and effect and get SimHub's
        /// position; new LED numbers are added and vanished ones removed. Other parts keep their layout while their LED
        /// count is unchanged, else they get SimHub's (suggested) layout; a dashboard keeps its LED positions on the
        /// picture while its LEDs part keeps its layout. Returns a short description of the changes.
        /// </summary>
        public static string Refresh(DeviceConfig device, DeviceConfig fresh)
        {
            var changes = new System.Collections.Generic.List<string>();
            var old = device.Preset;
            var keepsPositions = old != null && device.Parts.Any(p => p.Channel == LedChannel.Leds && fresh.Parts.Any(f => f.Channel == LedChannel.Leds && !IsSimHubLayout(f) && f.TotalLeds == p.TotalLeds));
            device.Preset = fresh.Preset;
            if (keepsPositions && old.PhotoFile == null && fresh.Preset?.PhotoFile != null && device.Type == DeviceType.Dashboard)
            {
                // Positions on the picture are new for this device: its kept groups take them over.
                var target = device.Parts.First(p => p.Channel == LedChannel.Leds);
                CopyPhotoLeds(target, fresh.Parts.First(p => p.Channel == LedChannel.Leds));
                changes.Add("LED positions on the picture added");
            }
            else if (keepsPositions && fresh.Preset != null && device.Type == DeviceType.Dashboard)
            {
                fresh.Preset.PhotoFile = old.PhotoFile;
                fresh.Preset.CanvasWidth = old.CanvasWidth;
                fresh.Preset.CanvasHeight = old.CanvasHeight;
                fresh.Preset.LedPositions = old.LedPositions;
            }

            foreach (var part in device.Parts.ToList())
            {
                if (fresh.Parts.All(p => p.Channel != part.Channel))
                {
                    device.Parts.Remove(part);
                    changes.Add($"removed {part.Name}");
                }
            }

            foreach (var update in fresh.Parts)
            {
                var part = device.Parts.FirstOrDefault(p => p.Channel == update.Channel);
                if (part == null)
                {
                    device.Parts.Add(update);
                    changes.Add($"added {update.Name}");
                }
                else if (IsSimHubLayout(update))
                {
                    var (added, removed, moved) = MergeElements(part, update);
                    if (added + removed + moved > 0) changes.Add($"{part.Name}: {added} added, {removed} removed, {moved} moved");
                }
                else if (update.TotalLeds != part.TotalLeds)
                {
                    foreach (var group in part.Groups.ToList()) part.RemoveGroup(group);
                    foreach (var group in update.Groups) part.AddGroup(group.Clone());
                    changes.Add($"{part.Name}: new layout with {part.TotalLeds} LEDs");
                }
            }

            device.EnsureDefaults();
            return changes.Count == 0 ? "Already up to date with SimHub." : "Updated from SimHub: " + string.Join("; ", changes) + ".";
        }

        private static bool IsSimHubLayout(DevicePart part) => part.Elements.Any(e => e.HasPhotoPosition);

        /// <summary>Copies the picture positions of <paramref name="source"/>'s rows and columns onto the matching groups (same kind and side, in order).</summary>
        private static void CopyPhotoLeds(DevicePart target, DevicePart source)
        {
            var available = source.Groups.Where(g => g.PhotoLeds.Count > 0).ToList();
            foreach (var group in target.Groups.Where(g => g.Kind == LedGroupKind.HorizontalRow || g.Kind == LedGroupKind.VerticalColumn))
            {
                var match = available.FirstOrDefault(g => g.Kind == group.Kind && g.Side == group.Side);
                if (match == null) continue;
                available.Remove(match);
                group.PhotoLeds = DashPhotoLayouts.Resample(match.PhotoLeds, group.Count);
            }
        }

        private static (int added, int removed, int moved) MergeElements(DevicePart part, DevicePart update)
        {
            int added = 0, removed = 0, moved = 0;
            foreach (var element in part.Elements.Where(e => e.HasPhotoPosition).ToList())
            {
                if (update.Elements.Any(u => u.ElementSlot == element.ElementSlot)) continue;
                part.RemoveGroup(element);
                removed++;
            }

            foreach (var fresh in update.Elements)
            {
                var element = part.Elements.FirstOrDefault(e => e.HasPhotoPosition && e.ElementSlot == fresh.ElementSlot);
                if (element == null)
                {
                    var copy = fresh.Clone();
                    part.AddGroup(copy);
                    copy.StartIndex = fresh.StartIndex;
                    added++;
                    continue;
                }

                if (element.PhotoX != fresh.PhotoX || element.PhotoY != fresh.PhotoY || element.PhotoSize != fresh.PhotoSize || element.StartIndex != fresh.StartIndex) moved++;
                element.PhotoX = fresh.PhotoX;
                element.PhotoY = fresh.PhotoY;
                element.PhotoSize = fresh.PhotoSize;
                element.StartIndex = fresh.StartIndex;
            }

            return (added, removed, moved);
        }

        /// <summary>Display name of a channel's part.</summary>
        public static string PartName(LedChannel channel, DeviceType kind)
        {
            switch (channel)
            {
                case LedChannel.Buttons: return "Buttons";
                case LedChannel.Encoders: return "Encoders";
                case LedChannel.Matrix: return "LED flag";
                default: return kind == DeviceType.Wheel ? "RPM LEDs" : kind == DeviceType.Dashboard ? "RPM and side LEDs" : "LEDs";
            }
        }

        private static DevicePart BuildPart(SimHubDeviceInfo info, LedChannel channel, DeviceType kind, SimHubButtonLayout layout, LedSplit split)
        {
            var count = info.CountOf(channel);
            var part = new DevicePart { Name = PartName(channel, kind), Channel = channel };
            part.EnsureDefaults();

            if (channel == LedChannel.Buttons && layout != null && layout.Markers.Any(m => m.Visible))
            {
                AddElements(part, layout);
                return part;
            }

            if (channel == LedChannel.Matrix || (kind == DeviceType.LedFlag && channel == LedChannel.Leds))
            {
                // SimHub doesn't state a matrix' size: 8x8, or the LED count as a near-square matrix.
                var columns = count > 0 ? (int)Math.Ceiling(Math.Sqrt(count)) : 8;
                var rows = count > 0 ? (int)Math.Ceiling(count / (double)columns) : 8;
                part.AddGroup(new LedGroup { Name = "LED flag", Kind = LedGroupKind.Matrix, Columns = columns, Rows = rows });
                return part;
            }

            if (count <= 0) count = 1;
            if (channel == LedChannel.Leds && (kind == DeviceType.Wheel || kind == DeviceType.Dashboard))
            {
                var s = split ?? info.SuggestedSplit ?? new LedSplit(0, count, 0, false);
                if (s.Total != count) s = new LedSplit(0, count, 0, false);
                if (s.Left > 0) part.AddGroup(new LedGroup { Name = "Left side", Kind = LedGroupKind.VerticalColumn, Count = s.Left, Side = LedGroupSide.Left });
                if (s.Top > 0) part.AddGroup(new LedGroup { Name = "RPM LEDs", Kind = LedGroupKind.HorizontalRow, Count = s.Top });
                // Dashboards chain their LEDs around the display: up the left side, along the top, down the right side.
                if (s.Right > 0) part.AddGroup(new LedGroup { Name = "Right side", Kind = LedGroupKind.VerticalColumn, Count = s.Right, Side = LedGroupSide.Right, Reversed = kind == DeviceType.Dashboard });
                return part;
            }

            var name = channel == LedChannel.Buttons ? "Button LEDs" : channel == LedChannel.Encoders ? "Encoder LEDs" : "LEDs";
            part.AddGroup(new LedGroup { Name = name, Kind = LedGroupKind.HorizontalRow, Count = count });
            return part;
        }

        /// <summary>
        /// One element per visible marker, at its LED number and photo position. Dials sit in the center; buttons and
        /// lights belong to the side of the wheel they are on.
        /// </summary>
        private static void AddElements(DevicePart part, SimHubButtonLayout layout)
        {
            var center = layout.CanvasWidth / 2;
            foreach (var marker in layout.Markers.Where(m => m.Visible && m.LedNumber >= 1).OrderBy(m => m.LedNumber))
            {
                var type = marker.Size >= DialSize ? ElementType.Dial : marker.Size <= LightSize ? ElementType.Light : ElementType.Button;
                var element = new LedGroup
                {
                    Kind = LedGroupKind.Element,
                    ElementType = type,
                    Side = type == ElementType.Dial ? LedGroupSide.None : marker.X < center ? LedGroupSide.Left : LedGroupSide.Right,
                    ElementSlot = marker.LedNumber - 1,
                    Name = $"{type} {marker.LedNumber}",
                    PhotoX = marker.X,
                    PhotoY = marker.Y,
                    PhotoSize = Math.Max(1, marker.Size),
                };
                part.AddGroup(element);
                element.StartIndex = marker.LedNumber - 1;
            }

            part.EnsureDefaults();
        }
    }
}
