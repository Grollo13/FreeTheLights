using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;
using SimHub.Plugins.UI;

namespace FreeTheLights.UI
{
    /// <summary>SimHub dialog guiding through the basic configuration of a new device.</summary>
    public partial class DeviceWizard : SHDialogContentBase
    {
        private static readonly LedColor[] GroupColors = WiringPattern.Colors;

        private static readonly LedColor ButtonColor = LedColor.Parse("#FFD0D0D0");
        private static readonly LedColor DialColor = LedColor.Parse("#FF00C8FF");

        private readonly IWizardHost host;
        private readonly DispatcherTimer timer;
        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

        public DeviceWizard()
            : this(SimHubWizardHost.Create())
        {
        }

        /// <param name="host">SimHub, for the steps that work on the real device; null skips them.</param>
        public DeviceWizard(IWizardHost host)
        {
            this.host = host;
            ViewModel = new DeviceWizardViewModel(SimHubCatalog.LoadButtonLayout, SimHubCatalog.LoadDashPhoto, host);
            DataContext = ViewModel;
            InitializeComponent();
            LoadSimHubDevices();

            // The wizard has its own button row (Cancel, Back, Next / Add device); SimHub's Ok/Cancel bar would be a second one.
            ShowOk = false;
            ShowCancel = false;
            ShowLowerContentBar = false;

            ViewModel.PreviewChanged += (s, e) => UpdatePreview();
            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(DeviceWizardViewModel.Step)) PreloadPhoto();
                if (e.PropertyName == nameof(DeviceWizardViewModel.Step) || e.PropertyName == nameof(DeviceWizardViewModel.PhotoFile)) ShowPhoto();
                if (e.PropertyName == nameof(DeviceWizardViewModel.Step) || e.PropertyName == nameof(DeviceWizardViewModel.Buttons)) UpdateButtonPreview();
            };
            Placer.PositionsChanged += (s, e) => ViewModel.PhotoPositionsEdited();
            UpdatePreview();

