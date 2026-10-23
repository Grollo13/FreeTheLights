using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public struct PT { public int X, Y; public PT(int x, int y) { X = x; Y = y; } }

public class Sp { public Point Center; public object Group; public int Logical; }

public static class Harness
{
    static string simhub = (Environment.GetEnvironmentVariable("SIMHUB_INSTALL_PATH") ?? @"C:\Program Files (x86)\SimHub").TrimEnd('\\') + @"\";
    static string pluginDir;

    [STAThread]
    public static int Main(string[] args)
    {
        pluginDir = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var dir in new[] { pluginDir, simhub })
            {
                var p = Path.Combine(dir, name + ".dll");
                if (File.Exists(p)) return Assembly.LoadFrom(p);
                p = Path.Combine(dir, name + ".exe");
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            return 1;
        }
    }

    static void Run(string[] args)
    {
        System.Diagnostics.PresentationTraceSources.Refresh();
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
        System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
        var outDir = args[1];
        var mode = args[2];
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var src in new[]
        {
            "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Colors.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Accents/Blue.xaml",
            "pack://application:,,,/MahApps.Metro;component/Styles/Accents/BaseDark.xaml",
            "pack://application:,,,/SimHub.Plugins;component/Styles/SimHubStyles.xaml",
        })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(src) });
        }

        if (mode == "wizard") Wizard(outDir, args);
        else Settings(outDir, args);
    }

    /// <summary>
    /// HARNESS_UPDATE: gives the plugin an update service in that state (available, installing, installed, failed,
    /// uptodate, checkfailed, off) with a fake release 0.2.0 of an installed 0.1.0; nothing is downloaded.
    /// </summary>
    static void FakeUpdates(Assembly asm, object plugin, dynamic settings)
    {
        var mode = Environment.GetEnvironmentVariable("HARNESS_UPDATE");
        if (string.IsNullOrEmpty(mode)) return;

        var serviceType = asm.GetType("FreeTheLights.Updates.UpdateService");
        var releaseType = asm.GetType("FreeTheLights.Updates.ReleaseInfo");
        var folder = Path.Combine(Path.GetTempPath(), "FreeTheLights-harness-update");
        var installer = Activator.CreateInstance(asm.GetType("FreeTheLights.Updates.UpdateInstaller"), Path.Combine(folder, "FreeTheLights.dll"), null);
        var service = Activator.CreateInstance(serviceType, folder, new Version(0, 1, 0, 0), installer, null, null, null);

        dynamic release = Activator.CreateInstance(releaseType);
        var prefix = (string)releaseType.GetField("DownloadUrlPrefix").GetValue(null);
        release.Tag = "v0.2.0";
        release.PageUrl = (string)releaseType.GetField("ReleasesPageUrl").GetValue(null) + "/tag/v0.2.0";
        release.DllUrl = prefix + "v0.2.0/FreeTheLights.dll";
        release.ChecksumUrl = prefix + "v0.2.0/FreeTheLights.dll.sha256";

        string status, error = null;
        switch (mode)
        {
            case "available": status = "Available"; break;
            case "installing": status = "Installing"; break;
            case "installed": status = "Installed"; break;
            case "failed": status = "InstallFailed"; error = "Updating needs administrator rights. Allow it, or download the update from GitHub."; break;
            case "uptodate": status = "UpToDate"; release.Tag = "v0.1.0"; break;
            case "checkfailed": status = "CheckFailed"; release = null; error = "Response status code does not indicate success: 403 (rate limit exceeded)."; break;
            default: status = "NotChecked"; release = null; settings.CheckForUpdates = false; break;
        }

        void SetField(string name, object value) => serviceType.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(service, value);
        SetField("status", Enum.Parse(asm.GetType("FreeTheLights.Updates.UpdateStatus"), status));
        SetField("latest", (object)release);
        SetField("error", error);
        SetField("checkedUtc", (DateTime?)new DateTime(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc));
        plugin.GetType().GetProperty("Updates").SetValue(plugin, service);
    }

    static void Settings(string outDir, string[] args)
    {
        var asm = Assembly.LoadFrom(Path.Combine(pluginDir, "FreeTheLights.dll"));
        var pluginType = asm.GetType("FreeTheLights.FreeTheLightsPlugin");
        var plugin = Activator.CreateInstance(pluginType);
        var settingsType = asm.GetType("FreeTheLights.Settings.PluginSettings");
        dynamic settings = Activator.CreateInstance(settingsType);
        pluginType.GetProperty("Settings").SetValue(plugin, settings);
        FakeUpdates(asm, plugin, settings);

        var wizType = asm.GetType("FreeTheLights.UI.DeviceWizardViewModel");
        var templType = asm.GetType("FreeTheLights.UI.DeviceTemplate");
        object Build(string template, string name, int dial, int side)
        {
            dynamic w = Activator.CreateInstance(wizType, new object[] { null, null, null });
            w.Template = (dynamic)Enum.Parse(templType, template);
            w.Name = name;
            if (dial >= 0) w.SelectedDialPreset = ((dynamic)w.DialPresets)[dial];
            if (side >= 0) w.SelectedSidePreset = ((dynamic)w.SidePresets)[side];
            return w.Build();
        }

        var devices = (System.Collections.IList)settings.Devices;
        var presetName = Environment.GetEnvironmentVariable("HARNESS_PRESET");
        if (!string.IsNullOrEmpty(presetName))
        {
            // A SimHub device, built through the wizard with SimHub's catalog and layouts.
            var catalogType = asm.GetType("FreeTheLights.SimHubData.SimHubCatalog");
            var infoType = asm.GetType("FreeTheLights.Settings.SimHubDeviceInfo");
            var layoutType = asm.GetType("FreeTheLights.Settings.SimHubButtonLayout");
            var loader = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(infoType, layoutType), catalogType.GetMethod("LoadButtonLayout"));
            var photoType = asm.GetType("FreeTheLights.Settings.DashPhotoResult");
            var photoLoader = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(infoType, photoType), catalogType.GetMethod("LoadDashPhoto"));
            dynamic wiz = Activator.CreateInstance(wizType, loader, photoLoader, null);
            wiz.SetSimHubDevices((dynamic)catalogType.GetMethod("Load").Invoke(null, null));
            foreach (var choice in wiz.Choices) { if (((string)choice.Title).Contains(presetName)) { wiz.SelectedChoice = choice; break; } }
            var kindName = Environment.GetEnvironmentVariable("HARNESS_KIND");
            if (!string.IsNullOrEmpty(kindName)) wiz.SelectedKind = (dynamic)Enum.Parse(asm.GetType("FreeTheLights.Settings.DeviceType"), kindName);
            wiz.Step = 2;
            wiz.Step = 3;
            if (Environment.GetEnvironmentVariable("HARNESS_GENERIC") == "1") wiz.UseGenericDrawing = true;
            devices.Add(wiz.Build());
        }

        if (Environment.GetEnvironmentVariable("HARNESS_DIALOG") == "1" && devices.Count > 0)
        {
            var dialog = (FrameworkElement)Activator.CreateInstance(asm.GetType("FreeTheLights.UI.PhotoLedDialog"), devices[0]);
            var dialogWindow = new Window
            {
                Width = 920, Height = 560, Left = -20000, Top = 0,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                Content = new Border { Padding = new Thickness(16), Child = dialog },
            };
            dialogWindow.SetResourceReference(Control.ForegroundProperty, "BlackBrush");
            dialogWindow.Show();
            Pump(400);
            Save((FrameworkElement)dialogWindow.Content, Path.Combine(outDir, "photo_dialog.png"));
            return;
        }

        devices.Add(Build("Wheel", "Simucube wheel", 2, 3));
        devices.Add(Build("LedFlag", "LED flag", -1, -1));
        devices.Add(Build("LedBar", "Desk LED bar", -1, -1));
        devices.Add(Build("Dashboard", "Dash", -1, -1));
        settings.EnsureDefaults();
        ((dynamic)devices[2]).Enabled = false;
        if (Environment.GetEnvironmentVariable("HARNESS_TWOPARTS") == "1")
        {
            var partType = asm.GetType("FreeTheLights.Settings.DevicePart");
            dynamic p2 = partType.GetMethod("CreateDefault").Invoke(null, new object[] { "Buttons" });
            p2.ApplyFrontPlate("1-2", "2x3");
            ((dynamic)devices[0]).Parts.Add(p2);
        }
        if (Environment.GetEnvironmentVariable("HARNESS_TRACETEST") == "1")
        {
            var tb = new TextBlock(); tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("NoSuchProperty") { Source = new object() });
            var w0 = new Window { Width = 100, Height = 100, Left = -20000, Content = tb, ShowInTaskbar = false, WindowStyle = WindowStyle.None }; w0.Show(); Pump(100); w0.Close();
        }

        if (Environment.GetEnvironmentVariable("HARNESS_DIALOG") == "wiring")
        {
            // The device page's wiring dialog for the first device's first part (before the fake frames: its preview draws the pattern).
            dynamic first = devices[0];
            var wiringDialog = (FrameworkElement)Activator.CreateInstance(asm.GetType("FreeTheLights.UI.WiringDialog"), plugin, first, first.Parts[0], new Action(() => ((dynamic)plugin).PublishDevices()));
            var wiringWindow = new Window
            {
                Width = 900, Height = 620, Left = -20000, Top = 0,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
                Content = new Border { Padding = new Thickness(16), Child = wiringDialog },
            };
            wiringWindow.SetResourceReference(Control.ForegroundProperty, "BlackBrush");
            wiringWindow.Show();
            Pump(600);
            Save((FrameworkElement)wiringWindow.Content, Path.Combine(outDir, "wiring_dialog.png"));
            return;
        }

        // Fake frames for every part of the first device.
        var ledColorType = asm.GetType("FreeTheLights.Core.LedColor");
        var parse = ledColorType.GetMethod("Parse", new[] { typeof(string) });
        var framesField = pluginType.GetField("frames", BindingFlags.NonPublic | BindingFlags.Instance);
        var dict = framesField.GetValue(plugin);
        foreach (dynamic dev in ((dynamic)devices[0]).Parts)
        {
            int total = dev.TotalLeds;
            var frame = Array.CreateInstance(ledColorType, total);
            foreach (dynamic g in dev.Groups)
            {
                for (int i = 0; i < (int)g.LedCount; i++)
                {
                    string hex;
                    if ((int)g.Kind == 0) hex = i < 4 ? "#FF00FF00" : i < 8 ? "#FFFF0000" : i < 10 ? "#FF0040FF" : "#FF000000";
                    else if ((int)g.Kind == 1) hex = "#FFFFD000";
                    else if ((int)g.Kind == 3) hex = ((int)g.StartIndex % 3 == 0) ? "#FFFF6000" : ((int)g.StartIndex % 3 == 1) ? "#FF00C8FF" : "#FF000000";
                    else hex = (i % 2 == 0) ? "#FF00C8FF" : "#FFFF6000";
                    var index = (int)g.StartIndex + i;
                    if (index < total) frame.SetValue(parse.Invoke(null, new object[] { hex }), index);
                }
            }

            // Rendered "in the future", so the fake frames never get too old to show.
            dict.GetType().GetMethod("Put").Invoke(dict, new object[] { (Guid)dev.Id, frame, long.MaxValue / 2 });
        }

        if (Environment.GetEnvironmentVariable("HARNESS_EMPTY") == "1") devices.Clear();
        if (Environment.GetEnvironmentVariable("HARNESS_CHIPS") == "1") { dynamic tm = pluginType.GetProperty("TestMode").GetValue(plugin); tm.Yellow = true; tm.Abs = true; }
        var controlType = asm.GetType("FreeTheLights.UI.SettingsControl");
        var control = (FrameworkElement)Activator.CreateInstance(controlType, plugin);
        int width = int.Parse(args[3]), height = int.Parse(args[4]);
        var window = new Window
        {
            Width = width, Height = height, Left = -20000, Top = 0,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Content = control,
        };
        window.SetResourceReference(Control.ForegroundProperty, "BlackBrush");
        window.Show();
        Pump(400);
        vmRootObj = control.DataContext;

        Console.WriteLine("control " + control.ActualWidth + "x" + control.ActualHeight + " window " + window.ActualWidth);
        foreach (var g in FindAll<Grid>(control).Take(1)) foreach (var cd in g.ColumnDefinitions) Console.WriteLine(" col " + cd.ActualWidth);
        foreach (var sp in FindAll<StackPanel>(control).Where(x => x.MaxWidth == 520)) Console.WriteLine(" empty panel " + sp.ActualWidth + " at " + sp.TranslatePoint(new Point(0, 0), control));
        if (Environment.GetEnvironmentVariable("HARNESS_PREVIEW_CLICK") == "1")
        {
            ClickTest(window, (FrameworkElement)control.FindName("Preview"), (dynamic)devices[0]);
            return;
        }

        if (Environment.GetEnvironmentVariable("HARNESS_PREVIEW_SELECT") == "1")
        {
            var preview = (FrameworkElement)control.FindName("Preview");
            Console.WriteLine("preview " + preview.GetType().Name + " " + preview.ActualWidth + "x" + preview.ActualHeight);
            dynamic firstDevice = devices[0];
            var groups = ((System.Collections.IEnumerable)((dynamic)firstDevice.Parts[0]).Groups).Cast<dynamic>().ToList();
            var elementGroups = groups.Where(g => (bool)g.IsElement).ToList();
            var otherGroups = groups.Where(g => !(bool)g.IsElement).ToList();
            Console.WriteLine("groups " + groups.Count + " elements " + elementGroups.Count + " other " + otherGroups.Count);
            var setSel = preview.GetType().GetMethod("SetSelection");
            Pump(300);
            Save(preview, Path.Combine(outDir, "select_none.png"), 3);
            if (elementGroups.Count > 0)
            {
                var ids = new System.Collections.Generic.List<Guid>();
                foreach (var g in elementGroups.Take(2)) ids.Add((Guid)g.Id);
                setSel.Invoke(preview, new object[] { ids, (Guid?)ids[0], 0 });
                Pump(300);
                Save(preview, Path.Combine(outDir, "select_elements.png"), 3);
            }

            if (otherGroups.Count > 0)
            {
                // The first group with at least 3 LEDs, focused on LED index 2.
                var g = otherGroups.FirstOrDefault(x => (int)x.LedCount >= 3) ?? otherGroups[0];
                setSel.Invoke(preview, new object[] { new Guid[] { (Guid)g.Id }, (Guid?)(Guid)g.Id, 2 });
                Pump(300);
                Save(preview, Path.Combine(outDir, "select_group.png"), 3);
            }

            // Hover ring: set the private hover field of the interactive preview directly (the real mouse can't move offscreen).
            preview.GetType().GetProperty("IsInteractive").SetValue(preview, true);
            var spotsList = (System.Collections.IList)preview.GetType().GetField("spots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview);
            preview.GetType().GetField("hovered", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(preview, spotsList[spotsList.Count / 2]);
            Pump(300);
            Save(preview, Path.Combine(outDir, "select_hover.png"), 3);
            return;
        }

        var partName = Environment.GetEnvironmentVariable("HARNESS_PART");
        if (!string.IsNullOrEmpty(partName))
        {
            dynamic vmRoot = control.DataContext;
            foreach (var part in vmRoot.SelectedDevice.Parts) { if ((string)part.Name == partName) vmRoot.SelectedPart = part; }
            Pump(300);
        }

        var deviceIndex = Environment.GetEnvironmentVariable("HARNESS_DEVICE");
        if (!string.IsNullOrEmpty(deviceIndex)) { dynamic r0 = control.DataContext; { dynamic dev0 = devices[int.Parse(deviceIndex)]; r0.SelectedDevice = dev0; } Pump(300); }
        var selectMode = Environment.GetEnvironmentVariable("HARNESS_SELECT");
        if (!string.IsNullOrEmpty(selectMode))
        {
            dynamic root = control.DataContext;
            foreach (var part in root.SelectedDevice.Parts) Console.WriteLine("part '" + (string)part.Name + "' elements=" + ((System.Collections.IEnumerable)part.Elements).Cast<object>().Count());
            dynamic selPart = root.SelectedPart;
            if (selectMode == "element" || selectMode == "multi")
            {
                if (!((System.Collections.IEnumerable)selPart.Elements).Cast<object>().Any())
                {
                    foreach (var part in root.SelectedDevice.Parts) { if (((System.Collections.IEnumerable)part.Elements).Cast<object>().Any()) { root.SelectedPart = part; break; } }
                    selPart = root.SelectedPart;
                    Pump(200);
                }
            }

            var all = ((System.Collections.IEnumerable)selPart.Groups).Cast<dynamic>().ToList();
            var elementList = all.Where(g => (bool)g.IsElement).ToList();
            var otherList = all.Where(g => !(bool)g.IsElement).ToList();
            var elementType = asm.GetType("FreeTheLights.Settings.ElementType");
            if (selectMode == "press" || selectMode == "capture")
            {
                // A button with a press color and its input, or a dial waiting for its second input; inputs from a fake source.
                var wanted = selectMode == "press" ? "Button" : "Dial";
                var target = elementList.FirstOrDefault(g => g.ElementType.ToString() == wanted);
                if (target == null) Console.WriteLine("HARNESS_SELECT=" + selectMode + ": the part has no " + wanted.ToLowerInvariant() + ", nothing selected");
                else
                {
                    root.Inspector.InputSource = (dynamic)Activator.CreateInstance(asm.GetType("FreeTheLights.Core.InputState"));
                    target.PressColor = "#FF000000";
                    if (selectMode == "press") target.ButtonInput = "JoystickPlugin.Fanatec_Podium_Wheel_B05";
                    else target.ClockwiseInput = "JoystickPlugin.Fanatec_Podium_Wheel_B20";

                    root.Inspector.Select(selPart, target, 0, false);
                    if (selectMode == "capture") { dynamic slot = Enum.Parse(asm.GetType("FreeTheLights.Settings.InputSlot"), "CounterClockwise"); root.Inspector.StartCapture(slot); }
                }
            }
            else if (selectMode == "element" && elementList.Count > 0) root.Inspector.Select(selPart, elementList[0], 0, false);
            else if (selectMode == "multi")
            {
                var buttons = elementList.Where(g => g.ElementType.ToString() == "Button").Count();
                { dynamic typeArg = buttons > 0 ? Enum.Parse(elementType, "Button") : null; root.Inspector.SelectAll(selPart, typeArg); }
            }
            else if (selectMode == "multi-mixed") { elementList[0].StaticColor = "#FFFF0000"; { dynamic flagsArg = Enum.Parse(asm.GetType("FreeTheLights.Settings.LayerKind"), "Flags"); selPart.SetElementEffect(elementList[1], flagsArg); }  dynamic nullArg = null; root.Inspector.SelectAll(selPart, nullArg); }
            else if (selectMode == "group" && otherList.Count > 0)
            {
                var g = otherList.FirstOrDefault(x => (int)x.LedCount >= 3) ?? otherList[0];
                root.Inspector.Select(selPart, g, 2, false);
            }
            else if (selectMode == "matrix")
            {
                var g = otherList.FirstOrDefault(x => (bool)x.IsMatrix) ?? otherList[0];
                root.Inspector.Select(selPart, g, 5, false);
            }

            Pump(400);
            Console.WriteLine("inspector mode " + root.Inspector.Mode + " title '" + (string)root.Inspector.Title + "' subtitle '" + (string)root.Inspector.Subtitle + "'");
            DependencyObject up = preview0(control);
            while (up != null && !(up is Border b0 && b0.CornerRadius.TopLeft == 10 && b0.Padding.Left == 0 && b0.Padding.Top == 0)) up = VisualTreeHelper.GetParent(up);
            if (up != null) Save((FrameworkElement)up, Path.Combine(outDir, "card_" + selectMode + ".png"));

            // The press fields are at the bottom of the element panel: also save it scrolled to the end.
            if (up != null && (selectMode == "press" || selectMode == "capture"))
            {
                foreach (var scroller in FindAll<ScrollViewer>((DependencyObject)up).Where(s => s.IsVisible && s.ScrollableHeight > 0)) scroller.ScrollToEnd();
                Pump(200);
                Save((FrameworkElement)up, Path.Combine(outDir, "card_" + selectMode + "_end.png"));
            }
        }

        var tabs = FindAll<TabControl>(control).FirstOrDefault();
        var count = tabs == null ? 1 : tabs.Items.Count;
        var only = args.Length > 5 ? args[5] : "all";
        for (int t = 0; t < count; t++)
        {
            if (only != "all" && !only.Split(',').Contains(t.ToString())) continue;
            if (tabs != null) tabs.SelectedIndex = t;
            Pump(300);
            if (args.Length > 6 && args[6] == "drag")
            {
                // Drive DragReorder's private steps directly: the real mouse can't be moved offscreen.
                var dr = asm.GetType("FreeTheLights.UI.DragReorder");
                var enabledDp = (DependencyProperty)dr.GetField("IsEnabledProperty").GetValue(null);
                var stateDp = (DependencyProperty)dr.GetField("StateProperty", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                var list = FindAll<ItemsControl>(tabs).First(i => (bool)i.GetValue(enabledDp) && i.Items.Count >= 3);
                var names = new Func<string>(() => string.Join(", ", list.Items.Cast<object>().Select(o => o.GetType().GetProperty("DisplayName")?.GetValue(o) ?? o.GetType().GetProperty("Name")?.GetValue(o))));
                Console.WriteLine("before: " + names());
                var state = list.GetValue(stateDp);
                state.GetType().GetField("SourceIndex").SetValue(state, 0);
                var flags = BindingFlags.NonPublic | BindingFlags.Static;
                for (int k = 0; k < list.Items.Count; k++) { var c = list.ItemContainerGenerator.ContainerFromIndex(k) as FrameworkElement; Console.WriteLine(" container " + k + ": " + (c == null ? "null" : c.GetType().Name + " visible=" + c.IsVisible)); }
                Console.WriteLine("begin: " + dr.GetMethod("Begin", flags).Invoke(null, new object[] { list, state }));
                foreach (var offset in new[] { 60.0, 150.0 })
                {
                    dr.GetMethod("Track", flags).Invoke(null, new object[] { state, offset });
                    Pump(250);
                    Save(list, Path.Combine(outDir, "drag_" + t + "_" + offset + ".png"));
                }

                dr.GetMethod("Drop", flags).Invoke(null, new object[] { list, state });
                Pump(500);
                Save(list, Path.Combine(outDir, "drag_" + t + "_dropped.png"));
                Console.WriteLine("after:  " + names());
                continue;
            }

            if (args.Length > 6 && args[6] == "popup")
            {
                var picker = FindAll<UserControl>(tabs).First(u => u.GetType().Name == "ColorPickerButton");
                var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("PickerPopup");
                popup.IsOpen = true;
                Pump(300);
                Save((FrameworkElement)popup.Child, Path.Combine(outDir, "popup_" + t + ".png"));
                popup.IsOpen = false;
                continue;
            }

            var target = args.Length > 6 && args[6] == "tab" && tabs != null ? (FrameworkElement)tabs : control;
            Save(target, Path.Combine(outDir, (target == control ? "settings_" : "tab_") + t + ".png"));
        }
    }

    static void Wizard(string outDir, string[] args)
    {
        var asm = Assembly.LoadFrom(Path.Combine(pluginDir, "FreeTheLights.dll"));
        var wizardType = asm.GetType("FreeTheLights.UI.DeviceWizard");

        // HARNESS_HOST=1: SimHub has the device, Connect succeeds, and inputs can be pressed (FakeWizardHost).
        var fakeHost = Environment.GetEnvironmentVariable("HARNESS_HOST") == "1" ? FakeWizardHost.Create() : null;
        var wizard = (FrameworkElement)(fakeHost == null ? Activator.CreateInstance(wizardType) : Activator.CreateInstance(wizardType, fakeHost));
        dynamic vm = wizardType.GetProperty("ViewModel").GetValue(wizard);
        var window = new Window
        {
            Width = 860, Height = 640, Left = -20000, Top = 0,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)),
            Content = new Border { Padding = new Thickness(16), Child = wizard },
        };
        window.SetResourceReference(Control.ForegroundProperty, "BlackBrush");
        window.Show();
        // Wait for SimHub's device list, then optionally pick a SimHub device and walk the wizard's steps.
        for (var i = 0; i < 100 && ((System.Collections.ICollection)vm.Choices).Count <= 5; i++) Pump(100);
        Console.WriteLine("choices: " + ((System.Collections.ICollection)vm.Choices).Count + " status: " + vm.CatalogStatus);
        var pick = Environment.GetEnvironmentVariable("HARNESS_PRESET");
        var tag = string.IsNullOrEmpty(pick) ? "generic" : pick.Replace(" ", "_").Replace("\"", "").Replace("/", "_");
        if (!string.IsNullOrEmpty(pick))
        {
            vm.SearchText = pick;
            Pump(200);
            foreach (var choice in vm.Choices) { if (((string)choice.Title).Contains(pick)) { vm.SelectedChoice = choice; break; } }
            Console.WriteLine("picked: " + vm.SelectedChoice + " kind " + vm.SelectedKind + " (" + vm.KindReason + ")");
        }

        var kind = Environment.GetEnvironmentVariable("HARNESS_KIND");
        for (var n = 0; n < 12; n++)
        {
            Pump(300);
            var file = Path.Combine(outDir, "wizard_" + tag + "_" + n + "_step" + vm.Step);
            Save((FrameworkElement)window.Content, file + ".png");
            if (vm.IsLastStep) break;
            if (fakeHost != null && vm.Step == 2)
            {
                // The SimHub profile step: set up the profile, as a click on "Set up in SimHub".
                vm.Profile.ConnectCommand.Execute(null);
                Pump(300);
                Save((FrameworkElement)window.Content, file + "_connected.png");
            }

            if (fakeHost != null && vm.Step == 5 && vm.Wiring != null && ((System.Collections.ICollection)vm.Wiring.Groups).Count > 0)
            {
                // The wiring step: shorten the first group to 15 LEDs and add a column on each side.
                foreach (var group in vm.Wiring.Groups) { group.Count = 15; break; }
                vm.Wiring.AddCommand.Execute("Column");
                vm.Wiring.AddCommand.Execute("Column");
                Pump(300);
                Save((FrameworkElement)window.Content, file + "_edited.png");
            }

            if (fakeHost != null && vm.Step == 7)
            {
                // The buttons step: press two inputs (HARNESS_PRESSES: that many, saving each prompt), more than the 400 ms lockout apart.
                int presses;
                var each = int.TryParse(Environment.GetEnvironmentVariable("HARNESS_PRESSES"), out presses);
                if (!each) presses = 2;
                for (var p = 1; p <= presses; p++)
                {
                    FakeWizardHost.Press(fakeHost, "JoystickPlugin.Wheel_B" + p.ToString("00"));
                    Pump(500);
                    if (each) Save((FrameworkElement)window.Content, file + "_pressed" + p.ToString("00") + ".png");
                }

                Save((FrameworkElement)window.Content, file + "_pressed.png");
            }
            if (vm.Step == 1 && !string.IsNullOrEmpty(kind)) vm.SelectedKind = (dynamic)Enum.Parse(asm.GetType("FreeTheLights.Settings.DeviceType"), kind);
            vm.NextCommand.Execute(null);
        }

        Console.WriteLine("summary: " + vm.Summary);
        return;
        var ux = asm.GetType("FreeTheLights.UI.Ux");
        var iconDp = (DependencyProperty)ux.GetField("IconProperty").GetValue(null);
        var accentDp = (DependencyProperty)ux.GetField("AccentProperty").GetValue(null);
        vm.Step = 3;
        Pump(200);
        foreach (var cc in FindAll<ContentControl>(window).Where(c => c.GetType() == typeof(ContentControl)).Take(4))
        {
            var src = DependencyPropertyHelper.GetValueSource(cc, iconDp);
            Console.WriteLine("tile icon=" + cc.GetValue(iconDp) + " (" + src.BaseValueSource + ") accent=" + cc.GetValue(accentDp) + " parent=" + (cc.TemplatedParent == null ? "null" : cc.TemplatedParent.GetType().Name + " icon=" + cc.TemplatedParent.GetValue(iconDp)));
            Console.WriteLine("   size=" + cc.ActualWidth + "x" + cc.ActualHeight + " vis=" + cc.Visibility + " opacity=" + cc.Opacity + " children=" + VisualTreeHelper.GetChildrenCount(cc) + " fs=" + cc.FontSize);
            foreach (var fe in FindAll<FrameworkElement>(cc))
                Console.WriteLine("     " + fe.GetType().Name + " " + fe.ActualWidth + "x" + fe.ActualHeight + " op=" + fe.Opacity + (fe is Control c2 ? " fg=" + c2.Foreground : "") + (fe is Border b ? " bg=" + b.Background : ""));
        }

        // Reorder the wiring list: the color dots must follow the new order.
        vm.Step = 1;
        ((dynamic)vm.Groups).Move(0, 2);
        Pump(250);
        Save((FrameworkElement)window.Content, Path.Combine(outDir, "wizard_1_moved.png"));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetCursorPos(out PT p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    static System.Collections.Generic.List<string> clicks = new System.Collections.Generic.List<string>();
    static object vmRootObj;
    static string InspectorState()
    {
        dynamic r = vmRootObj;
        if (r == null) return "";
        var ins = r.Inspector;
        return " [inspector " + ins.Mode + " '" + (string)ins.Title + "' part=" + (string)(ins.Part == null ? "null" : ins.Part.Name) + " selectedPart=" + (string)(r.SelectedPart == null ? "null" : r.SelectedPart.Name) + " clicked=" + ins.ClickedLogical + "]";
    }

    static void OnClick(object sender, EventArgs e)
    {
        dynamic a = e;
        clicks.Add(a.Part == null ? "empty add=" + a.Add : (string)a.Group.Name + " logical=" + a.Logical + " physical=" + a.Physical + " add=" + a.Add);
    }

    static void ClickTest(Window window, FrameworkElement preview, dynamic device)
    {
        // Real mouse input: the window moves on screen for a moment (topmost), the cursor is restored afterwards.
        window.Left = 50; window.Top = 50; window.Topmost = true; window.Activate();
        Pump(500);
        PT saved; GetCursorPos(out saved);
        var type = preview.GetType();
        var spots = (System.Collections.IList)type.GetField("spots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview);
        var evt = type.GetEvent("LedClicked");
        evt.AddEventHandler(preview, Delegate.CreateDelegate(evt.EventHandlerType, typeof(Harness).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Static)));
        Func<Func<Sp, bool>, Sp> Find = pred => { foreach (var s in spots) { var sp = new Sp { Center = (Point)s.GetType().GetField("Center").GetValue(s), Group = s.GetType().GetField("Group").GetValue(s), Logical = (int)s.GetType().GetField("Logical").GetValue(s) }; if (pred(sp)) return sp; } return null; };
        Func<Point, PT> toScreen = p => { var sp = preview.PointToScreen(p); return new PT((int)Math.Round(sp.X), (int)Math.Round(sp.Y)); };
        Action<Point, string> moveTo = (p, label) =>
        {
            var s = toScreen(p);
            SetCursorPos(s.X, s.Y);
            Pump(250);
            Console.WriteLine("  at " + label + ": mouseOver=" + preview.IsMouseOver + " cursor=" + preview.Cursor + " hovered=" + (type.GetField("hovered", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview) == null ? "none" : "spot"));
        };
        Action down = () => { mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Pump(120); };
        Action up = () => { mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); Pump(250); };
        Action<string> expect = label => { Console.WriteLine("  -> " + label + ": " + (clicks.Count == 0 ? "no event" : string.Join(" | ", clicks)) + InspectorState()); clicks.Clear(); };

        try
        {
            moveTo(new Point(1, 1), "top-left corner");
            if (!preview.IsMouseOver) { Console.WriteLine("mouse is not over the preview; aborting the click test"); return; }

            var element = Find(s => (bool)((dynamic)s.Group).IsElement);
            var rowSpot = Find(s => !(bool)((dynamic)s.Group).IsElement && s.Logical == 4);
            var otherRow = Find(s => s.Logical == 6 && s.Group == rowSpot.Group);

            Console.WriteLine("1. not interactive (default): click an element");
            moveTo(element.Center, "element"); down(); up(); expect("expect no event");

            type.GetProperty("IsInteractive").SetValue(preview, true);
            Console.WriteLine("2. interactive: click an element");
            moveTo(element.Center, "element"); down(); up(); expect("expect element, logical 0");

            Console.WriteLine("3. click LED 5 of a row");
            moveTo(rowSpot.Center, "row led 5"); down(); up(); expect("expect row logical=4");

            Console.WriteLine("4. click slightly off the LED edge (radius 6 + 4)");
            moveTo(new Point((rowSpot.Center).X, (rowSpot.Center).Y + 9.5), "9.5 px below"); down(); up(); expect("expect row logical=4 (forgiving radius)");

            Console.WriteLine("5. click empty space");
            moveTo(new Point(1, 1), "corner"); down(); up(); expect("expect empty");

            Console.WriteLine("6. press on LED 5, release on LED 7");
            moveTo(rowSpot.Center, "led 5"); down(); moveTo(otherRow.Center, "led 7"); up(); expect("expect no event");

            Console.WriteLine("7. press on LED, release on empty space");
            moveTo(rowSpot.Center, "led 5"); down(); moveTo(new Point(1, 1), "corner"); up(); expect("expect no event");

            Console.WriteLine("8. press on empty, release on LED");
            moveTo(new Point(1, 1), "corner"); down(); moveTo(rowSpot.Center, "led 5"); up(); expect("expect no event");

            Console.WriteLine("9. ctrl-click an element");
            keybd_event(0x11, 0, 0, UIntPtr.Zero); Pump(100);
            moveTo(element.Center, "element"); down(); up(); expect("expect element add=True");
            keybd_event(0x11, 0, 2, UIntPtr.Zero); Pump(100);

            if (Environment.GetEnvironmentVariable("HARNESS_TWOPARTS") == "1")
            {
                dynamic root = vmRootObj;
                var partList = ((System.Collections.IEnumerable)root.SelectedDevice.Parts).Cast<dynamic>().ToList();
                var ids2 = new System.Collections.Generic.HashSet<Guid>(((System.Collections.IEnumerable)partList.Last().Groups).Cast<dynamic>().Select(g => (Guid)g.Id));
                var element2 = Find(sp => (bool)((dynamic)sp.Group).IsElement && ids2.Contains((Guid)((dynamic)sp.Group).Id));
                var row1 = Find(sp => !(bool)((dynamic)sp.Group).IsElement && !ids2.Contains((Guid)((dynamic)sp.Group).Id) && sp.Logical == 2);
                Console.WriteLine("11. click an element of the second part");
                moveTo(element2.Center, "element of part 2"); down(); up(); expect("expect selectedPart=Buttons, inspector Elements, part=Buttons");
                Console.WriteLine("12. click an LED of a row of the first part");
                moveTo(row1.Center, "row led 3 of part 1"); down(); up(); expect("expect selectedPart=first part, inspector Group");
                Console.WriteLine("13. click empty space");
                moveTo(new Point(1, 1), "corner"); down(); up(); expect("expect empty, inspector None, part kept");
            }

            Console.WriteLine("10. mouse capture state after the clicks: " + System.Windows.Input.Mouse.Captured);
            moveTo(new Point(-300, -300), "outside left");
            Console.WriteLine("  hover cleared after leaving: " + (type.GetField("hovered", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(preview) == null));
        }
        finally
        {
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
            SetCursorPos(saved.X, saved.Y);
            window.Topmost = false;
        }
    }

    static DependencyObject preview0(FrameworkElement control) => (DependencyObject)control.FindName("Preview");

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
    }

    static void Save(FrameworkElement element, string path, double zoom = 1)
    {
        element.UpdateLayout();
        var w0 = (int)Math.Ceiling(element.ActualWidth);
        var h0 = (int)Math.Ceiling(element.ActualHeight);
        var w = (int)Math.Ceiling(w0 * zoom);
        var h = (int)Math.Ceiling(h0 * zoom);
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(zoom, zoom));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26)), null, new Rect(0, 0, w0, h0));
            dc.DrawRectangle(new VisualBrush(element) { Viewbox = new Rect(0, 0, w0, h0), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, w0, h0));
            dc.Pop();
        }
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path)) enc.Save(f);
        Console.WriteLine("saved " + path);
    }

    static System.Collections.Generic.IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var c = VisualTreeHelper.GetChild(root, i);
            if (c is T t) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
    }
}

