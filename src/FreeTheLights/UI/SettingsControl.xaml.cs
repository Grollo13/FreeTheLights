using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;
using SimHub.Plugins.UI;

namespace FreeTheLights.UI
{
    public partial class SettingsControl : UserControl
    {
        private readonly FreeTheLightsPlugin plugin;
        private readonly SettingsViewModel viewModel;
        /// <summary>How long the editing must pause before the settings are saved.</summary>
        private const int AutoSaveDelayMs = 2000;

        private readonly DispatcherTimer refreshTimer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Debouncer autoSave = new Debouncer(AutoSaveDelayMs);
        private int tick;

        public SettingsControl(FreeTheLightsPlugin plugin)
        {
            this.plugin = plugin;
            viewModel = new SettingsViewModel(plugin);
            DataContext = viewModel;
            InitializeComponent();

            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SettingsViewModel.SelectedDevice)) RefreshConnections();
            };
            Loaded += (s, e) => RefreshConnections();

            // The preview and the inspector next to it show the same selection.
            Preview.LedClicked += Preview_LedClicked;
            viewModel.Inspector.SelectionChanged += (s, e) => ShowSelectionInPreview();
            viewModel.Inspector.Post = PostCapturedInput;
            ShowSelectionInPreview();

            // Settings, layout and status: a few times per second. The LED colors: once per screen frame (see OnRendering).
            refreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            refreshTimer.Tick += RefreshTimer_Tick;
            Loaded += (s, e) =>
            {
                viewModel.Attach();
                refreshTimer.Start();
                CompositionTarget.Rendering += OnRendering;
            };
            Unloaded += (s, e) =>
            {
                refreshTimer.Stop();
                CompositionTarget.Rendering -= OnRendering;

                // The settings outlive this page; a hidden or discarded page must not stay subscribed to them.
                viewModel.Detach();
                try
                {
                    PublishSettings();
                }
                catch (Exception ex)
                {
                    plugin.LogErrorThrottled("FreeTheLights: could not publish settings", ex);
                }

                plugin.SaveSettings();
                autoSave.Reset();
            };
        }

        /// <summary>
        /// Once per screen frame: show the game loop's latest frames. Repaints only when there is a new frame, so the
        /// animations run at SimHub's update rate instead of the timer's.
        /// </summary>
        private void OnRendering(object sender, EventArgs e)
        {
            try
            {
                Preview.RefreshFrames();
            }
            catch (Exception ex)
            {
                plugin.LogErrorThrottled("FreeTheLights: error drawing the preview", ex);
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // All settings edits happen on this page: hand changed settings to the render threads, and save them
                // once the editing pauses, so they survive a SimHub crash.
                PublishAndScheduleSave();
                if (autoSave.TakeDue(clock.ElapsedMilliseconds)) plugin.SaveSettings();

                Preview.Update(viewModel.SelectedDevice, plugin.GetFrame);
                ShowVisibleTab();

                if (++tick % 3 == 0) viewModel.RefreshStatus();
            }
            catch (Exception ex)
            {
                // An exception escaping a DispatcherTimer tick would take down SimHub's UI thread.
                plugin.LogErrorThrottled("FreeTheLights: error refreshing the settings page", ex);
            }
        }

        /// <summary>Tabs of features a part can't show are hidden (RPM needs a row of LEDs); never leave one of them selected.</summary>
        private void ShowVisibleTab()
        {
            if (FeatureTabs.SelectedItem is TabItem selected && selected.Visibility == Visibility.Visible) return;
            var first = FeatureTabs.Items.OfType<TabItem>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
            if (first != null && first != FeatureTabs.SelectedItem) FeatureTabs.SelectedItem = first;
        }

        /// <summary>Hands changed settings to the render threads, and saves them once the editing pauses.</summary>
        private void PublishAndScheduleSave()
        {
            if (PublishSettings()) autoSave.Changed(clock.ElapsedMilliseconds);
        }

        /// <summary>Hands changed settings to the render threads; true if something changed.</summary>
        private bool PublishSettings() => plugin.PublishDevices();

        /// <summary>A click on an LED (or on empty space) of the preview selects it in the inspector.</summary>
        private void Preview_LedClicked(object sender, LedClickEventArgs e)
        {
            try
            {
                // Leave the field being edited first, so a half-typed value is applied to the LEDs it was typed for.
                Keyboard.ClearFocus();

                // The feature tabs below follow the part that was clicked.
                if (e.Group != null && e.Part != null && e.Part != viewModel.SelectedPart) viewModel.SelectedPart = e.Part;
                viewModel.Inspector.Select(e.Part ?? viewModel.SelectedPart, e.Group, e.Logical, e.Add);
            }
            catch (Exception ex)
            {
                // An exception escaping an event handler would take down SimHub's UI thread.
                plugin.LogErrorThrottled("FreeTheLights: error selecting an LED", ex);
            }
        }

        /// <summary>Outlines the inspector's selection in the preview: the elements, or the group with its clicked LED.</summary>
        private void ShowSelectionInPreview()
        {
            try
            {
                var inspector = viewModel.Inspector;
                switch (inspector.Mode)
                {
                    case InspectorMode.Elements:
                        Preview.SetSelection(inspector.Elements.Select(element => element.Id).ToList(), inspector.Elements[inspector.Elements.Count - 1].Id, 0);
                        break;
                    case InspectorMode.Group:
                        Preview.SetSelection(new[] { inspector.Group.Id }, inspector.Group.Id, inspector.ClickedLogical);
                        break;
                    default:
                        Preview.SetSelection(null, null, -1);
                        break;
                }
            }
            catch (Exception ex)
            {
                plugin.LogErrorThrottled("FreeTheLights: could not show the selection in the preview", ex);
            }
        }

        private void SelectAllButtons_Click(object sender, RoutedEventArgs e) => SelectAllElements(ElementType.Button);

        private void SelectAllLights_Click(object sender, RoutedEventArgs e) => SelectAllElements(ElementType.Light);

        private void SelectAllDials_Click(object sender, RoutedEventArgs e) => SelectAllElements(ElementType.Dial);

        private void SelectAllElements_Click(object sender, RoutedEventArgs e) => SelectAllElements(null);

        private void SelectAllElements(ElementType? type)
        {
            try
            {
                viewModel.Inspector.SelectAll(viewModel.Inspector.Part ?? viewModel.SelectedPart, type);
            }
            catch (Exception ex)
            {
                plugin.LogErrorThrottled("FreeTheLights: error selecting the front plate elements", ex);
            }
        }

        private void AssignInput_Click(object sender, RoutedEventArgs e) => EditInputs(() => viewModel.Inspector.StartCapture(SlotOf(sender)));

        private void CancelCapture_Click(object sender, RoutedEventArgs e) => EditInputs(() => viewModel.Inspector.CancelCapture());

        private void ClearInput_Click(object sender, RoutedEventArgs e) => EditInputs(() => viewModel.Inspector.ClearInput(SlotOf(sender)));

        /// <summary>The input slot of a button in the inspector's slot list (its data context).</summary>
        private static InputSlot SlotOf(object sender) => ((sender as FrameworkElement)?.DataContext as InputSlotItem)?.Slot ?? InputSlot.Button;

        private void EditInputs(Action edit)
        {
            try
            {
                edit();
            }
            catch (Exception ex)
            {
                plugin.LogErrorThrottled("FreeTheLights: error assigning an input", ex);
            }
        }

        /// <summary>Inputs are captured on SimHub's input threads; the inspector handles them on the UI thread.</summary>
        private void PostCapturedInput(Action handle)
        {
            Dispatcher.BeginInvoke(new Action(() => EditInputs(handle)));
        }

        private async void AddDevice_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var wizard = new DeviceWizard();
                var result = await wizard.ShowDialogWindowAsync(this, DialogOptions.Resizable, 880, 740);
                if (result == System.Windows.Forms.DialogResult.OK) viewModel.AddDevice(wizard.ViewModel.Build());
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: device wizard failed", ex);
            }
        }

        /// <summary>Edits the selected part's LED wiring, with the test pattern on the device, in the wizard's wiring editor.</summary>
        private async void EditWiring_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var device = viewModel.SelectedDevice;
                var part = viewModel.SelectedPart;
                if (device == null || part == null) return;
                await new WiringDialog(plugin, device, part, PublishAndScheduleSave).ShowDialogWindowAsync(this, DialogOptions.Resizable, 900, 620);

                // The wiring may have changed: save it now, not only when the editing pauses.
                PublishSettings();
                plugin.SaveSettings();
                autoSave.Reset();
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: wiring dialog failed", ex);
            }
        }

        /// <summary>Edits where a dashboard's LEDs are on SimHub's picture (or switches to the generic drawing).</summary>
        private async void PlaceLeds_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var device = viewModel.SelectedDevice;
                if (!PhotoLedDialog.CanEdit(device)) return;
                await new PhotoLedDialog(device).ShowDialogWindowAsync(this, DialogOptions.Resizable, 920, 600);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: LED positions dialog failed", ex);
            }
        }

        /// <summary>
        /// Updates a device made from a SimHub preset to SimHub's current data (its copy is never updated automatically).
        /// Asks before anything is removed.
        /// </summary>
        private async void RefreshFromSimHub_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var device = viewModel.SelectedDevice;
                if (device?.Preset == null) return;
                var catalog = await SimHubCatalog.LoadAsync();
                var info = catalog.FirstOrDefault(d => string.Equals(d.DeviceTypeId, device.Preset.DeviceTypeId, StringComparison.OrdinalIgnoreCase));
                if (info == null)
                {
                    MessageBox.Show($"SimHub doesn't list the {device.Preset.ProductName} any more; the device keeps its current layout.", "FreeTheLights", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var photo = device.Type == DeviceType.Dashboard ? await SimHubCatalog.LoadDashPhotoAsync(info) : null;

                // Removed while the picture was read: nothing to refresh.
                if (!viewModel.Devices.Contains(device)) return;
                var fresh = PresetBuilder.Build(info, device.Type, SimHubCatalog.LoadButtonLayout(info), photo: photo);
                var removed = PresetBuilder.CountRemovedByRefresh(device, fresh);
                if (removed > 0 && MessageBox.Show(
                        $"The layout of the {info.Name} changed in SimHub: {removed} button(s) or part(s) and their settings will be replaced. Continue?",
                        "FreeTheLights",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                var result = PresetBuilder.Refresh(device, fresh);
                viewModel.PartsChanged();
                RefreshConnections();
                MessageBox.Show(result, "FreeTheLights", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: refresh from SimHub failed", ex);
            }
        }

        /// <summary>Checks which parts of the selected (preset) device are shown in their SimHub LED profiles.</summary>
        private void RefreshConnections()
        {
            try
            {
                var device = viewModel.SelectedDevice;
                viewModel.SetConnections(device?.Preset == null ? null : SimHubConnector.Check(plugin.PluginManager, device));
            }
            catch (Exception ex)
            {
                plugin.LogErrorThrottled("FreeTheLights: could not check the SimHub setup", ex);
                viewModel.SetConnections(null);
            }
        }

        private void CheckConnections_Click(object sender, RoutedEventArgs e) => RefreshConnections();

        /// <summary>Adds the FreeTheLights effect for a part to its SimHub LED profile, after asking.</summary>
        private void ConnectPart_Click(object sender, RoutedEventArgs e)
        {
            var device = viewModel.SelectedDevice;
            if (!((sender as FrameworkElement)?.DataContext is PartConnection connection) || device == null) return;
            string plan;
            try
            {
                plan = SimHubConnector.Describe(plugin.PluginManager, device, connection.Part);
            }
            catch (Exception ex)
            {
                plugin.LogErrorThrottled("FreeTheLights: could not read SimHub's LED profiles", ex);
                plan = null;
            }

            if (plan == null)
            {
                MessageBox.Show("SimHub's settings of this device can't be read right now.", "FreeTheLights", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var answer = MessageBox.Show(
                plan + "\n\nYour other LED profiles stay as they are.\n\nContinue?",
                "FreeTheLights",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            try
            {
                var error = SimHubConnector.Connect(plugin.PluginManager, device, connection.Part, plugin.BackupDirectory);
                if (error != null) MessageBox.Show(error, "FreeTheLights", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: connecting to SimHub failed", ex);
                MessageBox.Show("Connecting failed: " + ex.Message, "FreeTheLights", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            RefreshConnections();
        }

        private void RemoveDevice_Click(object sender, RoutedEventArgs e)
        {
            var device = viewModel.SelectedDevice;
            if (device == null) return;

            var answer = MessageBox.Show(
                $"Remove device \"{device.Name}\" and its LED layout?",
                "FreeTheLights",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes) viewModel.RemoveDeviceCommand.Execute(null);
        }
    }
}