            // Once its SimHub profile can show it, the device is rendered by the game loop while the wizard is open.
            timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += Timer_Tick;
            Loaded += (s, e) => timer.Start();
            Unloaded += (s, e) =>
            {
                timer.Stop();
                StopShowingDevice();
            };
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                host?.ShowDevice(ViewModel.DeviceToRender);
                if (ViewModel.Step == DeviceWizardViewModel.StepProfile) UpdateProfilePreview();
                if (ViewModel.Step == DeviceWizardViewModel.StepWiring) UpdateWiringPreview();
            }
            catch (Exception ex)
            {
                // An exception escaping a DispatcherTimer tick would take down SimHub's UI thread.
                SimHub.Logging.Current.Error("FreeTheLights: device wizard refresh failed", ex);
                timer.Stop();
            }
        }

        private void StopShowingDevice()
        {
            try
            {
                ViewModel.Closed();
                host?.ShowWiringPattern(Guid.Empty);
                host?.ShowConfirmationPattern(Guid.Empty);
                host?.ShowDevice(null);

                // Closed without "Add device": SimHub's profiles go back to how they were (after Add device, nothing to undo).
                host?.UndoConnections();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: could not stop showing the wizard's device", ex);
            }
        }

        public DeviceWizardViewModel ViewModel { get; }

        /// <summary>
        /// Adds SimHub's devices to the list once they are read (in the background, cached for the session). The result
        /// is handed to the UI thread explicitly; nothing may escape, an exception here would end SimHub's UI thread.
        /// </summary>
        private void LoadSimHubDevices()
        {
            SimHubCatalog.LoadAsync().ContinueWith(task =>
            {
                var devices = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
                if (task.Exception != null) SimHub.Logging.Current.Error("FreeTheLights: could not read SimHub's device list", task.Exception.GetBaseException());
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        ViewModel.SetSimHubDevices(devices);
                    }
                    catch (Exception ex)
                    {
                        SimHub.Logging.Current.Error("FreeTheLights: could not show SimHub's device list", ex);
                    }
                }));
            }, TaskScheduler.Default);
        }

        private void Finish_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            // The device is added: SimHub keeps the profiles set up for it.
            try
            {
                host?.KeepConnections();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: could not keep the wizard's SimHub setup", ex);
            }

            Close(System.Windows.Forms.DialogResult.OK);
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            StopShowingDevice();
            Close(System.Windows.Forms.DialogResult.Cancel);
        }

        /// <summary>
        /// The kind step: starts finding the LEDs on the device's picture in the background, while the user picks the
        /// kind. Leaving the step for a dashboard needs the result (the view model reads it then), which takes a moment;
        /// it is usually ready by then. Not done while browsing the device list: that would read every picture passed.
        /// </summary>
        private void PreloadPhoto()
        {
            if (ViewModel.Step != DeviceWizardViewModel.StepKind || ViewModel.Preset?.ProductImageFile == null) return;
            SimHubCatalog.LoadDashPhotoAsync(ViewModel.Preset);
        }

        /// <summary>The LED positions step: the dashboard's picture with the groups of the device as built (with the wiring step's edits).</summary>
        private void ShowPhoto()
        {
            if (ViewModel.Step != DeviceWizardViewModel.StepPhoto) return;
            Placer.Show(PreviewLayout.LoadPhoto(ViewModel.PhotoFile), ViewModel.PhotoGroups);
        }

        /// <summary>
        /// What the game loop rendered for the part, so a preview matches the device (the patterns move with the game loop's
        /// clock); null when it renders nothing, the preview then draws the pattern itself.
        /// </summary>
        private LedColor[] RenderedFrame(Guid partId) => host?.GetFrame(partId);

        /// <summary>The profile step: the connected parts breathe green, as on the device.</summary>
        private void UpdateProfilePreview()
        {
            var device = ViewModel.Build();
            var connected = new HashSet<Guid>(ViewModel.Profile.ConnectedPartIds);
            var now = clock.ElapsedMilliseconds;
            var frames = device.Parts.Where(p => connected.Contains(p.Id)).ToDictionary(p => p.Id, p => RenderedFrame(p.Id) ?? ConfirmationPattern.Render(p, now));
            ProfilePreview.Update(device, id => frames.TryGetValue(id, out var frame) ? frame : null);
            ProfilePreview.RefreshFrames();
        }

        /// <summary>The wiring step: the same test pattern as on the device, mapped through the configured layout.</summary>
        private void UpdateWiringPreview()
        {
            WiringEditor.UpdatePreview(ViewModel.Build(), RenderedFrame, clock.ElapsedMilliseconds);
        }

        /// <summary>The buttons step: the button to press white and circled, assigned ones green.</summary>
        private void UpdateButtonPreview()
        {
            var buttons = ViewModel.Buttons;
            if (ViewModel.Step != DeviceWizardViewModel.StepButtons || buttons == null) return;
            var frames = buttons.Device.Parts.ToDictionary(p => p.Id, buttons.Frame);
            ButtonPreview.Update(buttons.Device, id => frames.TryGetValue(id, out var frame) ? frame : null);
            var current = buttons.Current;
            ButtonPreview.SetSelection(current == null ? null : new[] { current.Id }, current?.Id, 0);
            ButtonPreview.RefreshFrames();
        }

        /// <summary>Shows the resulting device with one color per group so the wiring order is visible.</summary>
        private void UpdatePreview()
        {
            var device = ViewModel.Build();
            var frames = device.Parts.ToDictionary(p => p.Id, PreviewFrame);
            Preview.Update(device, id => frames.TryGetValue(id, out var frame) ? frame : null);
            PlatePreview.Update(device, id => frames.TryGetValue(id, out var frame) ? frame : null);
            UpdateButtonPreview();
        }

        private static LedColor[] PreviewFrame(DevicePart part)
        {
            var frame = new LedColor[part.TotalLeds];
            var groups = part.Groups.Where(g => !g.IsElement).ToList();
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var color = GroupColors[g % GroupColors.Length];
                for (var i = 0; i < group.LedCount; i++)
                {
                    // The first wired LED of each group is brightest, so the wiring direction is visible too.
                    frame[group.StartIndex + i] = color.Scale(i == 0 ? 1.0 : 0.45);
                }
            }

            // Front plate elements: one color per element type.
            foreach (var element in part.Elements)
            {
                frame[element.StartIndex] = element.ElementType == ElementType.Dial ? DialColor : ButtonColor;
            }

            return frame;
        }
    }
}
