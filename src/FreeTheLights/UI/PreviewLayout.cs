using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;

namespace FreeTheLights.UI
{
    /// <summary>
    /// Where <see cref="LedPreview"/> draws a device's LEDs, labels and backdrop (see <see cref="Build"/>). Only geometry:
    /// colors, brushes and drawing stay in the preview, so layouts can be checked without rendering.
    /// </summary>
    public sealed class PreviewLayout
    {
        private const double Pitch = 16;
        private const double Radius = 6;
        private const double GroupGap = 18;
        private const double LabelHeight = 14;

        /// <summary>Minimum horizontal room per side/center group so its name fits.</summary>
        private const double LabelSlot = 80;

        /// <summary>Grid unit of the front plate layout (distance between neighbouring elements).</summary>
        private const double PlateUnit = 26;

        /// <summary>Elements placed from a SimHub photo use one grid unit per this many canvas units.</summary>
        private const double CanvasPerPlateUnit = 62;

        // ---------- GT wheel ----------
        private const double WheelPad = 16;
        private const double WheelGap = 14;
        private const double ColumnGap = 6;
        private const double MinPlateWidth = 240;
        private const double DisplayWidth = 120;
        private const double DisplayHeight = 50;
        private const double GripWidth = 46;
        private const double ThumbWidth = 34;
        private const double TopBar = 24;
        private const double BottomBar = 36;

        /// <summary>Corner radius of the wheel body: large, so the grips round off into the top and bottom bars.</summary>
        private const double BodyRadius = 58;

        /// <summary>The plate is at least this much wider than tall, so the wheel keeps a GT wheel's wide shape.</summary>
        private const double PlateAspect = 1.25;

        // ---------- Photo and dashboard ----------
        private const double PhotoWidth = 560;
        private const double DashPad = 14;
        private const double MinScreenWidth = 200;
        private const double MinScreenHeight = 110;

        /// <summary>Loaded photos by path (null = could not be loaded); photos are frozen and shared.</summary>
        private static readonly Dictionary<string, BitmapSource> Photos = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        private readonly DeviceConfig device;
        private readonly List<LedSpot> spots = new List<LedSpot>();
        private readonly List<PreviewLabel> labels = new List<PreviewLabel>();

        private PreviewLayout(DeviceConfig device)
        {
            this.device = device;
        }

        /// <summary>A layout without LEDs.</summary>
        public static readonly PreviewLayout Empty = new PreviewLayout(null);

        /// <summary>Every LED, in drawing order.</summary>
        public IReadOnlyList<LedSpot> Spots => spots;

        /// <summary>Group names (schematic and the strip above a photo).</summary>
        public IReadOnlyList<PreviewLabel> Labels => labels;

        /// <summary>The shapes drawn behind the LEDs (wheel, dash or photo); null for the schematic.</summary>
        public PreviewBackdrop Backdrop { get; private set; }

        /// <summary>The size of everything drawn, from (0, 0).</summary>
        public Size Size { get; private set; }

        /// <summary>
        /// Lays out every LED of <paramref name="device"/>: a wheel made from a SimHub preset with a photo on its photo,
        /// other wheels on a generic GT wheel, a dashboard on its product picture or a generic dash, everything else as a
        /// labelled schematic (see <see cref="LedPreview"/>).
        /// </summary>
        public static PreviewLayout Build(DeviceConfig device)
        {
            var layout = new PreviewLayout(device);
            layout.BuildLayout();
            return layout;
        }

        /// <summary>Loads a photo from the SimHub folder once; null when it is missing or unreadable. UI thread only.</summary>
        public static BitmapSource LoadPhoto(string relativePath)
        {
            var path = SimHubPaths.Resolve(relativePath);
            if (path == null) return null;
            if (Photos.TryGetValue(path, out var cached)) return cached;

            BitmapSource photo = null;
            try
            {
                if (File.Exists(path))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(path);
                    image.EndInit();
                    image.Freeze();
                    photo = image;
                }
            }
            catch (Exception)
            {
                // A broken photo falls back to the generic wheel.
            }

