using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;
using SimHub.Plugins.UI;

namespace FreeTheLights.UI
{
    /// <summary>
    /// The device page's LED wiring of one part, in the same editor as the wizard's wiring step. While it is open, the
    /// device shows the wiring test pattern on that part; edits apply at once, as everywhere on the device page.
    /// </summary>
    public partial class WiringDialog : SHDialogContentBase
    {
        private readonly FreeTheLightsPlugin plugin;
        private readonly Action publish;
        private readonly DeviceConfig device;
        private readonly WiringEditor editor;
        private readonly DispatcherTimer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();

        /// <param name="publish">
        /// Hands the edits to the game loop the way the device page does, so its autosave notices them too (publishing them
        /// here directly would leave the page nothing to save).
        /// </param>
        public WiringDialog(FreeTheLightsPlugin plugin, DeviceConfig device, DevicePart part, Action publish)
        {
            this.plugin = plugin;
            this.publish = publish;
            this.device = device;
            editor = new WiringEditor(part, ChannelLeds(device, part));
            InitializeComponent();
            ShowOk = false;
            ShowCancel = false;
            ShowLowerContentBar = false;
            Heading.Text = device.Parts.Count > 1 ? $"{device.Name} · {part.Name}" : device.Name;
            Editor.DataContext = editor;

            // The edits reach the game loop, and the preview shows what it renders, a few times per second.
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Timer_Tick;
            Loaded += (s, e) =>
            {
                plugin.TestMode.WiringPartId = part.Id;
                timer.Start();
            };
            Unloaded += (s, e) =>
            {
                timer.Stop();
                editor.Detach();
                if (plugin.TestMode.WiringPartId == part.Id) plugin.TestMode.WiringPartId = Guid.Empty;
            };
        }

        /// <summary>The part's SimHub channel's LED count from SimHub's device list (read at start), or 0 when it isn't known.</summary>
        private static int ChannelLeds(DeviceConfig device, DevicePart part)
        {
            var catalog = SimHubCatalog.LoadAsync();
            if (device.Preset?.DeviceTypeId == null || catalog.Status != TaskStatus.RanToCompletion || catalog.Result == null) return 0;
            var info = catalog.Result.FirstOrDefault(d => string.Equals(d.DeviceTypeId, device.Preset.DeviceTypeId, StringComparison.OrdinalIgnoreCase));
            return info?.CountOf(part.Channel) ?? 0;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                publish();
                Editor.UpdatePreview(device, plugin.GetFrame, clock.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // An exception escaping a DispatcherTimer tick would take down SimHub's UI thread.
                plugin.LogErrorThrottled("FreeTheLights: error refreshing the wiring dialog", ex);
            }
        }

        private void Done_Click(object sender, RoutedEventArgs e) => Close(System.Windows.Forms.DialogResult.OK);
    }
}
