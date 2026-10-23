using System.Collections.Generic;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>
    /// What the device wizard needs from SimHub for the steps that work on the real device: setting up its LED profile,
    /// the wiring test pattern and capturing button inputs. Implemented by the wizard's view, so the view model stays
    /// free of SimHub types; without a host those steps are skipped.
    /// </summary>
    public interface IWizardHost
    {
        /// <summary>Whether a device of this SimHub type is added in SimHub's Devices, so its LED profile can be set up.</summary>
        bool HasSimHubDevice(string deviceTypeId);

        /// <summary>Whether each part of <paramref name="device"/> is shown in the active LED profile of its SimHub channel.</summary>
        IReadOnlyList<PartConnection> Check(DeviceConfig device);

        /// <summary>
        /// Shows the part in FreeTheLights' own LED profile of its channel; returns an error message, or null on success.
        /// SimHub's settings as they were before the first Connect are kept, for <see cref="UndoConnections"/>.
        /// </summary>
        string Connect(DeviceConfig device, DevicePart part);

        /// <summary>
        /// Puts SimHub's device settings back as they were before the wizard's first Connect: the device the profile
        /// shows will not be added (the wizard was cancelled, or other hardware picked). Nothing when nothing was connected.
        /// </summary>
        void UndoConnections();

        /// <summary>The device was added: SimHub keeps the profiles the wizard set up.</summary>
        void KeepConnections();

        /// <summary>Has the game loop render <paramref name="device"/> (not saved) until the next call; null = none.</summary>
        void ShowDevice(DeviceConfig device);

        /// <summary>Shows the wiring test pattern on the part with this id instead of its features; <see cref="System.Guid.Empty"/> = none.</summary>
        void ShowWiringPattern(System.Guid partId);

        /// <summary>Shows the confirmation pattern on every part of the device with this id; <see cref="System.Guid.Empty"/> = none.</summary>
        void ShowConfirmationPattern(System.Guid deviceId);

        /// <summary>
        /// The frame the game loop last rendered for the part, as the device shows it; null when there is none (outside
        /// SimHub, or the game loop isn't running). Previews of a pattern show this frame, so they match the device.
        /// </summary>
        Core.LedColor[] GetFrame(System.Guid partId);

        /// <summary>SimHub's inputs (wheel buttons, encoder steps), for assigning them to buttons and dials; null when not available.</summary>
        Core.InputState Inputs { get; }

        /// <summary>Runs <paramref name="action"/> on the UI thread (inputs are reported on SimHub's threads).</summary>
        void Post(System.Action action);
    }
}