/// <summary>
/// SimHub as the wizard's steps on the real device see it (HARNESS_HOST=1): the device is added in SimHub, Connect
/// succeeds, nothing is rendered, and inputs are pressed by the harness. The only class compiled against the plugin.
/// </summary>
public sealed class FakeWizardHost : FreeTheLights.UI.IWizardHost
{
    private readonly System.Collections.Generic.HashSet<Guid> connected = new System.Collections.Generic.HashSet<Guid>();

    public static object Create() => new FakeWizardHost();

    public static void Press(object host, string input) => ((FakeWizardHost)host).Inputs.Press(input);

    public bool HasSimHubDevice(string deviceTypeId) => true;

    public System.Collections.Generic.IReadOnlyList<FreeTheLights.Settings.PartConnection> Check(FreeTheLights.Settings.DeviceConfig device) =>
        device.Parts.Select(p => new FreeTheLights.Settings.PartConnection(
            p,
            connected.Contains(p.Id) ? FreeTheLights.Settings.ConnectionState.Connected : FreeTheLights.Settings.ConnectionState.NotConnected,
            connected.Contains(p.Id) ? "FreeTheLights: " + device.Name : "Default profile")).ToList();

    public string Connect(FreeTheLights.Settings.DeviceConfig device, FreeTheLights.Settings.DevicePart part)
    {
        if (part.Channel == FreeTheLights.Settings.LedChannel.Matrix) return "LED matrices are set up in SimHub's matrix editor.";
        connected.Add(part.Id);
        return null;
    }

    public void ShowDevice(FreeTheLights.Settings.DeviceConfig device)
    {
    }

    public void UndoConnections() => connected.Clear();

    public void KeepConnections()
    {
    }

    public void ShowWiringPattern(Guid partId)
    {
    }

    public void ShowConfirmationPattern(Guid deviceId)
    {
    }

    public FreeTheLights.Core.LedColor[] GetFrame(Guid partId) => null;

    public FreeTheLights.Core.InputState Inputs { get; } = new FreeTheLights.Core.InputState();

    public void Post(Action action) => Dispatcher.CurrentDispatcher.Invoke(action);
}
