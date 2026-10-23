using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>A click in an interactive <see cref="LedPreview"/>: the LED under the mouse, or empty space.</summary>
    public sealed class LedClickEventArgs : EventArgs
    {
        public LedClickEventArgs(DevicePart part, LedGroup group, int logical, int physical, bool add)
        {
            Part = part;
            Group = group;
            Logical = logical;
            Physical = physical;
            Add = add;
        }

        /// <summary>The part of the clicked LED; null when empty space was clicked.</summary>
        public DevicePart Part { get; }

        /// <summary>The group of the clicked LED; null when empty space was clicked.</summary>
        public LedGroup Group { get; }

        /// <summary>Logical LED index within the group (-1 for empty space; front plate elements: 0).</summary>
        public int Logical { get; }

        /// <summary>Physical LED index within the part (-1 for empty space).</summary>
        public int Physical { get; }

        /// <summary>Ctrl or Shift was held: the click adds to the selection instead of replacing it.</summary>
        public bool Add { get; }
    }

    /// <summary>
    /// Draws all LEDs of a device (every part, each with its own frame) with the colors of the latest frames:
    /// <list type="bullet">
    /// <item>Wheels made from a SimHub preset with a photo: the photo with the button LEDs as rings over the buttons,
    /// and the other LEDs (shift lights) as a labelled strip above it.</item>
    /// <item>Other wheels: a generic GT wheel (shift lights across the top of the center plate, side columns at its
    /// edges, front plate elements below a display).</item>
    /// <item>Dashboards: a generic dash (RPM LEDs across the top, side LEDs down the edges, a screen in the middle).</item>
    /// <item>Everything else: a labelled schematic (rows on top, left/right columns at the sides, the rest in the middle).</item>
    /// </list>
    /// Hovering an LED shows its part, group and LED number. With <see cref="IsInteractive"/> the LEDs can be clicked
    /// (<see cref="LedClicked"/>) and the selected groups are outlined (<see cref="SetSelection"/>).
    /// </summary>
    public class LedPreview : FrameworkElement
    {
        /// <summary>Radius of a lit LED's glow, relative to the LED's radius.</summary>
        private const double GlowScale = 2.1;

        private static readonly Brush OffBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x2B, 0x30)));
        private static readonly Pen RimPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x90, 0x90, 0x98)), 1));
        private static readonly Pen ButtonRimPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xC0, 0xC0, 0xC8)), 2.5));
        private static readonly Pen OffRingPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1.5));
        private static readonly Typeface LabelTypeface = new Typeface("Segoe UI");
        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x8C, 0x94)));

        // ---------- Selection and hover ----------

        /// <summary>Smallest hit radius of an LED when interactive, so tiny LEDs stay easy to click.</summary>
        private const double MinHitRadius = 6;

        /// <summary>Distance between an LED's edge and the centerline of its selection ring, hover ring and focus ring.</summary>
        private const double SelectionGap = 3.5;
        private const double HoverGap = 3;
        private const double FocusGap = SelectionGap;

        /// <summary>A dashed white ring: unlike any color, it can't be mistaken for a lit LED (e.g. a cyan button on a wheel photo).</summary>
        private static readonly Pen SelectionPen = Freeze(new Pen(Brushes.White, 2.2) { DashStyle = new DashStyle(new[] { 2.0, 1.5 }, 0), DashCap = PenLineCap.Flat });

        /// <summary>A dark stroke under the selection ring, 1.2 px wider on each side: keeps it visible on light photos and next to an LED of a similar color.</summary>
        private static readonly Pen SelectionOutlinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xA0, 0x00, 0x00, 0x00)), 4.6));
        /// <summary>The clicked LED: a solid white ring over the dashed one.</summary>
        private static readonly Pen FocusPen = Freeze(new Pen(Brushes.White, 2.6));
        private static readonly Pen HoverPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF)), 1.5));

        // ---------- Wheel and dash backdrops ----------
        private static readonly Brush BodyBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x2A, 0x2C, 0x32), Color.FromRgb(0x1A, 0x1B, 0x1F), 90));
        private static readonly Pen BodyPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 1));
        private static readonly Brush GripBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x1C, 0x1C, 0x20), Color.FromRgb(0x10, 0x10, 0x12), 0));
        private static readonly Pen GripPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1));
        private static readonly Brush PlateBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x21, 0x26)));
        private static readonly Pen PlatePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)), 1));
        private static readonly Brush DisplayBrush = Freeze(new LinearGradientBrush(Color.FromRgb(0x14, 0x16, 0x1C), Color.FromRgb(0x08, 0x09, 0x0B), 90));
        private static readonly Pen DisplayPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1));

        /// <summary>Fill, glow and ring pen per color; frames only have a handful of distinct colors.</summary>
        private readonly Dictionary<int, LedBrushes> brushes = new Dictionary<int, LedBrushes>();

        private readonly Dictionary<Guid, LedColor[]> frames = new Dictionary<Guid, LedColor[]>();
        private readonly ToolTip toolTip = new ToolTip();
        private DeviceConfig device;
        private string layoutSignature = string.Empty;

        /// <summary>Where the LEDs, labels and backdrop are drawn; rebuilt when the device's layout changes.</summary>
        private PreviewLayout layout = PreviewLayout.Empty;

        /// <summary>The labels' formatted text, built on first draw (null until then), and the DPI it was built for.</summary>
        private FormattedText[] labelTexts = new FormattedText[0];
        private double labelTextsDpi;

        private bool isInteractive;

        /// <summary>The selected groups, by id: they survive a relayout because spots are matched by their group at render time.</summary>
        private readonly HashSet<Guid> selectedGroups = new HashSet<Guid>();

        private readonly List<LedSpot> selectedSpots = new List<LedSpot>();
        private Guid? focusGroup;
        private int focusLogical = -1;

        /// <summary>The spot under the mouse (interactive only).</summary>
        private LedSpot hovered;

        private bool pressed;
        private LedSpot pressedSpot;

        public LedPreview()
        {
            ToolTip = toolTip;
            ToolTipService.SetInitialShowDelay(this, 0);
        }

        /// <summary>Raised when an LED or empty space is clicked; only when <see cref="IsInteractive"/>.</summary>
        public event EventHandler<LedClickEventArgs> LedClicked;

        /// <summary>
        /// True: a hand cursor and a hover ring over the LEDs, and clicks raise <see cref="LedClicked"/>.
        /// False (the default): the preview only shows the LEDs and their tooltips.
        /// </summary>
        public bool IsInteractive
        {
            get => isInteractive;
            set
            {
                if (isInteractive == value) return;
                isInteractive = value;
                hovered = null;
                Cursor = null;
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Outlines every LED of the groups <paramref name="groupIds"/> (null or empty = no selection), and marks the LED
        /// <paramref name="focusLogical"/> of <paramref name="focusGroupId"/> as the clicked one (front plate elements have one LED).
        /// </summary>
        public void SetSelection(IEnumerable<Guid> groupIds, Guid? focusGroupId, int focusLogical)
        {
            selectedGroups.Clear();
            if (groupIds != null)
            {
                foreach (var id in groupIds) selectedGroups.Add(id);
            }

            focusGroup = focusGroupId;
            this.focusLogical = focusLogical;
            InvalidateVisual();
        }

        /// <summary>Where <see cref="RefreshFrames"/> gets the parts' frames from (set by <see cref="Update"/>).</summary>
        private Func<Guid, LedColor[]> frameSource;

        /// <summary>
        /// Shows <paramref name="newDevice"/> with the frame of each of its parts. Rebuilds the layout when the device or
        /// its layout changed, so call it when settings may have changed (a few times per second is enough); call
        /// <see cref="RefreshFrames"/> once per screen frame for smooth animations.
        /// </summary>
        public void Update(DeviceConfig newDevice, Func<Guid, LedColor[]> frameOf)
        {
            frameSource = frameOf;
            var framesChanged = FetchFrames(newDevice);

            var signature = Signature(newDevice);
            var relayout = newDevice != device || signature != layoutSignature;
            device = newDevice;
            layoutSignature = signature;
            if (relayout)
            {
                layout = PreviewLayout.Build(device);
                labelTexts = new FormattedText[layout.Labels.Count];
                hovered = isInteractive && IsMouseOver ? SpotAt(Mouse.GetPosition(this)) : null;
                InvalidateMeasure();
            }

            // Called a few times per second: repaint only when something shown changed.
            if (relayout || framesChanged) InvalidateVisual();
        }

        /// <summary>
        /// Fetches the parts' latest frames and repaints only when one of them is new. The game loop's frame store keeps
        /// a part's array while its colors stay the same, so a changed reference means new colors. Cheap enough to call
        /// once per screen frame.
        /// </summary>
        public void RefreshFrames()
        {
            if (device == null || frameSource == null) return;
            if (FetchFrames(device)) InvalidateVisual();
        }

        /// <summary>
        /// Takes the latest frame of each part of <paramref name="shown"/> from the frame source; true if one is new.
        /// Runs once per screen frame: no allocations.
        /// </summary>
        private bool FetchFrames(DeviceConfig shown)
        {
            if (shown == null)
            {
                if (frames.Count == 0) return false;
                frames.Clear();
                return true;
            }

            var changed = false;
            var parts = shown.Parts;
            var sameParts = frames.Count == parts.Count;
            for (var i = 0; sameParts && i < parts.Count; i++) sameParts = frames.ContainsKey(parts[i].Id);
            if (!sameParts)
            {
                // Other parts: frames of parts no longer shown go.
                frames.Clear();
                changed = true;
            }

            foreach (var part in parts)
            {
                var frame = frameSource?.Invoke(part.Id);
                if (frames.TryGetValue(part.Id, out var current) && ReferenceEquals(current, frame)) continue;
                frames[part.Id] = frame;
                changed = true;
            }

            return changed;
        }

        protected override Size MeasureOverride(Size availableSize) => layout.Size;

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            DrawBackdrop(dc, layout.Backdrop);

            // Labels only change with the layout; formatting text on every repaint was a large part of a frame.
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (labelTextsDpi != dpi)
            {
                Array.Clear(labelTexts, 0, labelTexts.Length);
                labelTextsDpi = dpi;
            }

            for (var i = 0; i < layout.Labels.Count; i++)
            {
                var label = layout.Labels[i];
                var text = labelTexts[i];
                if (text == null)
                {
                    text = new FormattedText(label.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, 10, LabelBrush, dpi);
                    text.MaxTextWidth = Math.Max(20, label.MaxWidth);
                    text.MaxLineCount = 1;
                    text.Trimming = TextTrimming.CharacterEllipsis;
                    text.TextAlignment = label.Alignment;
                    labelTexts[i] = text;
                }

                dc.DrawText(text, label.Position);
            }

            // Glows first, so a neighbour's glow never covers an LED.
            var spots = layout.Spots;
            foreach (var spot in spots)
            {
                var color = ColorOf(spot);
                if (color.HasValue)
                {
                    var r = spot.Radius * (spot.IsRing ? 1.5 : GlowScale);
                    dc.DrawEllipse(BrushesOf(color.Value).Glow, null, spot.Center, r, r);
                }
            }

            foreach (var spot in spots)
            {
                var color = ColorOf(spot);
                if (spot.IsBar)
                {
                    // An LED on a dashboard picture: covers the LED painted on the picture, dark when off.
                    var rect = new Rect(spot.Center.X - spot.BarWidth / 2, spot.Center.Y - spot.BarHeight / 2, spot.BarWidth, spot.BarHeight);
                    var corner = Math.Min(spot.BarWidth, spot.BarHeight) / 2;
                    dc.DrawRoundedRectangle(color.HasValue ? BrushesOf(color.Value).Fill : OffBrush, RimPen, rect, corner, corner);
                }
                else if (spot.IsRing)
                {
                    // Button on a photo: a colored ring around the button, so the photo stays visible.
                    if (color.HasValue) dc.DrawEllipse(BrushesOf(color.Value).RingFill, BrushesOf(color.Value).Ring(spot.Radius), spot.Center, spot.Radius, spot.Radius);
                    else dc.DrawEllipse(null, OffRingPen, spot.Center, spot.Radius, spot.Radius);
                }
                else
                {
                    dc.DrawEllipse(color.HasValue ? BrushesOf(color.Value).Fill : OffBrush, spot.HasButtonRim ? ButtonRimPen : RimPen, spot.Center, spot.Radius, spot.Radius);
                }
            }

            DrawOverlays(dc);
        }

        /// <summary>The shapes behind the LEDs: a wheel or dash body, its plate and display, or a photo (none for the schematic).</summary>
        private static void DrawBackdrop(DrawingContext dc, PreviewBackdrop backdrop)
        {
            if (backdrop == null) return;
            if (backdrop.Body != null) dc.DrawGeometry(BodyBrush, BodyPen, backdrop.Body);
            if (backdrop.Grips != null) dc.DrawGeometry(GripBrush, GripPen, backdrop.Grips);
            if (backdrop.Plate.HasValue) dc.DrawRoundedRectangle(PlateBrush, PlatePen, backdrop.Plate.Value, backdrop.PlateRadius, backdrop.PlateRadius);
            if (backdrop.Display.HasValue) dc.DrawRoundedRectangle(DisplayBrush, DisplayPen, backdrop.Display.Value, 6, 6);
            if (backdrop.Photo != null) dc.DrawImage(backdrop.Photo, backdrop.PhotoRect);
        }

        /// <summary>The hover ring and the selection, over all LEDs.</summary>
        private void DrawOverlays(DrawingContext dc)
        {
            if (isInteractive && hovered != null)
            {
                var r = Extent(hovered) + HoverGap;
                dc.DrawEllipse(null, HoverPen, hovered.Center, r, r);
            }

            if (selectedGroups.Count == 0) return;
            selectedSpots.Clear();
            foreach (var spot in layout.Spots)
            {
                if (selectedGroups.Contains(spot.Group.Id)) selectedSpots.Add(spot);
            }

            // Dark outlines first, so a neighbour's outline never crosses a ring; then the rings, then the clicked LED.
            foreach (var spot in selectedSpots)
            {
                var r = Extent(spot) + SelectionGap;
                dc.DrawEllipse(null, SelectionOutlinePen, spot.Center, r, r);
            }

            foreach (var spot in selectedSpots)
            {
                var r = Extent(spot) + SelectionGap;
                dc.DrawEllipse(null, SelectionPen, spot.Center, r, r);
            }

            foreach (var spot in selectedSpots)
            {
                if (spot.Group.Id != focusGroup || !(spot.Group.IsElement || spot.Logical == focusLogical)) continue;
                var r = Extent(spot) + FocusGap;
                dc.DrawEllipse(null, FocusPen, spot.Center, r, r);
            }
        }

        /// <summary>Radius of what is drawn for the LED (including a button ring's outline), the base for the rings around it.</summary>
        private static double Extent(LedSpot spot)
        {
            if (spot.IsBar) return Math.Max(spot.BarWidth, spot.BarHeight) / 2;
            return spot.IsRing ? spot.Radius + LedBrushes.RingThickness(spot.Radius) / 2 : spot.Radius;
        }

        /// <summary>The LED's color in its part's current frame, or null when it is off (transparent or black).</summary>
        private LedColor? ColorOf(LedSpot spot)
        {
            frames.TryGetValue(spot.Part.Id, out var frame);
            var color = frame != null && spot.Physical < frame.Length ? frame[spot.Physical] : LedColor.Transparent;
            return color.IsTransparent || (color.R == 0 && color.G == 0 && color.B == 0) ? (LedColor?)null : color;
        }

        private LedBrushes BrushesOf(LedColor color)
        {
            var key = (color.R << 16) | (color.G << 8) | color.B;
            if (brushes.TryGetValue(key, out var cached)) return cached;
            if (brushes.Count > 512) brushes.Clear();

            var result = new LedBrushes(Color.FromRgb(color.R, color.G, color.B));
            brushes[key] = result;
            return result;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.GetPosition(this);
            var spot = SpotAt(p);
            if (isInteractive)
            {
                Cursor = spot != null ? Cursors.Hand : null;
                if (spot != hovered)
                {
                    hovered = spot;
                    InvalidateVisual();
                }
            }

            if (spot == null)
            {
                toolTip.IsOpen = false;
                return;
            }

            var part = device != null && device.Parts.Count > 1 ? spot.Part.Name + " · " : string.Empty;
            toolTip.Content = spot.Group.IsElement
                ? $"{part}{spot.Group.Name} ({spot.Group.ElementType.ToString().ToLowerInvariant()}) → LED #{spot.Physical + 1}"
                : $"{part}{spot.Group.Name}: LED {spot.Logical + 1} → LED #{spot.Physical + 1}";
            toolTip.IsOpen = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            toolTip.IsOpen = false;
            if (isInteractive) Cursor = null;
            if (hovered != null)
            {
                hovered = null;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!isInteractive) return;

            // A click is a press and a release on the same LED (or both on empty space).
            pressed = true;
            pressedSpot = SpotAt(e.GetPosition(this));
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!isInteractive || !pressed) return;

            var wasPressed = pressedSpot;
            ReleaseMouseCapture();
            e.Handled = true;

            var p = e.GetPosition(this);
            var spot = SpotAt(p);
            if (spot == null)
            {
                // Empty space: only a release inside the control counts.
                if (wasPressed != null || p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight) return;
                LedClicked?.Invoke(this, new LedClickEventArgs(null, null, -1, -1, IsAddModifier()));
                return;
            }

            if (wasPressed == null || spot.Group != wasPressed.Group || spot.Logical != wasPressed.Logical) return;
            LedClicked?.Invoke(this, new LedClickEventArgs(spot.Part, spot.Group, spot.Logical, spot.Physical, IsAddModifier()));
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            pressed = false;
            pressedSpot = null;
        }

        private static bool IsAddModifier() => (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

        /// <summary>
        /// The LED under <paramref name="p"/> (null = none). Interactive previews are more forgiving with tiny LEDs and
        /// pick the nearest one when hit areas overlap.
        /// </summary>
        private LedSpot SpotAt(Point p)
        {
            var spots = layout.Spots;
            if (!isInteractive) return spots.FirstOrDefault(s => (s.Center - p).Length <= s.Radius + 1);

            LedSpot nearest = null;
            var nearestDistance = double.MaxValue;
            foreach (var spot in spots)
            {
                var distance = (spot.Center - p).Length;
                if (distance <= Math.Max(spot.Radius + 1, MinHitRadius) && distance < nearestDistance)
                {
                    nearest = spot;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        private static string Signature(DeviceConfig d)
        {
            if (d == null) return string.Empty;
            return d.Type + ";" + d.Preset?.PhotoFile + ";" + d.Preset?.LedPositions + ";" + string.Join("#", d.Parts.Select(p =>
                p.Id + ";" + p.Name + ";" + p.FrontPlate?.DialPresetId + "/" + p.FrontPlate?.SidePresetId + ";" +
                string.Join(";", p.Groups.Select(g => $"{g.Id}|{g.Name}|{g.Kind}|{g.Count}|{g.Rows}|{g.Columns}|{g.StartIndex}|{g.Reversed}|{g.Serpentine}|{g.StartsAtBottom}|{g.Side}|{g.ElementType}|{g.ElementSlot}|{g.PhotoX}|{g.PhotoY}|{g.PhotoSize}|" +
                                                     string.Join(",", g.PhotoLeds.Select(l => $"{l.X:0.#}/{l.Y:0.#}/{l.Width:0.#}/{l.Height:0.#}"))))));
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private sealed class LedBrushes
        {
            private readonly Color color;
            private readonly Dictionary<int, Pen> rings = new Dictionary<int, Pen>();

            public LedBrushes(Color c)
            {
                color = c;
                Fill = Freeze(new RadialGradientBrush(Color.FromRgb(Lighten(c.R), Lighten(c.G), Lighten(c.B)), c) { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.45, 0.4) });
                Glow = Freeze(new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0x70, c.R, c.G, c.B), 0.3),
                    new GradientStop(Color.FromArgb(0x00, c.R, c.G, c.B), 1.0),
                }));
                RingFill = Freeze(new SolidColorBrush(Color.FromArgb(0x38, c.R, c.G, c.B)));
            }

            public Brush Fill { get; }

            public Brush Glow { get; }

            public Brush RingFill { get; }

            /// <summary>Ring outline for a button of <paramref name="radius"/>.</summary>
            public Pen Ring(double radius)
            {
                var thickness = RingThickness(radius);
                if (rings.TryGetValue(thickness, out var pen)) return pen;
                pen = Freeze(new Pen(new SolidColorBrush(color), thickness));
                rings[thickness] = pen;
                return pen;
            }

            /// <summary>Line width of the ring around a button of <paramref name="radius"/>.</summary>
            public static int RingThickness(double radius) => (int)Math.Round(Math.Max(2.5, radius * 0.22));

            private static byte Lighten(byte channel) => (byte)(channel + (255 - channel) * 0.45);
        }
    }
}
