using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// Where the LED positions of a dashboard on its product picture come from. Values are explicit because they are
    /// stored in the settings; never renumber them.
    /// </summary>
    public enum PhotoLedSource
    {
        /// <summary>No positions: the device has no picture, or nothing was found and the user wasn't asked yet.</summary>
        None = 0,

        /// <summary>FreeTheLights' list of SimHub's dashboards, for exactly this picture (same SHA-256).</summary>
        BuiltIn = 1,

        /// <summary>Found on the picture at runtime (<see cref="DashLedDetector"/>).</summary>
        Found = 2,

        /// <summary>Placed or adjusted by the user.</summary>
        Manual = 3,

        /// <summary>The user chose the generic dashboard drawing instead of the picture.</summary>
        Generic = 4,
    }

    /// <summary>LED positions for a SimHub device's product picture, and where they come from.</summary>
    public class DashPhotoResult
    {
        /// <summary>The picture, relative to the SimHub folder.</summary>
        public string PictureFile { get; set; }

        public int PictureWidth { get; set; }

        public int PictureHeight { get; set; }

        /// <summary>The positions, or null when none were found (<see cref="Source"/> is then <see cref="PhotoLedSource.None"/>).</summary>
        public DashPhotoLayout Layout { get; set; }

        public PhotoLedSource Source { get; set; }
    }

    /// <summary>
    /// The LED positions of SimHub's dashboards on their product pictures (SimHub has none). In this order:
    /// <list type="number">
    /// <item>FreeTheLights' built-in list (DashboardLayouts.json), reviewed by hand, when the picture is the one the list
    /// was made for (same SHA-256);</item>
    /// <item>otherwise the positions <see cref="DashLedDetector"/> finds on the picture;</item>
    /// <item>otherwise none: the user places the LEDs, or picks the generic dashboard drawing.</item>
    /// </list>
    /// </summary>
    public static class DashPhotoLayouts
    {
        private static readonly object Sync = new object();
        private static Dictionary<string, BuiltInLayout> builtIn;

        /// <summary>
        /// The positions for the picture of SimHub device <paramref name="deviceTypeId"/> with <paramref name="total"/>
        /// LEDs. <paramref name="pixels"/> decodes the picture (0xAARRGGBB per pixel) and is only called when the
        /// built-in list has nothing for this exact picture.
        /// </summary>
        public static DashPhotoResult Resolve(string deviceTypeId, string pictureFile, string sha256, int width, int height, Func<int[]> pixels, int total, LedSplit suggested)
        {
            var result = new DashPhotoResult { PictureFile = pictureFile, PictureWidth = width, PictureHeight = height };
            var known = Find(deviceTypeId);
            if (known != null && string.Equals(known.Sha256, sha256, StringComparison.OrdinalIgnoreCase) && known.Layout.Total == total)
            {
                result.Layout = known.Layout;
                result.Source = PhotoLedSource.BuiltIn;
                return result;
            }

            var argb = pixels?.Invoke();
            var found = argb == null ? null : DashLedDetector.Detect(argb, width, height, total, suggested);
            if (found != null)
            {
                result.Layout = found;
                result.Source = PhotoLedSource.Found;
            }

            return result;
        }

        /// <summary>The built-in entry for a SimHub device, or null.</summary>
        public static BuiltInLayout Find(string deviceTypeId)
        {
            if (string.IsNullOrEmpty(deviceTypeId)) return null;
            return BuiltIn.TryGetValue(deviceTypeId, out var layout) ? layout : null;
        }

        /// <summary>Every built-in entry by SimHub device type id.</summary>
        public static IReadOnlyDictionary<string, BuiltInLayout> BuiltIn
        {
            get
            {
                lock (Sync)
                {
                    if (builtIn == null) builtIn = Load();
                    return builtIn;
                }
            }
        }

        /// <summary>Reads the embedded list; an unreadable list is empty (runtime detection still works).</summary>
        private static Dictionary<string, BuiltInLayout> Load()
        {
            var result = new Dictionary<string, BuiltInLayout>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var stream = typeof(DashPhotoLayouts).Assembly.GetManifestResourceStream("FreeTheLights.Settings.DashboardLayouts.json"))
                {
                    if (stream == null) return result;
                    using (var reader = new StreamReader(stream)) Parse(reader.ReadToEnd(), result);
                }
            }
            catch (Exception)
            {
                result.Clear();
            }

            return result;
        }

        internal static void Parse(string json, Dictionary<string, BuiltInLayout> into)
        {
            foreach (var device in (JArray)JObject.Parse(json)["devices"])
            {
                var width = (int)device["width"];
                var height = (int)device["height"];
                var entry = new BuiltInLayout
                {
                    DeviceTypeId = (string)device["id"],
                    Name = (string)device["name"],
                    Sha256 = (string)device["sha256"],
                    Layout = new DashPhotoLayout
                    {
                        PictureWidth = width,
                        PictureHeight = height,
                        Left = Leds(device["left"]),
                        Top = Leds(device["top"]),
                        Right = Leds(device["right"]),
                    },
                };
                into[entry.DeviceTypeId] = entry;
            }
        }

        private static List<PhotoLed> Leds(JToken leds) => leds == null
            ? new List<PhotoLed>()
            : leds.Select(l => new PhotoLed((double)l[0], (double)l[1], (double)l[2], (double)l[3])).ToList();

        /// <summary>
        /// Gives the side columns and the top row among <paramref name="groups"/> their positions from
        /// <paramref name="layout"/> (the first row gets the top LEDs, the left and right columns their sides). Groups
        /// of another LED count get the positions spread over their count.
        /// </summary>
        public static void Apply(IEnumerable<LedGroup> groups, DashPhotoLayout layout)
        {
            if (groups == null || layout == null) return;
            var list = groups.ToList();
            var row = list.FirstOrDefault(g => g.Kind == LedGroupKind.HorizontalRow);
            foreach (var group in list.Where(g => g.Kind == LedGroupKind.HorizontalRow || g.Kind == LedGroupKind.VerticalColumn))
            {
                var leds = group == row ? layout.Top
                    : group.Kind == LedGroupKind.VerticalColumn && group.Side == LedGroupSide.Left ? layout.Left
                    : group.Kind == LedGroupKind.VerticalColumn && group.Side == LedGroupSide.Right ? layout.Right
                    : null;
                group.PhotoLeds = leds != null && leds.Count > 0 ? Resample(leds, group.Count) : new List<PhotoLed>();
            }
        }

        /// <summary>
        /// Gives every row and column among <paramref name="groups"/> positions on a picture of
        /// <paramref name="width"/> × <paramref name="height"/>: kept when they fit the group's LED count, spread over
        /// the new count when it changed, else a default place (top row along the top edge, columns along the sides).
        /// </summary>
        public static void EnsurePositions(IEnumerable<LedGroup> groups, int width, int height)
        {
            var list = groups.Where(g => g.Kind == LedGroupKind.HorizontalRow || g.Kind == LedGroupKind.VerticalColumn).ToList();
            var rows = 0;
            foreach (var group in list)
            {
                if (group.PhotoLeds.Count == group.Count) continue;
                group.PhotoLeds = group.PhotoLeds.Count >= 2 ? Resample(group.PhotoLeds, group.Count) : Default(group, group.Kind == LedGroupKind.HorizontalRow ? rows : 0, width, height);
                if (group.Kind == LedGroupKind.HorizontalRow) rows++;
            }
        }

        /// <summary>A starting place for a group without positions: rows along the top edge, columns along the sides.</summary>
        public static List<PhotoLed> Default(LedGroup group, int rowIndex, double width, double height)
        {
            var size = Math.Max(4, Math.Round(width * 0.022));
            if (group.Kind == LedGroupKind.HorizontalRow)
            {
                var y = height * (0.07 + 0.07 * rowIndex);
                return Line(new PhotoLed(width * 0.25, y, size, size), new PhotoLed(width * 0.75, y, size, size), group.Count);
            }

            var x = group.Side == LedGroupSide.Right ? width * 0.94 : group.Side == LedGroupSide.Left ? width * 0.06 : width * 0.5;
            return Line(new PhotoLed(x, height * 0.65, size, size), new PhotoLed(x, height * 0.3, size, size), group.Count);
        }

        /// <summary><paramref name="count"/> LEDs evenly spaced from <paramref name="first"/> to <paramref name="last"/>.</summary>
        public static List<PhotoLed> Line(PhotoLed first, PhotoLed last, int count)
        {
            var result = new List<PhotoLed>();
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.5 : i / (double)(count - 1);
                result.Add(new PhotoLed(first.X + (last.X - first.X) * t, first.Y + (last.Y - first.Y) * t, first.Width, first.Height));
            }

            return result;
        }

        /// <summary>
        /// <paramref name="count"/> LEDs spread evenly along the path through <paramref name="leds"/> (same ends and
        /// shape, e.g. a curved row); the same LEDs when the count is unchanged.
        /// </summary>
        public static List<PhotoLed> Resample(IList<PhotoLed> leds, int count)
        {
            if (leds == null || leds.Count == 0 || count <= 0) return new List<PhotoLed>();
            if (leds.Count == count) return leds.Select(l => l.Clone()).ToList();
            if (leds.Count == 1) return Enumerable.Range(0, count).Select(_ => leds[0].Clone()).ToList();

            var result = new List<PhotoLed>();
            for (var i = 0; i < count; i++)
            {
                var position = count == 1 ? (leds.Count - 1) / 2.0 : i * (leds.Count - 1) / (double)(count - 1);
                var a = Math.Min(leds.Count - 2, (int)Math.Floor(position));
                var t = position - a;
                PhotoLed p = leds[a], q = leds[a + 1];
                result.Add(new PhotoLed(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Width + (q.Width - p.Width) * t, p.Height + (q.Height - p.Height) * t));
            }

            return result;
        }

        /// <summary>
        /// Moves LED <paramref name="index"/> of <paramref name="leds"/> by (<paramref name="dx"/>, <paramref name="dy"/>).
        /// Moving the first or last LED stretches the group: the others follow in proportion to their distance from
        /// the opposite end, so the LEDs stay evenly spread (and a curve keeps its shape). Any other LED moves alone.
        /// </summary>
        public static List<PhotoLed> Move(IList<PhotoLed> leds, int index, double dx, double dy)
        {
            var result = leds.Select(l => l.Clone()).ToList();
            if (index < 0 || index >= result.Count) return result;
            var last = result.Count - 1;
            for (var i = 0; i < result.Count; i++)
            {
                double weight;
                if (index == 0 && last > 0) weight = 1 - i / (double)last;
                else if (index == last && last > 0) weight = i / (double)last;
                else weight = i == index ? 1 : 0;
                result[i].X += dx * weight;
                result[i].Y += dy * weight;
            }

            return result;
        }
    }

    /// <summary>One entry of the built-in list: the positions for one SimHub dashboard's picture.</summary>
    public class BuiltInLayout
    {
        public string DeviceTypeId { get; set; }

        public string Name { get; set; }

        /// <summary>SHA-256 of the picture file the positions were made for (hex).</summary>
        public string Sha256 { get; set; }

        public DashPhotoLayout Layout { get; set; }
    }
}
