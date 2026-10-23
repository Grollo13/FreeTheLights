using System;
using System.Collections.Generic;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;

namespace FreeTheLights.UI
{
    /// <summary>The device wizard's access to SimHub, through the running plugin.</summary>
    public sealed class SimHubWizardHost : IWizardHost
    {
        private readonly FreeTheLightsPlugin plugin;

        /// <summary>SimHub's settings of each device the wizard connected, as they were before (by instance id).</summary>
        private readonly Dictionary<string, DeviceSettingsCopy> originals = new Dictionary<string, DeviceSettingsCopy>();

        private SimHubWizardHost(FreeTheLightsPlugin plugin)
        {
            this.plugin = plugin;
        }

        /// <summary>The host of the running plugin, or null outside SimHub.</summary>
        public static IWizardHost Create() => FreeTheLightsPlugin.Instance == null ? null : new SimHubWizardHost(FreeTheLightsPlugin.Instance);

        public bool HasSimHubDevice(string deviceTypeId) => SimHubConnector.HasDevice(plugin.PluginManager, deviceTypeId);

        public IReadOnlyList<PartConnection> Check(DeviceConfig device) => SimHubConnector.Check(plugin.PluginManager, device);

        public string Connect(DeviceConfig device, DevicePart part)
        {
            try
            {
                // SimHub's settings before the wizard changed them, once per SimHub device, for UndoConnections.
                foreach (var copy in SimHubConnector.Copy(plugin.PluginManager, device))
                {
                    if (!originals.ContainsKey(copy.InstanceId)) originals[copy.InstanceId] = copy;
                }

                return SimHubConnector.Connect(plugin.PluginManager, device, part, plugin.BackupDirectory);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: connecting to SimHub failed", ex);
                return "Connecting failed: " + ex.Message;
            }
        }

        public void UndoConnections()
        {
            if (originals.Count == 0) return;
            try
            {
                SimHubConnector.Restore(originals.Values);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: could not restore SimHub's device settings after the wizard", ex);
            }

            originals.Clear();
        }

        public void KeepConnections() => originals.Clear();

        public void ShowDevice(DeviceConfig device)
        {
            plugin.WizardDevice = device;
            plugin.PublishDevices();
        }

        public void ShowWiringPattern(Guid partId) => plugin.TestMode.WiringPartId = partId;

        public void ShowConfirmationPattern(Guid deviceId) => plugin.TestMode.ConfirmationDeviceId = deviceId;

        public LedColor[] GetFrame(Guid partId) => plugin.GetFrame(partId);

        public InputState Inputs => plugin.Inputs;

        public void Post(Action action)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    // An exception escaping a dispatcher callback would take down SimHub's UI thread.
                    plugin.LogErrorThrottled("FreeTheLights: error assigning an input in the wizard", ex);
                }
            }));
        }
    }
}
