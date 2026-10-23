using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;

public static class DashProbe
{
    // Built to tools/bin/DashProbe.exe: the plugin comes from the repo's build, SimHub from SIMHUB_INSTALL_PATH or the
    // default folder; contact sheets go to tools/bin/dash/.
    static string simhub = (Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub").TrimEnd('\\') + @"\";
    static string plugin = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\src\FreeTheLights\bin\Debug")) + @"\";
    static string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dash") + @"\";

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var dir in new[] { plugin, simhub })
            {
                var p = Path.Combine(dir, name + ".dll");
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }

            return null;
        };
        Run(args);
    }

    static void Run(string[] args)
    {
        Directory.CreateDirectory(outDir);
        var detectAt = typeof(DashLedDetector).GetMethod("DetectAt", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(int[]), typeof(int), typeof(int), typeof(int) }, null);
        var all = SimHubCatalog.Load();
        var tiles = new System.Collections.Generic.List<BitmapSource>();
        var exclude = new[] { "86AE9014-6D14-4FEC-B3B0-59501B9FED1D" }; // Innato LMP2: a wheel, match not verified
        var json = new System.Collections.Generic.List<string>();
        int ok = 0, fail = 0, nopic = 0, nocount = 0;
        foreach (var info in all)
        {
            if (!info.Has(LedChannel.Leds) || !info.HasDisplay) continue;
            var kind = DeviceKindGuess.Guess(info).Item1;
            if (kind != DeviceType.Dashboard) continue;
            var total = info.CountOf(LedChannel.Leds);
            var tag = $"{info.Name,-48} {kind,-9} n={total,3} split={(info.SuggestedSplit == null ? "-" : info.SuggestedSplit.Left + "/" + info.SuggestedSplit.Top + "/" + info.SuggestedSplit.Right + (info.SuggestedSplit.Confident ? "!" : "?"))}";
            if (info.ProductImageFile == null) { Console.WriteLine(tag + "  NO PICTURE"); nopic++; continue; }
            if (total <= 0) { nocount++; }
            var path = Path.Combine(simhub, info.ProductImageFile);
            var src = new FormatConvertedBitmap(new BitmapImage(new Uri(path)), PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight;
            var px = new int[w * h];
            src.CopyPixels(px, w * 4, 0);
            var layout = total > 0 ? DashLedDetector.Detect(px, w, h, total, info.SuggestedSplit) : null;
            var perMode = ((int[])typeof(DashLedDetector).GetField("Modes", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))
                .Select(t => (DashPhotoLayout)detectAt.Invoke(null, new object[] { px, w, h, t })).ToList();
            var counts = string.Join(" ", perMode.Select(l => l == null ? "x" : $"{l.Left.Count}/{l.Top.Count}/{l.Right.Count}"));
            var closest = perMode.Where(l => l != null).OrderBy(l => Math.Abs(l.Total - total)).FirstOrDefault();
            if (layout != null) ok++; else fail++;
            if (layout != null && !exclude.Contains(info.DeviceTypeId))
            {
                string sha;
                using (var h256 = System.Security.Cryptography.SHA256.Create()) sha = BitConverter.ToString(h256.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
                Func<System.Collections.Generic.List<PhotoLed>, string> leds = list => "[" + string.Join(", ", list.Select(l => string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0:0.#}, {1:0.#}, {2:0.#}, {3:0.#}]", l.X, l.Y, l.Width, l.Height))) + "]";
                var nl = "\n";
                json.Add("    {" + nl +
                         "      \"id\": \"" + info.DeviceTypeId + "\"," + nl +
                         "      \"name\": \"" + info.Name.Replace("\"", "\\\"") + "\"," + nl +
                         "      \"sha256\": \"" + sha + "\"," + nl +
                         "      \"width\": " + w + ", \"height\": " + h + "," + nl +
                         "      \"left\": " + leds(layout.Left) + "," + nl +
                         "      \"top\": " + leds(layout.Top) + "," + nl +
                         "      \"right\": " + leds(layout.Right) + nl +
                         "    }");
            }
            Console.WriteLine($"{tag}  {(layout != null ? "OK " + layout.Left.Count + "/" + layout.Top.Count + "/" + layout.Right.Count : "FAIL")}  [{counts}] {w}x{h} {info.DeviceTypeId}");
            tiles.Add(Tile(src, layout ?? closest, $"{(layout != null ? "OK" : "FAIL")} {info.Name} n={total}"));
        }

        for (var sheet = 0; sheet * 12 < tiles.Count; sheet++)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.DimGray, null, new Rect(0, 0, 3 * 400, 4 * 280));
                for (var i = 0; i < 12 && sheet * 12 + i < tiles.Count; i++) dc.DrawImage(tiles[sheet * 12 + i], new Rect(i % 3 * 400, i / 3 * 280, 398, 278));
            }

            var rtb = new RenderTargetBitmap(1200, 1120, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var f = File.Create(outDir + "sheet" + sheet + ".png")) enc.Save(f);
        }

        if (args.Length > 0) File.WriteAllText(args[0], "{\n  \"version\": 1,\n  \"devices\": [\n" + string.Join(",\n", json) + "\n  ]\n}\n", new System.Text.UTF8Encoding(false));
        Console.WriteLine($"ok={ok} fail={fail} nopicture={nopic} nocount={nocount}");
    }

    static string Safe(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    static BitmapSource Tile(BitmapSource src, DashPhotoLayout layout, string label)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, src.PixelWidth, src.PixelHeight + 30));
            dc.DrawImage(src, new Rect(0, 30, src.PixelWidth, src.PixelHeight));
            dc.DrawText(new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, label.StartsWith("OK") ? Brushes.LightGreen : Brushes.OrangeRed, 1), new Point(4, 2));
            dc.PushTransform(new TranslateTransform(0, 30));
            if (layout != null)
            {
                Draw(dc, layout.Left, Brushes.Cyan);
                Draw(dc, layout.Top, Brushes.Yellow);
                Draw(dc, layout.Right, Brushes.Magenta);
            }
        }

        var rtb = new RenderTargetBitmap(src.PixelWidth, src.PixelHeight + 30, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static void Draw(DrawingContext dc, System.Collections.Generic.List<PhotoLed> leds, Brush brush)
    {
        var pen = new Pen(brush, 1.5);
        for (var i = 0; i < leds.Count; i++)
        {
            var l = leds[i];
            dc.DrawRectangle(null, pen, new Rect(l.X - l.Width / 2 - 2, l.Y - l.Height / 2 - 2, l.Width + 4, l.Height + 4));
            if (i == 0) dc.DrawEllipse(brush, null, new Point(l.X, l.Y), 2, 2);
        }
    }
}
