using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeTheLights.Settings;
using SimHub.Plugins.Devices;

namespace FreeTheLights.SimHubData
{
    /// <summary>
    /// The devices SimHub ships, read at runtime:
    /// <list type="bullet">
    /// <item>LED channels and counts from the .shdevice defaults (DevicesDefaults, DevicesDefinitions, offline templates);</item>
    /// <item>brand and the link to the device's button layout from SimHub's device registries;</item>
    /// <item>button layouts (photo + LED positions) from SimHub's DefaultColorsEditor controls, loaded when picked.</item>
    /// </list>
    /// All of this is SimHub's internal data and code, so everything is guarded: when something can't be read, that
    /// device (or the whole catalog) is simply missing and FreeTheLights falls back to its generic devices.
    /// </summary>
    public static class SimHubCatalog
    {
        private const string LayoutPrefix = ShDeviceParser.LayoutPrefix;

        private static readonly object Sync = new object();
        private static Task<IReadOnlyList<SimHubDeviceInfo>> loading;
        private static readonly Dictionary<string, Task<DashPhotoResult>> DashPhotos = new Dictionary<string, Task<DashPhotoResult>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Called with a message when something could not be read (logging).</summary>
        public static Action<string, Exception> Log { get; set; }

        /// <summary>
        /// Starts reading the catalog in the background (once); the result is cached. A read that failed is started again
        /// by the next call, so the wizard isn't left without SimHub's devices for the rest of the session.
        /// </summary>
        public static Task<IReadOnlyList<SimHubDeviceInfo>> LoadAsync()
        {
            lock (Sync)
            {
                if (loading == null || loading.IsFaulted || loading.IsCanceled) loading = Task.Run(() => Load());
                return loading;
            }
        }

        /// <summary>Every SimHub device with at least one LED channel, sorted by brand and name.</summary>
        public static IReadOnlyList<SimHubDeviceInfo> Load()
        {
            var devices = new Dictionary<string, SimHubDeviceInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var json in DeviceDefaults())
            {
                try
                {
                    var info = ShDeviceParser.Parse(json);
                    if (info != null && !devices.ContainsKey(info.DeviceTypeId)) devices[info.DeviceTypeId] = info;
                }
                catch (Exception ex)
                {
                    Log?.Invoke("FreeTheLights: could not read a SimHub device file", ex);
                }
            }

            var layouts = LayoutTypes();
            var registered = RegisteredDevices(layouts);
            foreach (var info in devices.Values)
            {
                if (registered.TryGetValue(info.DeviceTypeId, out var known))
                {
                    if (!string.IsNullOrWhiteSpace(known.brand)) info.Brand = known.brand.Trim();
                    info.ButtonLayoutName = known.layout;
                }

                if (info.ButtonLayoutName == null && info.Has(LedChannel.Buttons)) info.ButtonLayoutName = ShDeviceParser.MatchLayoutName(info.Name, layouts.Keys);
                var logo = Path.Combine("DevicesLogos", info.DeviceTypeId + ".png");
                if (File.Exists(SimHubPaths.Resolve(logo))) info.ProductImageFile = logo;
            }

            return devices.Values
                .Where(d => d.Channels.Count > 0)
                .OrderBy(d => d.Brand, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// SimHub's button layout of a device: photo and LED positions. Creates SimHub's WPF control, so call it on
        /// the UI thread. Null when the device has no layout or it can't be read.
        /// </summary>
        public static SimHubButtonLayout LoadButtonLayout(SimHubDeviceInfo info)
        {
            if (info?.ButtonLayoutName == null) return null;
            try
            {
                var type = LoadableTypes(typeof(DeviceDescriptor).Assembly).FirstOrDefault(t => t.Name == info.ButtonLayoutName && typeof(FrameworkElement).IsAssignableFrom(t));
                if (type == null) return null;
                var editor = (FrameworkElement)Activator.CreateInstance(type);
                var layout = new SimHubButtonLayout();
                var all = LogicalDescendants(editor).ToList();

                // The photo: its place on SimHub's canvas, so the markers can be stored relative to the photo. Outside
                // SimHub its "siteoforigin" source doesn't load; SimHub names the photo after the layout anyway.
                var image = all.OfType<Image>().FirstOrDefault();
                var photoLeft = image == null || double.IsNaN(Canvas.GetLeft(image)) ? 0 : Canvas.GetLeft(image);
                var photoTop = image == null || double.IsNaN(Canvas.GetTop(image)) ? 0 : Canvas.GetTop(image);
                layout.CanvasWidth = image != null && !double.IsNaN(image.Width) && image.Width > 0 ? image.Width : 1000;
                layout.PhotoFile = RelativePhotoPath(image?.Source) ?? ConventionalPhoto(info.ButtonLayoutName);
                var photo = LoadBitmap(layout.PhotoFile);
                if (photo != null && photo.PixelWidth > 0) layout.CanvasHeight = layout.CanvasWidth * photo.PixelHeight / photo.PixelWidth;

                foreach (var marker in all.OfType<Control>().Where(c => c.GetType().Name == "DefaultButtonColor"))
                {
                    var text = marker.GetType().GetProperty("Text")?.GetValue(marker) as string;
                    if (!int.TryParse(text, out var number)) continue;
                    var size = !double.IsNaN(marker.Width) && marker.Width > 0 ? marker.Width : 50;
                    var height = !double.IsNaN(marker.Height) && marker.Height > 0 ? marker.Height : size;
                    var left = Canvas.GetLeft(marker);
                    var top = Canvas.GetTop(marker);
                    layout.Markers.Add(new ButtonMarker
                    {
                        LedNumber = number,
                        X = (double.IsNaN(left) ? 0 : left) + size / 2 - photoLeft,
                        Y = (double.IsNaN(top) ? 0 : top) + height / 2 - photoTop,
                        Size = size,
                        Visible = marker.Visibility == Visibility.Visible,
                    });
                }

                return layout.Markers.Count == 0 ? null : layout;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"FreeTheLights: could not read SimHub's button layout of '{info.Name}'", ex);
                return null;
            }
        }