            Photos[path] = photo;
            return photo;
        }

        private void BuildLayout()
        {
            var items = device?.Parts.SelectMany(p => p.Groups.Where(g => g != null).Select(g => new Item(p, g))).ToList() ?? new List<Item>();
            if (items.Count == 0)
            {
                Size = new Size(0, 0);
                return;
            }

            switch (device.Type)
            {
                case DeviceType.Wheel:
                    var photo = LoadPhoto(device.Preset?.PhotoFile);
                    if (photo != null && items.Any(i => i.Group.HasPhotoPosition)) BuildPhotoLayout(items, photo);
                    else BuildWheelLayout(items);
                    break;
                case DeviceType.Dashboard:
                    var picture = device.Preset?.ShowsLedsOnPhoto == true ? LoadPhoto(device.Preset.PhotoFile) : null;
                    if (picture != null && items.Any(i => i.Group.PhotoLeds.Count > 0)) BuildDashPhotoLayout(items, picture);
                    else BuildDashLayout(items);
                    break;
                default:
                    Size = BuildSchematic(items);
                    break;
            }
        }

        /// <summary>The labelled schematic, laid out from (0, 0); returns its size.</summary>
        private Size BuildSchematic(List<Item> items)
        {
            var rows = items.Where(i => i.Group.Kind == LedGroupKind.HorizontalRow).ToList();
            var leftColumns = items.Where(i => i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.Left).ToList();
            var rightColumns = items.Where(i => i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.Right).ToList();
            var center = items.Where(i => i.Group.Kind == LedGroupKind.Matrix || (i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.None)).ToList();
            var plate = PlatePositions(items.Where(i => i.Group.IsElement));
            var plateSize = PlateSize(plate);

            double SizeW(Item i) => i.Group.Width * Pitch;
            double SizeH(Item i) => i.Group.Height * Pitch + LabelHeight;
            double SlotW(Item i) => Math.Max(SizeW(i), LabelSlot);

            var leftWidth = leftColumns.Sum(i => SlotW(i) + GroupGap);
            var rightWidth = rightColumns.Sum(i => SlotW(i) + GroupGap);
            var centerWidth = Math.Max(0, center.Sum(i => SlotW(i) + GroupGap) + (plate.Count > 0 ? plateSize.Width + GroupGap : 0) - GroupGap);
            var rowsWidth = rows.Count == 0 ? 0 : rows.Max(SizeW);
            var middleWidth = Math.Max(centerWidth, rowsWidth);
            var totalWidth = Math.Max(leftWidth + middleWidth + rightWidth, 120);

            var y = 0.0;
            foreach (var row in rows)
            {
                AddGroup(row, leftWidth + (middleWidth - SizeW(row)) / 2, SizeW(row), TextAlignment.Left, y);
                y += SizeH(row) + 6;
            }

            var sideTop = y;
            var x = 0.0;
            foreach (var column in leftColumns)
            {
                AddGroup(column, x, SlotW(column), TextAlignment.Left, sideTop);
                x += SlotW(column) + GroupGap;
            }

            // Right columns hug the right edge, labels right-aligned above them.
            x = totalWidth - rightWidth + GroupGap;
            foreach (var column in rightColumns)
            {
                AddGroup(column, x, SlotW(column), TextAlignment.Right, sideTop);
                x += SlotW(column) + GroupGap;
            }

            x = leftWidth + (middleWidth - centerWidth) / 2;
            foreach (var group in center)
            {
                AddGroup(group, x, SlotW(group), TextAlignment.Center, sideTop);
                x += SlotW(group) + GroupGap;
            }

            if (plate.Count > 0) AddPlate(plate, x + plateSize.Width / 2, sideTop, "Front plate");

            var sideHeight = items.Where(i => i.Group.Kind != LedGroupKind.HorizontalRow && !i.Group.IsElement).Select(SizeH).DefaultIfEmpty(0).Max();
            sideHeight = Math.Max(sideHeight, plateSize.Height);
            return new Size(totalWidth, (rows.Count > 0 || sideHeight > 0 ? sideTop + sideHeight : 0) + 2);
        }

