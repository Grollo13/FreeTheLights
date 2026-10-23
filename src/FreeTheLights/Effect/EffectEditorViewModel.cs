using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;

namespace FreeTheLights.Effect
{
    /// <summary>Editor of a FreeTheLights effect in SimHub's LED editor: device → part (LED channel) → matrix group.</summary>
    public class EffectEditorViewModel : ObservableObject
    {
        private readonly IFreeTheLightsEffect effect;

        private readonly ProfileOwner owner;

        public EffectEditorViewModel(IFreeTheLightsEffect effect)
        {
            this.effect = effect;
            try
            {
                owner = SimHubConnector.FindOwner(FreeTheLightsPlugin.Instance?.PluginManager, effect.ParentProfileId);
            }
            catch (Exception)
            {
                // Without the owner there is just no hint.
            }

            if (effect.DeviceId == Guid.Empty || SelectedDevice == null) EffectHelper.AssignMatchingPart(effect);
            UseMatchingCommand = new UI.RelayCommand(_ => UseMatching(), _ => Matching.part != null);
        }

        /// <summary>Which SimHub device and channel this LED profile belongs to, e.g. "SimHub device: Cube Controls F-PRO, buttons".</summary>
        public string OwnerText => owner == null ? null : $"This LED profile belongs to SimHub's {owner.Name} ({ChannelName(owner.Channel)}).";

        /// <summary>
        /// Warning when the selected FreeTheLights device/part isn't the one made for this SimHub device and channel,
        /// e.g. a wheel's buttons shown on a dashboard.
        /// </summary>
        public string MismatchWarning
        {
            get
            {
                if (owner == null) return null;
                var (device, part) = Matching;
                if (part != null && (SelectedDevice != device || SelectedPart != part))
                {
                    return $"It shows {Describe(SelectedDevice, SelectedPart)}, but \"{Describe(device, part)}\" was made for it.";
                }

                var preset = SelectedDevice?.Preset;
                if (part == null && preset != null && !string.Equals(preset.DeviceTypeId, owner.DeviceTypeId, StringComparison.OrdinalIgnoreCase))
                {
                    return $"It shows {SelectedDevice.Name}, which was made for SimHub's {preset.ProductName}.";
                }

                return null;
            }
        }

        public bool HasMismatch => MismatchWarning != null;

        public bool CanUseMatching => HasMismatch && Matching.part != null;

        public string UseMatchingText => Matching.part == null ? string.Empty : $"Show {Describe(Matching.device, Matching.part)}";

        public System.Windows.Input.ICommand UseMatchingCommand { get; }

        /// <summary>The FreeTheLights part made for this profile's SimHub device and channel.</summary>
        private (DeviceConfig device, DevicePart part) Matching => owner == null ? (null, null) : SimHubProfiles.FindPartFor(Devices, owner.DeviceTypeId, owner.Channel);

        private void UseMatching()
        {
            var (device, part) = Matching;
            if (part == null) return;
            SelectedDevice = device;
            SelectedPart = part;
        }

        private static string Describe(DeviceConfig device, DevicePart part)
        {
            if (device == null) return "nothing";
            return device.Parts.Count > 1 && part != null ? $"{device.Name} / {part.Name}" : device.Name;
        }

        private static string ChannelName(LedChannel channel)
        {
            switch (channel)
            {
                case LedChannel.Buttons: return "button LEDs";
                case LedChannel.Encoders: return "encoder LEDs";
                case LedChannel.Matrix: return "LED matrix";
                default: return "LEDs";
            }
        }

        public bool PluginRunning => FreeTheLightsPlugin.Instance != null;

        public bool IsMatrixEffect => effect.IsMatrixEffect;

        public IList<DeviceConfig> Devices => FreeTheLightsPlugin.Instance?.Settings?.Devices.ToList() ?? new List<DeviceConfig>();

        public DeviceConfig SelectedDevice
        {
            get => Devices.FirstOrDefault(d => d.Id == effect.DeviceId);
            set
            {
                effect.DeviceId = value?.Id ?? Guid.Empty;
                effect.PartId = Guid.Empty;
                effect.MatrixGroupId = Guid.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Parts));
                OnPropertyChanged(nameof(HasSeveralParts));
                PartChanged();
            }
        }

        public IList<DevicePart> Parts => SelectedDevice?.Parts.ToList() ?? new List<DevicePart>();

        /// <summary>The part picker is only shown when there is a choice.</summary>
        public bool HasSeveralParts => Parts.Count > 1;

        public DevicePart SelectedPart
        {
            get => SelectedDevice?.FindPart(effect.PartId);
            set
            {
                effect.PartId = value?.Id ?? Guid.Empty;
                effect.MatrixGroupId = Guid.Empty;
                PartChanged();
            }
        }

        public IList<LedGroup> MatrixGroups => SelectedPart?.Groups.Where(g => g.Kind == LedGroupKind.Matrix).ToList() ?? new List<LedGroup>();

        public LedGroup SelectedMatrixGroup
        {
            get => SelectedPart?.FindMatrixGroup(effect.MatrixGroupId);
            set
            {
                effect.MatrixGroupId = value?.Id ?? Guid.Empty;
                OnPropertyChanged();
            }
        }

        public string Summary
        {
            get
            {
                var device = SelectedDevice;
                if (device == null) return "Select the FreeTheLights device this hardware represents.";
                var part = SelectedPart;
                var name = device.Parts.Count > 1 ? $"{device.Name} / {part?.Name}" : device.Name;
                return $"{name}: {part?.TotalLeds ?? 0} LEDs. Set it up on the FreeTheLights page.";
            }
        }

        private void PartChanged()
        {
            OnPropertyChanged(nameof(MismatchWarning));
            OnPropertyChanged(nameof(HasMismatch));
            OnPropertyChanged(nameof(CanUseMatching));
            OnPropertyChanged(nameof(SelectedPart));
            OnPropertyChanged(nameof(MatrixGroups));
            OnPropertyChanged(nameof(SelectedMatrixGroup));
            OnPropertyChanged(nameof(Summary));
        }
    }
}