        /// <summary>
        /// A dashboard's LED positions on its product picture (see <see cref="DashPhotoLayouts"/>): from the built-in
        /// list when the picture is unchanged, else found on the picture. Null when the device has no readable picture.
        /// Read in the background, once per device and session (the result is cached); the task never fails.
        /// </summary>
        public static Task<DashPhotoResult> LoadDashPhotoAsync(SimHubDeviceInfo info)
        {
            if (info?.ProductImageFile == null) return Task.FromResult<DashPhotoResult>(null);
            lock (DashPhotos)
            {
                if (!DashPhotos.TryGetValue(info.DeviceTypeId, out var task))
                {
                    task = Task.Run(() => ReadDashPhoto(info));
                    DashPhotos[info.DeviceTypeId] = task;
                }

                return task;
            }
        }

        /// <summary>
        /// <see cref="LoadDashPhotoAsync"/>, waiting for the result: only for a caller that can't wait asynchronously.
        /// Detection takes a moment, so start it early with LoadDashPhotoAsync where possible.
        /// </summary>
        public static DashPhotoResult LoadDashPhoto(SimHubDeviceInfo info) => LoadDashPhotoAsync(info).GetAwaiter().GetResult();

        /// <summary>Reads the picture and finds the LEDs. Uses WPF's imaging only inside, so it runs on any thread.</summary>
        private static DashPhotoResult ReadDashPhoto(SimHubDeviceInfo info)
        {
            DashPhotoResult result = null;
            try
            {
                var bytes = File.ReadAllBytes(SimHubPaths.Resolve(info.ProductImageFile));
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);

                BitmapSource picture;
                using (var stream = new MemoryStream(bytes))
                {
                    picture = new FormatConvertedBitmap(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
                }

                int width = picture.PixelWidth, height = picture.PixelHeight;
                int[] Pixels()
                {
                    var pixels = new int[width * height];
                    picture.CopyPixels(pixels, width * 4, 0);
                    return pixels;
                }

                result = DashPhotoLayouts.Resolve(info.DeviceTypeId, info.ProductImageFile, hash, width, height, Pixels, info.CountOf(LedChannel.Leds), info.SuggestedSplit);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"FreeTheLights: could not read SimHub's picture of '{info.Name}'", ex);
            }

            return result;
        }

        /// <summary>The JSON of every device defaults file SimHub ships.</summary>
        private static IEnumerable<string> DeviceDefaults()
        {
            var folder = SimHubPaths.Folder;
            var files = new List<string>();
            AddFiles(files, Path.Combine(folder, "DevicesDefaults"), "*.shdevice", SearchOption.TopDirectoryOnly);
            AddFiles(files, Path.Combine(folder, "DevicesDefinitions"), "defaults.json", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string json = null;
                try
                {
                    json = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"FreeTheLights: could not read '{file}'", ex);
                }

                if (json != null) yield return json;
            }

            var templates = new List<string>();
            AddFiles(templates, Path.Combine(folder, "DevicesDefaults"), "*.shdevicetemplate", SearchOption.AllDirectories);
            foreach (var template in templates)
            {
                string json = null;
                try
                {
                    using (var zip = ZipFile.OpenRead(template))
                    {
                        var entry = zip.GetEntry("defaults.json");
                        if (entry != null)
                        {
                            using (var reader = new StreamReader(entry.Open())) json = reader.ReadToEnd();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"FreeTheLights: could not read '{template}'", ex);
                }

                if (json != null) yield return json;
            }
        }

        private static void AddFiles(List<string> files, string folder, string pattern, SearchOption option)
        {
            try
            {
                if (Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder, pattern, option).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log?.Invoke($"FreeTheLights: could not list '{folder}'", ex);
            }
        }

        /// <summary>SimHub's button layout controls by type name.</summary>
        private static Dictionary<string, Type> LayoutTypes()
        {
            var result = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var type in LoadableTypes(typeof(DeviceDescriptor).Assembly))
            {
                if (type.Name.StartsWith(LayoutPrefix, StringComparison.Ordinal) && type.Name.Length > LayoutPrefix.Length && type.Name != LayoutPrefix + "Standard"
                    && typeof(FrameworkElement).IsAssignableFrom(type))
                {
                    result[type.Name] = type;
                }
            }