        /// <summary>
        /// A wheel from a SimHub preset: the button LEDs as rings at their places on SimHub's photo, every other LED
        /// (shift lights, ...) as a labelled strip above the photo.
        /// </summary>
        private void BuildPhotoLayout(List<Item> items, BitmapSource photo)
        {
            var canvasWidth = device.Preset.CanvasWidth > 0 ? device.Preset.CanvasWidth : 1000;
            var scale = PhotoWidth / canvasWidth;
            var photoHeight = PhotoWidth * photo.PixelHeight / Math.Max(1, photo.PixelWidth);

            // The strip: everything not placed on the photo, laid out as a schematic, centered above the photo.
            var firstSpot = spots.Count;
            var firstLabel = labels.Count;
            var stripItems = items.Where(i => !i.Group.HasPhotoPosition).ToList();
            var strip = stripItems.Count == 0 ? new Size(0, 0) : BuildSchematic(stripItems);
            var width = Math.Max(PhotoWidth, strip.Width);
            Shift(firstSpot, firstLabel, (width - strip.Width) / 2, 0);

            var photoTop = strip.Height > 0 ? strip.Height + 10 : 0;
            var photoRect = new Rect((width - PhotoWidth) / 2, photoTop, PhotoWidth, photoHeight);
            foreach (var item in items.Where(i => i.Group.HasPhotoPosition))
            {
                spots.Add(new LedSpot
                {
                    Part = item.Part,
                    Group = item.Group,
                    Physical = item.Group.PhysicalIndex(0),
                    Center = new Point(photoRect.Left + item.Group.PhotoX * scale, photoRect.Top + item.Group.PhotoY * scale),
                    Radius = Math.Max(4, item.Group.PhotoSize * scale / 2),
                    IsRing = true,
                });
            }

            Backdrop = new PreviewBackdrop { Photo = photo, PhotoRect = photoRect };
            Size = new Size(width, photoRect.Bottom);
        }

        /// <summary>
        /// A dashboard from a SimHub preset: its rows and columns at their places on SimHub's product picture (spread
        /// over the group's LED count if it changed), every other group as a labelled strip above the picture.
        /// </summary>
        private void BuildDashPhotoLayout(List<Item> items, BitmapSource picture)
        {
            var pictureWidth = device.Preset.CanvasWidth > 0 ? device.Preset.CanvasWidth : picture.PixelWidth;
            var scale = PhotoWidth / pictureWidth;
            var photoHeight = PhotoWidth * picture.PixelHeight / Math.Max(1, picture.PixelWidth);

            var firstSpot = spots.Count;
            var firstLabel = labels.Count;
            var stripItems = items.Where(i => i.Group.PhotoLeds.Count == 0).ToList();
            var strip = stripItems.Count == 0 ? new Size(0, 0) : BuildSchematic(stripItems);
            var width = Math.Max(PhotoWidth, strip.Width);
            Shift(firstSpot, firstLabel, (width - strip.Width) / 2, 0);

            var photoRect = new Rect((width - PhotoWidth) / 2, strip.Height > 0 ? strip.Height + 10 : 0, PhotoWidth, photoHeight);
            foreach (var item in items.Where(i => i.Group.PhotoLeds.Count > 0))
            {
                var group = item.Group;
                var leds = DashPhotoLayouts.Resample(group.PhotoLeds, group.Count);
                for (var i = 0; i < leds.Count; i++)
                {
                    var led = leds[i];
                    var w = Math.Max(5, led.Width * scale);
                    var h = Math.Max(5, led.Height * scale);
                    spots.Add(new LedSpot
                    {
                        Part = item.Part,
                        Group = group,
                        Logical = i,
                        Physical = group.PhysicalIndex(i),
                        Center = new Point(photoRect.Left + led.X * scale, photoRect.Top + led.Y * scale),
                        Radius = Math.Max(w, h) / 2,
                        BarWidth = w,
                        BarHeight = h,
                        IsBar = true,
                    });
                }
            }

            Backdrop = new PreviewBackdrop { Photo = picture, PhotoRect = photoRect };
            Size = new Size(width, photoRect.Bottom);
        }

        /// <summary>
        /// Lays the groups out on a GT wheel: a center plate between two grips, with thumb openings in between.
        /// The plate grows with the LEDs it carries; the wheel body is built around it.
        /// </summary>
        private void BuildWheelLayout(List<Item> items)
        {
            var rows = items.Where(i => i.Group.Kind == LedGroupKind.HorizontalRow).ToList();
            var leftColumns = items.Where(i => i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.Left).ToList();
            var rightColumns = items.Where(i => i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.Right).ToList();
            var center = items.Where(i => i.Group.Kind == LedGroupKind.Matrix || (i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.None)).ToList();
            var plate = PlatePositions(items.Where(i => i.Group.IsElement));

            double W(Item i) => i.Group.Width * Pitch;
            double H(Item i) => i.Group.Height * Pitch;

            var rowsWidth = rows.Count == 0 ? 0 : rows.Max(W);
            var rowsHeight = rows.Sum(i => H(i) + 4);
            var leftWidth = leftColumns.Sum(i => W(i) + ColumnGap);
            var rightWidth = rightColumns.Sum(i => W(i) + ColumnGap);
            var columnsHeight = leftColumns.Concat(rightColumns).Select(H).DefaultIfEmpty(0).Max();

            // Center stack: the display (or matrices and side-less columns in its place), then the front plate elements.
            var centerGroupsWidth = center.Count == 0 ? 0 : center.Sum(i => W(i) + WheelGap) - WheelGap;
            var topWidth = center.Count > 0 ? centerGroupsWidth : DisplayWidth;
            var topHeight = center.Count > 0 ? center.Max(H) : DisplayHeight;
            var elementsWidth = plate.Count == 0 ? 0 : (2 * plate.Max(p => Math.Abs(p.x)) + 1) * PlateUnit;
            var elementsHeight = plate.Count == 0 ? 0 : (plate.Max(p => p.y) - plate.Min(p => p.y) + 1) * PlateUnit;
            var centerWidth = Math.Max(topWidth, elementsWidth);
            var centerHeight = topHeight + (plate.Count > 0 ? WheelGap + elementsHeight : 0);

            var middleTop = WheelPad + rowsHeight + (rows.Count > 0 ? 8 : 0);
            var plateHeight = middleTop + Math.Max(columnsHeight, centerHeight) + WheelPad;
            var plateWidth = Math.Max(Math.Max(MinPlateWidth, plateHeight * PlateAspect), Math.Max(rowsWidth, leftWidth + centerWidth + rightWidth + 2 * WheelGap) + 2 * WheelPad);

            var plateRect = new Rect(GripWidth + ThumbWidth, TopBar, plateWidth, plateHeight);
            var bodyRect = new Rect(0, 0, plateRect.Right + ThumbWidth + GripWidth, TopBar + plateHeight + BottomBar);
            var axis = plateRect.Left + plateWidth / 2;

            var y = plateRect.Top + WheelPad;
            foreach (var row in rows)
            {
                AddGroup(row, axis - W(row) / 2, W(row), TextAlignment.Left, y, false);
                y += H(row) + 4;
            }

            var top = plateRect.Top + middleTop;
            var x = plateRect.Left + WheelPad;
            foreach (var column in leftColumns)
            {
                AddGroup(column, x, W(column), TextAlignment.Left, top, false);
                x += W(column) + ColumnGap;
            }

            x = plateRect.Right - WheelPad;
            foreach (var column in rightColumns)
            {
                x -= W(column);
                AddGroup(column, x, W(column), TextAlignment.Left, top, false);
                x -= ColumnGap;
            }

            Rect? display = null;
            if (center.Count > 0)
            {
                x = axis - centerGroupsWidth / 2;
                foreach (var group in center)
                {
                    AddGroup(group, x, W(group), TextAlignment.Left, top, false);
                    x += W(group) + WheelGap;
                }
            }
            else
            {
                display = new Rect(axis - DisplayWidth / 2, top, DisplayWidth, DisplayHeight);
            }

            if (plate.Count > 0) AddPlate(plate, axis, top + topHeight + WheelGap, null);

            // Body minus the thumb openings between the grips and the plate.
            var holeTop = bodyRect.Top + TopBar + 8;
            var holeHeight = Math.Max(20, bodyRect.Bottom - BottomBar - 4 - holeTop);
            var holeWidth = ThumbWidth - 8;
            var holes = new GeometryGroup();
            holes.Children.Add(new RectangleGeometry(new Rect(GripWidth, holeTop, holeWidth, holeHeight), holeWidth / 2, 18));
            holes.Children.Add(new RectangleGeometry(new Rect(plateRect.Right + 8, holeTop, holeWidth, holeHeight), holeWidth / 2, 18));
            var radius = Math.Min(BodyRadius, bodyRect.Height / 2);
            var body = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(bodyRect, radius, radius), holes);
            body.Freeze();