            return result;
        }

        /// <summary>
        /// Brand and button layout per device type id, from SimHub's device registries. The layout is found by following
        /// the device's factory to the layout control it references (SimHub has no direct link).
        /// </summary>
        private static Dictionary<string, (string brand, string layout)> RegisteredDevices(Dictionary<string, Type> layouts)
        {
            var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
            var assembly = typeof(DeviceDescriptor).Assembly;
            foreach (var type in LoadableTypes(assembly).Where(t => typeof(IDeviceDescriptorsRegistry).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface))
            {
                try
                {
                    if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                    var registry = (IDeviceDescriptorsRegistry)Activator.CreateInstance(type);
                    foreach (var descriptor in registry.GetDevices() ?? Enumerable.Empty<DeviceDescriptor>())
                    {
                        if (string.IsNullOrWhiteSpace(descriptor?.DeviceTypeID)) continue;
                        string layout = null;
                        try
                        {
                            layout = FindLayout(descriptor.Factory?.Method, assembly, layouts, 0, new HashSet<MethodBase>());
                        }
                        catch (Exception)
                        {
                            // No layout for this device.
                        }

                        result[descriptor.DeviceTypeID.Trim()] = (descriptor.Brand, layout);
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"FreeTheLights: could not read SimHub's device registry '{type.Name}'", ex);
                }
            }

            return result;
        }

        /// <summary>
        /// The layout control a method references (typeof), directly or through the methods it calls (depth ≤ 3).
        /// Reads the method's IL; only the operands of call/newobj/ldftn/ldtoken are resolved.
        /// </summary>
        private static string FindLayout(MethodBase method, Assembly assembly, Dictionary<string, Type> layouts, int depth, HashSet<MethodBase> seen)
        {
            if (method == null || depth > 3 || !seen.Add(method) || method.DeclaringType?.Assembly != assembly) return null;
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return null;

            var module = method.Module;
            var calls = new List<MethodBase>();
            for (var i = 0; i + 4 < il.Length; i++)
            {
                try
                {
                    switch (il[i])
                    {
                        case 0xD0: // ldtoken
                            var type = module.ResolveType(BitConverter.ToInt32(il, i + 1));
                            if (layouts.ContainsKey(type.Name)) return type.Name;
                            break;
                        case 0x28: // call
                        case 0x6F: // callvirt
                        case 0x73: // newobj
                            calls.Add(module.ResolveMethod(BitConverter.ToInt32(il, i + 1)));
                            break;
                        case 0xFE: // ldftn (FE 06)
                            if (il[i + 1] == 0x06 && i + 5 < il.Length) calls.Add(module.ResolveMethod(BitConverter.ToInt32(il, i + 2)));
                            break;
                    }
                }
                catch (Exception)
                {
                    // Not an operand of this kind; scanning raw bytes hits those.
                }
            }

            foreach (var call in calls)
            {
                if (call is ConstructorInfo ctor && layouts.ContainsKey(ctor.DeclaringType?.Name ?? string.Empty)) return ctor.DeclaringType.Name;
                var found = FindLayout(call, assembly, layouts, depth + 1, seen);
                if (found != null) return found;
            }

            return null;
        }

        private static IEnumerable<Type> LoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
        }

        /// <summary>SimHub's photo for a layout: DevicesUI\&lt;layout name&gt;.jpg or .png, if it exists.</summary>
        private static string ConventionalPhoto(string layoutName)
        {
            foreach (var extension in new[] { ".jpg", ".png" })
            {
                var path = Path.Combine("DevicesUI", layoutName + extension);
                if (File.Exists(SimHubPaths.Resolve(path))) return path;
            }

            return null;
        }

        private static BitmapSource LoadBitmap(string relativePath)
        {
            var path = SimHubPaths.Resolve(relativePath);
            if (path == null || !File.Exists(path)) return null;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>"pack://siteoforigin:,,,/DevicesUI/x.jpg" or a file URI → path relative to the SimHub folder.</summary>
        private static string RelativePhotoPath(ImageSource source)
        {
            var uri = (source as BitmapImage)?.UriSource?.ToString() ?? source?.ToString();
            if (string.IsNullOrEmpty(uri)) return null;
            if (uri.IndexOf("DevicesUI", StringComparison.OrdinalIgnoreCase) < 0) return null;
            var marker = uri.IndexOf(",,,/", StringComparison.Ordinal);
            var path = marker >= 0 ? uri.Substring(marker + 4) : uri;
            if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) path = new Uri(path).LocalPath;
            path = Uri.UnescapeDataString(path).Replace('/', '\\');
            var folder = SimHubPaths.Folder;
            if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)) path = path.Substring(folder.Length);
            return path.TrimStart('\\');
        }

        private static IEnumerable<object> LogicalDescendants(object root)
        {
            if (!(root is DependencyObject d)) yield break;
            foreach (var child in LogicalTreeHelper.GetChildren(d))
            {
                yield return child;
                foreach (var x in LogicalDescendants(child)) yield return x;
            }
        }
    }
}