            // Rubber grips: the body's outer strips, following its rounded outline.
            var gripAreas = new GeometryGroup();
            gripAreas.Children.Add(new RectangleGeometry(new Rect(0, 0, GripWidth, bodyRect.Height)));
            gripAreas.Children.Add(new RectangleGeometry(new Rect(bodyRect.Right - GripWidth, 0, GripWidth, bodyRect.Height)));
            var grips = new CombinedGeometry(GeometryCombineMode.Intersect, body, gripAreas);
            grips.Freeze();

            Backdrop = new PreviewBackdrop { Body = body, Grips = grips, Plate = plateRect, PlateRadius = 14, Display = display };
            Size = new Size(bodyRect.Width, bodyRect.Height);
        }

        /// <summary>
        /// A generic dashboard: the rows (RPM LEDs) across the top bar, the side columns down the left and right edges
        /// next to the screen, matrices, side-less columns and button LEDs below the screen.
        /// </summary>
        private void BuildDashLayout(List<Item> items)
        {
            var rows = items.Where(i => i.Group.Kind == LedGroupKind.HorizontalRow).ToList();
            var leftColumns = items.Where(i => i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.Left).ToList();
            var rightColumns = items.Where(i => i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.Right).ToList();
            var center = items.Where(i => i.Group.Kind == LedGroupKind.Matrix || (i.Group.Kind == LedGroupKind.VerticalColumn && i.Group.Side == LedGroupSide.None)).ToList();
            var plate = PlatePositions(items.Where(i => i.Group.IsElement));

            double W(Item i) => i.Group.Width * Pitch;
            double H(Item i) => i.Group.Height * Pitch;

            var rowsWidth = rows.Count == 0 ? 0 : rows.Max(W);
            var leftWidth = leftColumns.Sum(i => W(i) + ColumnGap);
            var rightWidth = rightColumns.Sum(i => W(i) + ColumnGap);
            var columnsHeight = leftColumns.Concat(rightColumns).Select(H).DefaultIfEmpty(0).Max();
            var belowWidth = Math.Max(center.Count == 0 ? 0 : center.Sum(i => W(i) + WheelGap) - WheelGap, plate.Count == 0 ? 0 : (2 * plate.Max(p => Math.Abs(p.x)) + 1) * PlateUnit);
            var belowHeight = Math.Max(center.Select(H).DefaultIfEmpty(0).Max(), plate.Count == 0 ? 0 : (plate.Max(p => p.y) - plate.Min(p => p.y) + 1) * PlateUnit);

            var screenWidth = Math.Max(MinScreenWidth, Math.Max(rowsWidth - 2 * DashPad, belowWidth));
            var screenHeight = Math.Max(MinScreenHeight, Math.Min(columnsHeight, screenWidth * 0.6));
            var bodyWidth = Math.Max(rowsWidth + 2 * DashPad, leftWidth + screenWidth + rightWidth + 4 * DashPad);
            var axis = bodyWidth / 2;

            var y = DashPad;
            foreach (var row in rows)
            {
                AddGroup(row, axis - W(row) / 2, W(row), TextAlignment.Left, y, false);
                y += H(row) + 4;
            }

            var screen = new Rect(axis - screenWidth / 2, (rows.Count > 0 ? y + 6 : DashPad), screenWidth, screenHeight);

            // Side LEDs: vertically centered on the screen.
            var sideTop = screen.Top + Math.Max(0, (screen.Height - columnsHeight) / 2);
            var x = DashPad;
            foreach (var column in leftColumns)
            {
                AddGroup(column, x, W(column), TextAlignment.Left, sideTop, false);
                x += W(column) + ColumnGap;
            }

            x = bodyWidth - DashPad;
            foreach (var column in rightColumns)
            {
                x -= W(column);
                AddGroup(column, x, W(column), TextAlignment.Left, sideTop, false);
                x -= ColumnGap;
            }

            var bottom = Math.Max(screen.Bottom, sideTop + columnsHeight);
            if (belowHeight > 0)
            {
                var belowTop = bottom + DashPad;
                x = axis - (center.Count == 0 ? 0 : center.Sum(i => W(i) + WheelGap) - WheelGap) / 2;
                foreach (var group in center)
                {
                    AddGroup(group, x, W(group), TextAlignment.Left, belowTop, false);
                    x += W(group) + WheelGap;
                }

                if (plate.Count > 0) AddPlate(plate, axis, belowTop, null);
                bottom = belowTop + belowHeight;
            }

            var bodyRect = new Rect(0, 0, bodyWidth, bottom + DashPad);
            var body = new RectangleGeometry(bodyRect, 22, 22);
            body.Freeze();
            Backdrop = new PreviewBackdrop { Body = body, Display = screen };
            Size = bodyRect.Size;
        }

        /// <summary>Places a group inside a slot of <paramref name="slotWidth"/>, its LEDs aligned like its label (if it has one).</summary>
        private void AddGroup(Item item, double slotX, double slotWidth, TextAlignment alignment, double y, bool withLabel = true)
        {
            var group = item.Group;
            if (withLabel) labels.Add(new PreviewLabel { Text = group.Name, Position = new Point(slotX, y), MaxWidth = slotWidth, Alignment = alignment });

            var width = group.Width * Pitch;
            var x = alignment == TextAlignment.Right ? slotX + slotWidth - width
                : alignment == TextAlignment.Center ? slotX + (slotWidth - width) / 2
                : slotX;
            var top = withLabel ? y + LabelHeight : y;
            for (var py = 0; py < group.Height; py++)
            {
                for (var px = 0; px < group.Width; px++)
                {
                    var logical = group.Kind == LedGroupKind.VerticalColumn ? group.Count - 1 - py : group.Kind == LedGroupKind.Matrix ? py * group.Columns + px : px;
                    spots.Add(new LedSpot
                    {
                        Part = item.Part,
                        Group = group,
                        Logical = logical,
                        Physical = group.PhysicalIndex(px, py),
                        Center = new Point(x + px * Pitch + Pitch / 2, top + py * Pitch + Pitch / 2),
                        Radius = Radius,
                    });
                }
            }
        }

        /// <summary>Size of the front plate block: the element grid plus its label.</summary>
        private static Size PlateSize(List<(Item item, double x, double y)> plate)
        {
            if (plate.Count == 0) return new Size(0, 0);
            var width = (plate.Max(p => p.x) - plate.Min(p => p.x) + 1) * PlateUnit;
            var height = (plate.Max(p => p.y) - plate.Min(p => p.y) + 1) * PlateUnit + LabelHeight;
            return new Size(Math.Max(width, LabelSlot), height);
        }

        /// <summary>
        /// Places the front plate elements at their grid positions, mirror-symmetric around <paramref name="axis"/>,
        /// under an optional <paramref name="label"/>.
        /// </summary>
        private void AddPlate(List<(Item item, double x, double y)> plate, double axis, double top, string label)
        {
            if (label != null)
            {
                labels.Add(new PreviewLabel { Text = label, Position = new Point(axis - LabelSlot / 2, top), MaxWidth = LabelSlot, Alignment = TextAlignment.Center });
                top += LabelHeight;
            }

            var minY = plate.Min(p => p.y);
            foreach (var (item, x, y) in plate)
            {
                var group = item.Group;
                spots.Add(new LedSpot
                {
                    Part = item.Part,
                    Group = group,
                    Physical = group.PhysicalIndex(0),
                    Center = new Point(axis + x * PlateUnit, top + (y - minY + 0.5) * PlateUnit),
                    Radius = group.ElementType == ElementType.Dial ? 11 : group.ElementType == ElementType.Light ? 4 : 8,
                    HasButtonRim = group.ElementType == ElementType.Button,
                });
            }
        }

        /// <summary>
        /// Front plate elements with their grid positions: from the SimHub photo for preset elements, else from the
        /// part's front plate presets (elements outside the current presets are skipped).
        /// </summary>
        private List<(Item item, double x, double y)> PlatePositions(IEnumerable<Item> elements)
        {
            var canvasWidth = device?.Preset?.CanvasWidth > 0 ? device.Preset.CanvasWidth : 1000;
            var result = new List<(Item, double, double)>();
            foreach (var item in elements)
            {
                var element = item.Group;
                if (element.HasPhotoPosition)
                {
                    result.Add((item, (element.PhotoX - canvasWidth / 2) / CanvasPerPlateUnit, element.PhotoY / CanvasPerPlateUnit));
                    continue;
                }

                try
                {
                    var (x, y) = FrontPlatePresets.Position(item.Part.FrontPlate, element.Side, element.ElementSlot);
                    result.Add((item, x, y));
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Not part of the current layout.
                }
            }

            return result;
        }

        private void Shift(int firstSpot, int firstLabel, double dx, double dy)
        {
            for (var i = firstSpot; i < spots.Count; i++) spots[i].Center = new Point(spots[i].Center.X + dx, spots[i].Center.Y + dy);
            for (var i = firstLabel; i < labels.Count; i++) labels[i].Position = new Point(labels[i].Position.X + dx, labels[i].Position.Y + dy);
        }

        /// <summary>A group and the part it belongs to.</summary>
        private struct Item
        {
            public Item(DevicePart part, LedGroup group)
            {
                Part = part;
                Group = group;
            }

            public DevicePart Part { get; }

            public LedGroup Group { get; }
        }
    }

    /// <summary>One LED in a <see cref="PreviewLayout"/>.</summary>
    public sealed class LedSpot
    {
        public DevicePart Part;
        public LedGroup Group;

        /// <summary>Logical LED index within the group (front plate elements: 0).</summary>
        public int Logical;

        /// <summary>Physical LED index within the part: where its color is in the part's frame.</summary>
        public int Physical;

        public Point Center;
        public double Radius;

        /// <summary>A front plate button: drawn with a stronger rim than lights and dials.</summary>
        public bool HasButtonRim;

        /// <summary>Drawn as a colored ring around a button on a photo, not as a filled LED.</summary>
        public bool IsRing;

        /// <summary>An LED on a dashboard picture: a rounded rectangle of <see cref="BarWidth"/> × <see cref="BarHeight"/>.</summary>
        public bool IsBar;

        public double BarWidth;

        public double BarHeight;
    }

    /// <summary>A group's name in a <see cref="PreviewLayout"/>: one line, trimmed to <see cref="MaxWidth"/>.</summary>
    public sealed class PreviewLabel
    {
        public string Text;
        public Point Position;
        public double MaxWidth;
        public TextAlignment Alignment;
    }

    /// <summary>The shapes behind the LEDs of a <see cref="PreviewLayout"/>: a wheel or dash body, its plate and display, or a photo.</summary>
    public sealed class PreviewBackdrop
    {
        public Geometry Body;
        public Geometry Grips;
        public Rect? Plate;
        public double PlateRadius;
        public Rect? Display;
        public ImageSource Photo;
        public Rect PhotoRect;
    }
}
